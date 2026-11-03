using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;

namespace CSGOFeel.Movement;

// Opt-in lab patch: clears residual horizontal velocity on the next server tick
// after the player releases every movement key. It is NOT CS:GO inertia and it
// does NOT promise a render-frame latency (a 64-tick server ticks every ~15.6ms).
//
// Two independent gates must both be open before any speed is modified:
//   1. the server process was started with CSGO_FEEL_MOVEMENT_LAB=1
//   2. the affected SteamID is explicitly enabled
// The local player named by CSGO_FEEL_LOCAL_STEAMID counts as enabled, so a
// normal local lab session works without extra console steps.
public sealed class MovementPlugin : BasePlugin
{
    public override string ModuleName => "CSGO Feel - independent movement patch";
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "csgo-feel";
    public override string ModuleDescription =>
        "Opt-in next-server-tick fast stop, local lab player only; not a render-latency guarantee";

    private readonly HashSet<ulong> _enabled = new();
    private long _stops;
    private long _nextStopLog = 1;
    private bool _gateWarned;

    private static bool Lab => Environment.GetEnvironmentVariable("CSGO_FEEL_MOVEMENT_LAB") == "1";

    private static ulong LocalSteamId =>
        ulong.TryParse(Environment.GetEnvironmentVariable("CSGO_FEEL_LOCAL_STEAMID"), out ulong id) ? id : 0;

    public override void Load(bool hotReload)
    {
        AddCommand("css_feelmove", "css_feelmove | css_feelmove <slot> 0|1", Command);
        RegisterListener<Listeners.OnTick>(Tick);

        ulong local = LocalSteamId;
        Server.PrintToConsole(Lab
            ? $"[FeelMove] Gate OPEN. localSteamId={(local == 0 ? "unset" : local)}. " +
              "Stops on the next server tick; not a render frame."
            : "[FeelMove] Gate CLOSED (CSGO_FEEL_MOVEMENT_LAB != 1): velocity is never modified.");
    }

    private void Command(CCSPlayerController? caller, CommandInfo info)
    {
        if (info.ArgCount == 1)
        {
            info.ReplyToCommand($"[FeelMove] gate={(Lab ? "open" : "closed")}, " +
                $"local={LocalSteamId}, manual={_enabled.Count}, stops={_stops}; server tick, not render frame.");
            return;
        }

        // Console-only, and only for the local lab. Ordinary clients cannot turn
        // a gameplay changer on for themselves or for someone else.
        ulong local = LocalSteamId;
        bool self = caller != null && caller.SteamID == local && local != 0;
        if ((!self && !Lan()) || !Lab)
        {
            info.ReplyToCommand("[FeelMove] Local LAN lab server console only.");
            return;
        }

        if (info.ArgCount != 3 || !int.TryParse(info.GetArg(1), out int slot) || info.GetArg(2) is not ("0" or "1"))
        {
            info.ReplyToCommand("Usage: css_feelmove <slot> 0|1");
            return;
        }

        var player = Utilities.GetPlayerFromSlot(slot);
        if (player == null || !player.IsValid || player.IsBot) return;

        if (info.GetArg(2) == "1") _enabled.Add(player.SteamID);
        else _enabled.Remove(player.SteamID);

        info.ReplyToCommand($"[FeelMove] slot={slot}, manual={_enabled.Contains(player.SteamID)}");
    }

    private static bool Lan() => ConVar.Find("sv_lan")?.GetPrimitiveValue<bool>() == true;

    private void Tick()
    {
        if (!Lab)
        {
            if (!_gateWarned)
            {
                _gateWarned = true;
                Server.PrintToConsole("[FeelMove] Tick running but gate closed; nothing is changed.");
            }
            return;
        }

        ulong local = LocalSteamId;
        foreach (var player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot) continue;
            if (!_enabled.Contains(player.SteamID) && !(local != 0 && player.SteamID == local)) continue;

            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            var buttons = player.Buttons;
            bool moving = (buttons & (PlayerButtons.Forward | PlayerButtons.Back |
                PlayerButtons.Moveleft | PlayerButtons.Moveright)) != 0;
            var velocity = pawn.AbsVelocity;

            if (!MovementPolicy.ShouldStop(player.PawnIsAlive, (pawn.Flags & 1u) != 0,
                pawn.MoveType == MoveType_t.MOVETYPE_WALK, true, moving,
                (buttons & PlayerButtons.Jump) != 0, velocity.X, velocity.Y)) continue;

            // No NextFrame, no timers, no position teleport, no vertical change,
            // no speed increase. Only residual horizontal drift is cleared.
            pawn.Teleport(velocity: new System.Numerics.Vector3(0f, 0f, velocity.Z));
            ++_stops;

            if (_stops >= _nextStopLog)
            {
                _nextStopLog = _stops < 10 ? _stops + 1 : _stops + 500;
                Server.PrintToConsole($"[FeelMove] stop #{_stops} ({player.PlayerName})");
            }
        }
    }

    public override void Unload(bool hotReload) => _enabled.Clear();
}
