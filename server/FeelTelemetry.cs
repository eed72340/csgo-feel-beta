using System.IO.MemoryMappedFiles;
using CounterStrikeSharp.API.Core;

namespace CSGOFeel;

// Separate from the old 80-byte control protocol. Single server-thread writer;
// sequence is odd during publication. Never identify a player by connection order.
//
// Layout is fixed at 128 bytes and shared with client/src/telemetry.h:
//   0 magic / 4 version / 8 sequence / 12 pid
//   16 tick / 24 steamId
//   32 vx / 36 vy / 40 vz / 44 yaw
//   48 flags / 52 alive / 56 enabled / 60 bob
//   64 lower / 68 lat / 72 vert / 76 cycle / 80 shiftLeft / 84 shiftRight
//   88 qieqiangAmount / 92 qieqiangTime / 96 animFlags
//   100 fireSeq / 104 switchSeq / 108 recoilScale / 112 punchDecay
//   116 punchPitch / 120 punchYaw / 124 punchFlags
//
// v2 adds the animation block (88..116). A v1 client just sees zeros there and keeps
// the old bob-only behaviour, so an old client against a new server stays safe.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class FeelTelemetry : IDisposable
{
    private const uint Version = 2;
    private MemoryMappedFile? _file;
    private MemoryMappedViewAccessor? _view;
    private int _sequence;

    public void Publish(CCSPlayerController player, PlayerPrefs prefs, uint fireSeq, uint switchSeq,
                        bool punchOk, float punchPitch, float punchYaw)
    {        _file ??= MemoryMappedFile.CreateOrOpen(@"Local\csgo_feel_telemetry_v1", 128);
        _view ??= _file.CreateViewAccessor(0, 128);
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) { Invalidate(); return; }
        var v = _view;
        v.Write(8, ++_sequence);
        Thread.MemoryBarrier();
        v.Write(0, 0x544C4546u);
        v.Write(4, Version);
        v.Write(12, Environment.ProcessId);
        v.Write(16, Environment.TickCount64);
        v.Write(24, player.SteamID);
        v.Write(32, pawn.AbsVelocity.X);
        v.Write(36, pawn.AbsVelocity.Y);
        v.Write(40, pawn.AbsVelocity.Z);
        v.Write(44, pawn.EyeAngles.Y);
        v.Write(48, pawn.Flags);
        v.Write(52, player.PawnIsAlive ? 1 : 0);
        v.Write(56, prefs.Enabled ? 1 : 0);
        v.Write(60, prefs.Bob ? 1 : 0);
        v.Write(64, (float)prefs.Lower);
        v.Write(68, (float)prefs.Lat);
        v.Write(72, (float)prefs.Vert);
        v.Write(76, (float)prefs.Cycle);
        v.Write(80, (float)prefs.ShiftLeft);
        v.Write(84, (float)prefs.ShiftRight);

        // ---- v2：动画参数
        v.Write(88, (float)Math.Clamp(prefs.QieqiangAmount, 0.0, 2.5));
        v.Write(92, (float)Math.Clamp(prefs.QieqiangTime, 0.10, 2.0));
        v.Write(96, AnimFlags(prefs));

        // 上扬只认「真实开火事件」的计数，幅度在客户端按 viewmodel_recoil_scale 缩放。
        // 不在服务端按武器表编后坐角度——那需要每把枪的实测数据，本仓库没有。
        v.Write(100, fireSeq);
        v.Write(104, switchSeq);
        v.Write(108, (float)Math.Clamp(prefs.Scale, 0.0, 3.0));
        v.Write(112, (float)Math.Clamp(prefs.PunchDecay, 5, 60));

        // 真实后坐角由调用方读好传进来（每 tick 只读一次 schema）。
        // 拿不到就置 0 并清有效位，客户端退回「开火计数」那条近似路径。
        if (punchOk)
        {
            v.Write(116, punchPitch);
            v.Write(120, punchYaw);
            v.Write(124, PunchFlags.Live);
        }
        else
        {
            v.Write(116, 0.0f);
            v.Write(120, 0.0f);
            v.Write(124, 0u);
        }
        Thread.MemoryBarrier();
        v.Write(8, ++_sequence);
    }

    /// <summary>bit0 切枪动画 / bit1 枪口上扬 / bit2 枪模上扬。与 telemetry.h 的 kAnim* 对应。</summary>
    private static uint AnimFlags(PlayerPrefs p)
    {
        uint flags = 0;
        if (p.Qieqiang) flags |= 1u << 0;
        if (p.Shangyang) flags |= 1u << 1;
        if (p.ViewmodelRecoil) flags |= 1u << 2;
        return flags;
    }

    public void Invalidate() => _view?.Write(0, 0u);
    public void Dispose()
    {
        Invalidate();
        _view?.Dispose(); _file?.Dispose();
        _view = null; _file = null;
    }
}

/// <summary>punchFlags 位定义，与 telemetry.h 的 kPunchLive 对应。</summary>
internal static class PunchFlags
{
    public const uint Live = 1u << 0;
}
