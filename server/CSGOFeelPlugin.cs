using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace CSGOFeel;

/// <summary>
/// 每名玩家的"CS:GO 手感"配置。
/// 服务端只负责保存 / 下发，真正的晃动与枪口上扬由客户端执行
/// （客户端插件或 GUI 模拟器）。
/// </summary>
public sealed class PlayerPrefs
{
    public string Preset { get; set; } = "csgo";
    /// <summary>总开关。关闭后所有「CS:GO 手感」项一起停用。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>切枪动画：换枪时枪模先下沉再回弹（客户端执行，这里只开关与幅度）。</summary>
    public bool Qieqiang { get; set; } = true;
    /// <summary>客户端切枪动画的下沉幅度（viewmodel_offset 单位）。</summary>
    public double QieqiangAmount { get; set; } = 0.55;
    /// <summary>客户端切枪动画的回弹时长（秒）。</summary>
    public double QieqiangTime { get; set; } = 0.35;
    public bool Bob { get; set; } = true;
    public bool NewBob { get; set; }
    public double Lower { get; set; } = 21;
    public double Lat { get; set; } = 0.33;
    public double Vert { get; set; } = 0.14;
    public double Cycle { get; set; } = 0.98;
    public double ShiftLeft { get; set; } = 1.5;
    public double ShiftRight { get; set; } = 0.75;
    public bool Shangyang { get; set; } = true;
    public bool ViewmodelRecoil { get; set; } = true;
    public int PunchDecay { get; set; } = 18;
    public double Scale { get; set; } = 1.0;

    public static PlayerPrefs FromPreset(string preset) => preset switch
    {
        "pro" => new PlayerPrefs
        {
            Preset = "pro", Lower = 5, Lat = 0.1, Vert = 0.1,
            ShiftLeft = 0.5, ShiftRight = 0.25
        },
        "cs2" => new PlayerPrefs
        {
            Preset = "cs2", NewBob = true, Lower = 12, Lat = 0.3, Vert = 0.2, Cycle = 1.0
        },
        "off" => new PlayerPrefs
        {
            Preset = "off", Bob = false, Qieqiang = false,
            Shangyang = false, ViewmodelRecoil = false
        },
        _ => new PlayerPrefs { Preset = "csgo" }
    };
}

// CounterStrikeSharp 只跑在 CS2 服务端（Windows / Linux 皆有），但共享内存通道走的是
// Windows 命名对象，这里明确标注，避免 CA1416 把整个调用链都刷成警告。
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CSGOFeelPlugin : BasePlugin
{
    private static class CC
    {
        public static readonly char White = ChatColors.White;
        public static readonly char Gold = ChatColors.Gold;
        public static readonly char Green = ChatColors.Green;
        public static readonly char LightPurple = ChatColors.LightPurple;
        public static readonly char Grey = ChatColors.Grey;
    }

    public override string ModuleName => "CSGO Feel Restore";
    public override string ModuleVersion => "2.3.0-preview";
    public override string ModuleAuthor => "csgo-feel";
    public override string ModuleDescription =>
        "保存并下发 CS:GO 的 cl_bob 全指令 / viewmodel_recoil / cs_go_shangyang / 切枪手感（含客户端动画联动）";

    private readonly Dictionary<ulong, PlayerPrefs> _prefs = new();
    private bool _clientCommandBlocked;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>本机上真正坐在键盘前的那名玩家：共享内存只服务一个人。</summary>
    private ulong _linkSteamId;

    /// <summary>本机玩家的真实换枪次数。客户端靠它触发切枪动画（只认真实事件，不猜按键）。</summary>
    private uint _switchSeq;

    /// <summary>本机玩家的真实开火次数，驱动枪模上扬。</summary>
    private uint _fireSeq;

    /// <summary>上一 tick 是否读到了引擎真实后坐角，以及它的 pitch（给 !csgo 回显用的）。</summary>
    private bool _punchOk;
    private float _punchPitch;
    private readonly FeelTelemetry _telemetry = new();

    private CounterStrikeSharp.API.Modules.Timers.Timer? _linkTimer;

    // 共享内存轮询与去重
    private float _lastLinkPoll;
    private int _lastReqSeq = -1; // -1 = 还没和客户端对齐过，见 OnGameTick
    private DateTime _lastToggleAt = DateTime.MinValue;
    private string _lastToggleSource = string.Empty;

    // F8 是否已经成功下发过（失败会在出生时重试）
    private ulong _boundSteamId;

    private string PrefsPath => Path.Combine(ModuleDirectory, "players.json");

    public override void Load(bool hotReload)
    {
        FeelLog.Init(ModuleDirectory);
        FeelLog.Write($"==== 加载 CSGO Feel v{ModuleVersion}（hotReload={hotReload}）");

        LoadPrefs();
        FeelLog.Write($"players.json 已读：{_prefs.Count} 名玩家");

        AddCommand("css_csgo", "查看当前 CS:GO 手感配置", OnCsgoCommand);
        AddCommand("css_preset", "切换预设: css_preset csgo|pro|cs2|off", OnPresetCommand);
        AddCommand("css_shangyang", "枪口上扬开关: !shangyang 0|1", OnShangyangCommand);
        AddCommand("css_bob", "手臂晃动开关: !bob 0|1", OnBobCommand);
        AddCommand("css_csgocfg", "重新把指令下发到你的客户端", OnCfgCommand);
        AddCommand("csgo_qieqiang", "切枪手感开关: csgo_qieqiang 0|1|toggle", OnQieqiangCommand);
        AddCommand("csgo_feel", "CS:GO 手感总开关: csgo_feel 0|1|toggle", OnFeelCommand);
        AddCommandListener("say", OnChatCommand);
        AddCommandListener("say_team", OnChatCommand);

        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventItemEquip>(OnItemEquip);
        RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        RegisterListener<Listeners.OnTick>(OnGameTick);

        Server.PrintToConsole("[CSGOFeel] plugin loaded. type !csgo in chat.");

        if (!FeelLink.Open())
        {
            Server.PrintToConsole("[CSGOFeel] 共享内存通道未建立：" + (FeelLink.LastError ?? "unknown"));
            FeelLog.Write("共享内存通道建立失败：" + (FeelLink.LastError ?? "unknown"));
        }
        else
        {
            FeelLog.Write("共享内存通道已建立 Local\\csgo_feel_v2（80 字节）");
        }

        // 5 秒心跳：客户端 15 秒收不到服务端心跳就退回 ini，避免服务端挂了跟着卡死
        _linkTimer = AddTimer(5.0f, () =>
        {
            if (_linkSteamId == 0) return;
            PublishLink();
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        FeelLog.Write($"==== 卸载 CSGO Feel v{ModuleVersion}（hotReload={hotReload}）");
        _linkTimer?.Kill();
        _linkTimer = null;
        _telemetry.Dispose();
        FeelLink.Close();
    }

    /// <summary>
    /// 每帧检查客户端有没有按键请求（F8 走这条路，不依赖可能被拦截的 bind 指令）。
    /// 50ms 轮询一次，读的是本机内存，开销可忽略。
    /// </summary>
    private void OnGameTick()
    {
        // Explicit local owner only. A remote/last-connected player is not the local player.
        var owner = FindLinkPlayer();
        bool punchOk = false;
        float punchPitch = 0.0f, punchYaw = 0.0f;
        if (owner != null) punchOk = AimPunchTap.TryRead(owner, out punchPitch, out punchYaw);
        _punchOk = punchOk;
        _punchPitch = punchPitch;
        if (owner != null) _telemetry.Publish(owner, GetPrefs(owner), _fireSeq, _switchSeq, punchOk, punchPitch, punchYaw);
        else _telemetry.Invalidate();
        float now = Server.CurrentTime;
        if (now - _lastLinkPoll < 0.05f) return;
        _lastLinkPoll = now;

        if (!FeelLink.TryReadRequest(out int seq, out uint cmd)) return;

        // 插件热重载后的第一次轮询：客户端可能早就发过请求并被上一份插件处理过了。
        // 直接把当前序号当成「已处理」，否则一 reload 就凭空空翻一次开关。
        if (_lastReqSeq < 0)
        {
            _lastReqSeq = seq;
            FeelLink.AckRequest(seq);
            return;
        }

        if (seq == _lastReqSeq) return;

        _lastReqSeq = seq;
        FeelLink.AckRequest(seq);

        var player = FindLinkPlayer();
        if (player == null)
        {
            FeelLog.Write($"收到客户端按键请求 seq={seq} cmd={cmd}，但找不到对应玩家（link={_linkSteamId}）");
            return;
        }

        FeelLog.Write($"收到客户端按键请求 seq={seq} cmd={cmd}（{player.PlayerName}）");

        if (cmd == FeelLink.ReqToggleQieqiang) SetQieqiang(player, !GetPrefs(player).Qieqiang, "shm");
        else if (cmd == FeelLink.ReqToggleFeel) SetFeel(player, !GetPrefs(player).Enabled, "shm");
    }

    private CCSPlayerController? FindLinkPlayer()
    {
        if (_linkSteamId == 0) return null;
        foreach (var p in Utilities.GetPlayers())
        {
            if (p != null && p.IsValid && !p.IsBot && p.SteamID == _linkSteamId) return p;
        }
        return null;
    }

    /// <summary>
    /// 同一次按键可能同时经「客户端共享内存请求」和「F8 绑定说 !csgo toggle」两条路到达。
    /// 350ms 内来自不同来源的切换直接丢弃，避免按一下翻两次；同一来源连按仍然照常生效。
    /// </summary>
    private bool TryBeginToggle(string source)
    {
        var now = DateTime.UtcNow;
        double gap = (now - _lastToggleAt).TotalMilliseconds;
        if (gap < 350 && _lastToggleSource != source)
        {
            FeelLog.Write($"丢弃重复的切换请求（来源 {source}，{gap:0}ms 前刚由 {_lastToggleSource} 处理过）");
            return false;
        }

        _lastToggleAt = now;
        _lastToggleSource = source;
        return true;
    }

    // ------------------------------------------------------------ 指令

    /// <summary>
    /// 配置回显。只给「启用状态 + 各项手感」，一眼看完。
    /// 客户端心跳、后坐角来源、F8 绑定这些排查信息放到 <c>css_csgo diag</c>。
    /// </summary>
    private void OnCsgoCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        var p = GetPrefs(player);
        if (info.ArgCount > 1 && info.GetArg(1).Equals("diag", StringComparison.OrdinalIgnoreCase))
        {
            PrintCsgoDiagnostics(player, p);
            return;
        }

        PrintCsgoConfig(player, p);
    }

    /// <summary>四项手感：启用状态 + 晃动 / 切枪 / 上扬。</summary>
    private void PrintCsgoConfig(CCSPlayerController player, PlayerPrefs p)
    {
        Reply(player, $"CSGO 手感 {Flag(p.Enabled)} · 预设 {CC.Gold}{PresetName(p.Preset)}");
        Reply(player, $"晃动 {Flag(p.Bob)} · 下沉 {p.Lower:0} · 左右 {p.Lat:0.00} · 上下 {p.Vert:0.00} · 周期 {p.Cycle:0.00} · 横移 {p.ShiftLeft:0.00}/{p.ShiftRight:0.00}");
        Reply(player, $"切枪 {Flag(p.Qieqiang)} · 下沉 {p.QieqiangAmount:0.00} · 回弹 {p.QieqiangTime:0.00}s");
        Reply(player, $"上扬 {Flag(p.Shangyang)} · 枪模 {Flag(p.ViewmodelRecoil)} · 回正 {p.PunchDecay} · 倍率 {p.Scale:0.00}");

        FeelLog.Write($"csgo 配置回显：{player.PlayerName}");
    }

    /// <summary>排查用：后坐角来源、客户端心跳、F8 绑定。</summary>
    private void PrintCsgoDiagnostics(CCSPlayerController player, PlayerPrefs p)
    {
        PrintCsgoConfig(player, p);

        Reply(player, _punchOk
            ? $"后坐角 {CC.Green}真实{CC.White}（AimPunchServices {_punchPitch:+0.00;-0.00;0.00}°）"
            : $"后坐角 {CC.Grey}不可用{CC.White}（AimPunchServices 未就绪，退回开火计数近似）");

        if (FeelLink.TryReadClient(out int ageMs, out uint flags))
        {
            string caps = (flags & FeelLink.FlagWindowHook) != 0 ? "滚轮/侧键已接管" : "仅按键识别";
            Reply(player, $"客户端 {CC.Green}已注入{CC.White}（{ageMs}ms 前心跳 · {caps}）");
        }
        else
        {
            Reply(player, $"客户端 {CC.Grey}未注入{CC.White}（按 csgo_feel.ini 执行）");
        }

        FeelLog.Write($"css_csgo diag：{player.PlayerName}，F8 绑定={(_boundSteamId == player.SteamID ? "已下发" : "未下发")}");
        Reply(player, $"F8 绑定 {Flag(_boundSteamId == player.SteamID)} · {CC.Gold}!preset csgo{CC.White} 一键还原 · {CC.Gold}!csgocfg{CC.White} 重新下发");
    }

    private void OnPresetCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        if (info.ArgCount < 2)
        {
            Reply(player, $"可用预设: {CC.Gold}csgo{CC.White} / {CC.Gold}pro{CC.White} / {CC.Gold}cs2{CC.White} / {CC.Gold}off");
            return;
        }

        TryApplyPreset(player, info.GetArg(1).ToLowerInvariant());
    }

    /// <summary>套用预设。控制台 <c>css_preset</c> 与聊天 <c>!preset</c> 共用。</summary>
    private bool TryApplyPreset(CCSPlayerController player, string key)
    {
        if (key is not ("csgo" or "pro" or "cs2" or "off"))
        {
            Reply(player, "未知预设，可用: csgo / pro / cs2 / off");
            return false;
        }

        var prefs = PlayerPrefs.FromPreset(key);
        _prefs[player.SteamID] = prefs;
        SavePrefs();
        PushToClient(player, prefs);
        PublishLink(player);

        Reply(player, $"已切换到 {CC.Gold}{PresetName(key)}{CC.White}，指令已下发。");
        return true;
    }

    private void OnShangyangCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        var p = GetPrefs(player);

        if (!TryReadToggle(info, 1, p.Shangyang, out bool value))
        {
            Reply(player, $"当前枪口上扬 {Flag(p.Shangyang)}，用法: !shangyang 0|1");
            return;
        }

        p.Shangyang = value;
        SavePrefs();
        PushToClient(player, p);
        Reply(player, $"cs_go_shangyang = {(value ? "1" : "0")}（枪口上扬{(value ? "已恢复" : "已关闭")}）");
    }

    private void OnBobCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        var p = GetPrefs(player);

        if (!TryReadToggle(info, 1, p.Bob, out bool value))
        {
            Reply(player, $"当前手臂晃动 {Flag(p.Bob)}，用法: !bob 0|1");
            return;
        }

        p.Bob = value;
        SavePrefs();
        PushToClient(player, p);
        Reply(player, $"cl_bob = {(value ? "1" : "0")}");
    }

    private void OnCfgCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        PushToClient(player, GetPrefs(player));
        Reply(player, "已把配置重新下发到你的客户端。");
    }

    /// <summary>切枪手感：控制台 <c>csgo_qieqiang 0|1|toggle</c> 与聊天 <c>!qieqiang</c> 共用。</summary>
    private void OnQieqiangCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;

        var p = GetPrefs(player);
        string sub = info.ArgCount >= 2 ? info.GetArg(1).ToLowerInvariant() : string.Empty;

        // csgo_qieqiang amt 0.7 / csgo_qieqiang time 0.3：不碰 ini 也能调客户端动画手感
        bool isTime = sub is "time" or "dur" or "duration";
        bool isAmount = sub is "amt" or "amount";
        if ((isTime || isAmount) && info.ArgCount >= 3)
        {
            if (!double.TryParse(info.GetArg(2), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                Reply(player, $"用法: {CC.Gold}csgo_qieqiang amt 0.7{CC.White} / {CC.Gold}csgo_qieqiang time 0.35");
                return;
            }

            if (isTime) p.QieqiangTime = Math.Clamp(v, 0.10, 2.0);
            else p.QieqiangAmount = Math.Clamp(v, 0.0, 2.5);

            p.Qieqiang = true;
            SavePrefs();
            PublishLink(player);

            Reply(player, isTime
                ? $"切枪回弹 = {CC.Gold}{p.QieqiangTime:0.00}s{CC.White} · 切枪 {Flag(p.Qieqiang)}"
                : $"切枪下沉 = {CC.Gold}{p.QieqiangAmount:0.00}{CC.White} · 切枪 {Flag(p.Qieqiang)}");
            return;
        }

        SetQieqiang(player, TryReadToggle(info, 1, p.Qieqiang, out bool value) ? value : !p.Qieqiang, "console");
    }

    private void SetQieqiang(CCSPlayerController player, bool value, string source = "cmd")
    {
        if (!TryBeginToggle(source)) return;

        var p = GetPrefs(player);
        p.Qieqiang = value;
        SavePrefs();
        PublishLink(player);
        FeelLog.Write($"切枪手感 -> {value}（来源 {source}，玩家 {player.PlayerName}）");
        Reply(player, $"切枪 {Flag(value)}");
    }

    /// <summary>总开关：控制台 <c>csgo_feel 0|1|toggle</c>；F8 绑定到聊天的 <c>!csgo toggle</c>。</summary>
    private void OnFeelCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid) return;
        SetFeel(player, TryReadToggle(info, 1, GetPrefs(player).Enabled, out bool value) ? value : !GetPrefs(player).Enabled,
            "console");
    }

    private void SetFeel(CCSPlayerController player, bool value, string source = "cmd")
    {
        if (!TryBeginToggle(source)) return;

        var p = GetPrefs(player);
        p.Enabled = value;
        SavePrefs();
        PublishLink(player);
        FeelLog.Write($"总开关 -> {value}（来源 {source}，玩家 {player.PlayerName}）");
        Reply(player, $"CSGO 手感 {Flag(value)}");
    }

    /// <summary>聊天里的 <c>!csgo</c> / <c>!qieqiang</c>。命中后不广播原命令。</summary>
    private HookResult OnChatCommand(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        string raw = (info.ArgString ?? string.Empty).Trim();
        if (raw.Length < 2 || raw[0] != '!') return HookResult.Continue;

        string body = raw[1..];
        int space = body.IndexOf(' ');
        string name = (space < 0 ? body : body[..space]).ToLowerInvariant();
        string arg = space < 0 ? string.Empty : body[(space + 1)..].Trim();

        switch (name)
        {
            case "csgo":
            case "feel":
                // 不带参数 = 看配置；带参数 = 切换/设置。F8 绑的是 !csgo toggle，所以开关照旧。
                if (arg.Length == 0)
                {
                    PrintCsgoConfig(player, GetPrefs(player));
                    return HookResult.Handled;
                }
                SetFeel(player, ReadChatToggle(arg, GetPrefs(player).Enabled), "chat");
                return HookResult.Handled;
            case "qieqiang":
                if (arg.Length == 0)
                {
                    Reply(player, $"切枪 {Flag(GetPrefs(player).Qieqiang)}");
                    return HookResult.Handled;
                }
                SetQieqiang(player, ReadChatToggle(arg, GetPrefs(player).Qieqiang), "chat");
                return HookResult.Handled;
            case "preset":
                if (arg.Length == 0)
                {
                    Reply(player, $"当前预设 {CC.Gold}{PresetName(GetPrefs(player).Preset)}{CC.White}，可用: csgo / pro / cs2 / off");
                    return HookResult.Handled;
                }
                TryApplyPreset(player, arg.ToLowerInvariant());
                return HookResult.Handled;
            default:
                return HookResult.Continue;
        }
    }

    private static bool ReadChatToggle(string arg, bool current) => arg switch
    {
        "1" or "on" or "true" or "开" => true,
        "0" or "off" or "false" or "关" => false,
        _ => !current
    };

    /// <summary>
    /// 枪模上扬的触发源：只统计本机玩家的真实开火。
    ///
    /// 服务端拿不到本地子弹的后坐角度（这个 API 版本没有 AimPunchAngle），所以这里只发
    /// 「开了一枪」这件事，幅度由客户端按 viewmodel_recoil_scale 缩放。不按武器表编角度。
    /// </summary>
    private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
        if (player.SteamID != _linkSteamId) return HookResult.Continue;

        var p = GetPrefs(player);
        if (p.Enabled && p.Shangyang && p.ViewmodelRecoil) _fireSeq++;
        return HookResult.Continue;
    }

    /// <summary>切枪动画的触发源。只转发本机玩家的真实换枪事件。</summary>
    private HookResult OnItemEquip(EventItemEquip @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        var p = GetPrefs(player);
        if (!p.Enabled || !p.Qieqiang) return HookResult.Continue;

        // 只有本机这名玩家的换枪才通知客户端播动画：机器人一场能换几百次枪
        if (player.SteamID == _linkSteamId)
        {
            FeelLink.BumpSwitch();
            _switchSeq++;
        }

        // Never rewrite attack readiness using the old unverified deploy table.
        // Native draw/reload/fire timings remain authoritative.
        return HookResult.Continue;
    }

    private static void ApplyDeployTiming(int slot)
    {
        try
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSlot(slot);
            CCSPlayerPawn? pawn = player?.PlayerPawn?.Value;
            CBasePlayerWeapon? weapon = pawn?.WeaponServices?.ActiveWeapon?.Value;
            if (weapon == null || !weapon.IsValid) return;

            float deploy = DeploySeconds(weapon);
            float ready = Server.CurrentTime + deploy;
            bool wrote = WriteFloat(weapon, _nextPrimaryOffset, ready);
            wrote |= WriteFloat(weapon, _nextSecondaryOffset, ready);
            if (wrote) Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_flNextPrimaryAttack");
        }
        catch (Exception)
        {
            // 单个玩家的切枪失败不应影响其他人。
        }
    }

    /// <summary>CS:GO 各武器的 deploy 秒数（切到该武器后可以开火的时间）。</summary>
    private static float DeploySeconds(CBasePlayerWeapon weapon)
    {
        string name = weapon.DesignerName ?? string.Empty;
        if (name.Contains("knife", StringComparison.OrdinalIgnoreCase) || name.Equals("weapon_bayonet", StringComparison.OrdinalIgnoreCase))
            return DeployByName["knife"];

        if (name.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            name = name["weapon_".Length..];

        return DeployByName.TryGetValue(name, out float seconds) ? seconds : DeployByName["default"];
    }

    private static readonly Dictionary<string, float> DeployByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["knife"] = 1.00f,
        ["default"] = 1.00f,

        // 手枪
        ["glock"] = 0.50f,
        ["usp_silencer"] = 0.50f,
        ["hkp2000"] = 0.50f,
        ["p2000"] = 0.50f,
        ["p250"] = 0.50f,
        ["fiveseven"] = 0.50f,
        ["tec9"] = 0.50f,
        ["cz75a"] = 0.50f,
        ["elite"] = 0.50f,
        ["deagle"] = 0.60f,
        ["revolver"] = 0.60f,

        // 冲锋枪
        ["mp9"] = 1.00f,
        ["mp7"] = 1.00f,
        ["mp5sd"] = 1.00f,
        ["mac10"] = 1.00f,
        ["ump45"] = 1.00f,
        ["p90"] = 1.00f,
        ["bizon"] = 1.00f,

        // 步枪
        ["ak47"] = 1.00f,
        ["m4a1"] = 1.00f,
        ["m4a1_silencer"] = 1.00f,
        ["m4a4"] = 1.00f,
        ["sg556"] = 1.00f,
        ["sg553"] = 1.00f,
        ["aug"] = 1.00f,
        ["famas"] = 1.00f,
        ["galilar"] = 1.00f,

        // 狙击枪
        ["ssg08"] = 1.25f,
        ["awp"] = 1.25f,
        ["scar20"] = 1.25f,
        ["g3sg1"] = 1.25f,

        // 霰弹枪
        ["nova"] = 1.00f,
        ["xm1014"] = 1.00f,
        ["sawedoff"] = 1.00f,
        ["mag7"] = 1.00f,

        // 机枪
        ["m249"] = 1.50f,
        ["negev"] = 1.50f,

        // 投掷物 / 拆弹器
        ["fraggrenade"] = 0.50f,
        ["flashbang"] = 0.50f,
        ["smokegrenade"] = 0.50f,
        ["molotov"] = 0.50f,
        ["incgrenade"] = 0.50f,
        ["decoy"] = 0.50f,
        ["tagrenade"] = 0.50f,
        ["breachcharge"] = 0.50f,
        ["snowball"] = 0.50f,
        ["c4"] = 1.00f
    };

    private static int SchemaOffset(string field)
    {
        try
        {
            int offset = Schema.GetSchemaOffset("CBasePlayerWeapon", field);
            return offset > 0 && offset < 0x2000 ? offset : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    // Retired compatibility code: do not query obsolete float attack-time schema.
    private static readonly int _nextPrimaryOffset = -1;
    private static readonly int _nextSecondaryOffset = -1;
    private static readonly bool DeployTimingAvailable = _nextPrimaryOffset > 0;

    private static bool WriteFloat(CEntityInstance entity, int offset, float value)
    {
        if (offset < 0) return false;
        nint handle = entity.Handle;
        if (handle == 0) return false;
        unsafe
        {
            *(float*)(handle + offset) = value;
        }
        return true;
    }

    // ------------------------------------------------------------ 下发

    /// <summary>
    /// 记住「本机这名玩家」，之后所有偏好变化都同步给客户端 DLL。
    /// 共享内存只有一个槽位，服务端只照顾真正坐在键盘前的人；机器人在换枪时不会碰它。
    /// </summary>
    private void MarkLinkPlayer(CCSPlayerController player)
    {
        if (player.IsBot) return;
        // Set CSGO_FEEL_LOCAL_STEAMID before starting a local server.
        // No implicit ownership based on connection order or a remote server.
        if (!ulong.TryParse(Environment.GetEnvironmentVariable("CSGO_FEEL_LOCAL_STEAMID"), out var local)
            || player.SteamID != local) return;
        _linkSteamId = player.SteamID;
        PublishLink(player);
    }

    private void PublishLink(CCSPlayerController? player = null)
    {
        if (player != null && player.SteamID != _linkSteamId) return;
        if (_linkSteamId == 0) return;
        if (!_prefs.TryGetValue(_linkSteamId, out var p)) return;

        FeelLink.Publish(
            p.Enabled,
            p.Qieqiang,
            (float)Math.Clamp(p.QieqiangAmount, 0.0, 2.5),
            (float)Math.Clamp(p.QieqiangTime, 0.10, 2.0), p.Bob);
    }

    /// <summary>
    /// 尝试把 F8 绑到 <c>say !csgo toggle</c>（带参数才是切换，裸 !csgo 是看配置）。
    ///
    /// 注意：CS2 里这个调用不保证生效——实测服务端下发 <c>bind</c> 后，
    /// 客户端退出时写出的 cs2_user_keys_0_slot0.vcfg 里并没有这条绑定，
    /// 而调用本身也不报错。所以 F8 真正的可靠实现是客户端 DLL（共享内存那条链路），
    /// 这里只是尽力而为，别把它当成必然可用。
    /// </summary>
    private void BindToggleKey(CCSPlayerController player, string reason)
    {
        if (_boundSteamId == player.SteamID) return;

        try
        {
            player.ExecuteClientCommand("bind F8 \"say !csgo toggle\"");
            _boundSteamId = player.SteamID;
            FeelLog.Write($"已尝试下发 bind F8（{reason}，玩家 {player.PlayerName}）；" +
                          "CS2 不保证生效，F8 真正可用要靠客户端插件");
        }
        catch (Exception ex)
        {
            FeelLog.Write($"bind F8 下发抛异常（{reason}）：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void PushToClient(CCSPlayerController player, PlayerPrefs p)
    {
        foreach (string cmd in BuildCommands(p))
        {
            try
            {
                player.ExecuteClientCommand(cmd);
            }
            catch (Exception ex)
            {
                // 多数线上服务器会拦截服务器下发客户端指令。十二条指令会全部失败，
                // 只记录第一次，避免每名玩家出生时刷满控制台。
                if (!_clientCommandBlocked)
                {
                    _clientCommandBlocked = true;
                    Server.PrintToConsole(
                        $"[CSGOFeel] 客户端指令下发失败（{cmd}）：{ex.Message}。后续同类错误不再记录。");
                    FeelLog.Write($"客户端指令下发失败（{cmd}）：{ex.GetType().Name}: {ex.Message}；后续不再记录");
                }
                break;
            }
        }
    }

    public static IEnumerable<string> BuildCommands(PlayerPrefs p)
    {
        yield return $"cl_bob {(p.Bob ? 1 : 0)}";
        yield return $"cl_usenewbob {(p.NewBob ? 1 : 0)}";
        yield return $"cl_bob_lower_amt {p.Lower.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"cl_bobamt_lat {p.Lat.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"cl_bobamt_vert {p.Vert.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"cl_bobcycle {p.Cycle.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"cl_viewmodel_shift_left_amt {p.ShiftLeft.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"cl_viewmodel_shift_right_amt {p.ShiftRight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
        yield return $"viewmodel_recoil {(p.ViewmodelRecoil ? 1 : 0)}";
        yield return $"view_punch_decay {p.PunchDecay}";
        yield return $"cs_go_shangyang {(p.Shangyang ? 1 : 0)}";
        yield return $"viewmodel_recoil_scale {p.Scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    // ------------------------------------------------------------ 事件

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        var prefs = GetPrefs(player);
        MarkLinkPlayer(player);
        AddTimer(0.8f, () =>
        {
            if (player.IsValid) PushToClient(player, prefs);
        });

        // 连接时那次绑定可能被拦，出生后再补一次
        AddTimer(2.0f, () =>
        {
            if (player.IsValid) BindToggleKey(player, "spawn+2s");
        });

        return HookResult.Continue;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

        FeelLog.Write($"玩家连入：{player.PlayerName}（{player.SteamID}）");
        MarkLinkPlayer(player);

        AddTimer(6.0f, () =>
        {
            if (!player.IsValid) return;
            BindToggleKey(player, "connectfull+6s");
            player.PrintToChat($" {CC.White}[{CC.Gold}CSGO{CC.White}] 聊天 {CC.Gold}!csgo{CC.White} 看当前手感配置，{CC.Gold}!csgo 0|1{CC.White} 开关；控制台 {CC.Gold}csgo_feel 0|1{CC.White}。装了客户端插件后 F8 也能开关。");
        });
        return HookResult.Continue;
    }

    // ------------------------------------------------------------ 工具

    private PlayerPrefs GetPrefs(CCSPlayerController player)
    {
        if (!_prefs.TryGetValue(player.SteamID, out var p))
        {
            p = new PlayerPrefs();
            _prefs[player.SteamID] = p;
        }
        return p;
    }

    private static bool TryReadToggle(CommandInfo info, int index, bool current, out bool value)
    {
        value = current;
        if (info.ArgCount <= index) return false;

        switch (info.GetArg(index).ToLowerInvariant())
        {
            case "1" or "on" or "true":
                value = true;
                return true;
            case "0" or "off" or "false":
                value = false;
                return true;
            case "toggle":
                value = !current;
                return true;
            default:
                return false;
        }
    }

    private static void Reply(CCSPlayerController player, string text) =>
        player.PrintToChat($" {CC.White}[{CC.Gold}CSGO{CC.White}] {text}");

    private static string Flag(bool on) => on ? $"{CC.Green}开{CC.White}" : $"{CC.Grey}关{CC.White}";

    private static string PresetName(string key) => key switch
    {
        "pro" => "职业哥 · 静音晃动",
        "cs2" => "CS2 原生晃动",
        "off" => "全部关闭",
        _ => "CS:GO 默认"
    };

    private void LoadPrefs()
    {
        try
        {
            if (!File.Exists(PrefsPath)) return;
            var data = JsonSerializer.Deserialize<Dictionary<ulong, PlayerPrefs>>(File.ReadAllText(PrefsPath), _json);
            if (data == null) return;
            foreach (var (id, p) in data) _prefs[id] = p;
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CSGOFeel] load players.json failed: {ex.Message}");
            FeelLog.Write($"读取 players.json 失败（文件可能损坏）：{ex.GetType().Name}: {ex.Message}；" +
                          "本次按默认值运行，原文件保持不动");
        }
    }

    private void SavePrefs()
    {
        try
        {
            Directory.CreateDirectory(ModuleDirectory);

            // 先写临时文件再改名：同卷 rename 是原子的。
            // 直接 WriteAllText 到正式路径时，一旦服务端在半截被杀（crash / kill / 停电），
            // players.json 就是无效 JSON，下次 LoadPrefs 反序列化失败，
            // 所有人的偏好在没有任何提示的情况下被重置。
            string tmp = PrefsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_prefs, _json));
            File.Move(tmp, PrefsPath, true);
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[CSGOFeel] save players.json failed: {ex.Message}");
            FeelLog.Write($"保存 players.json 失败：{ex.GetType().Name}: {ex.Message}");
        }
    }
}


