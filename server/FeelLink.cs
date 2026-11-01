using System.IO.MemoryMappedFiles;

namespace CSGOFeel;

/// <summary>
/// 服务端插件到客户端 DLL（CSGOFeelClient.dll）的唯一通道。
///
/// 为什么不是 ConVar 或 ExecuteClientCommand：
///   <list type="bullet">
///   <item>客户端 DLL 是注入进去的，没有注册 ConVar 的能力，服务端改不了它的开关；</item>
///   <item>很多服务器（包括 Valve 官方服的策略）会拦截服务端下发的客户端指令，
///         走 ExecuteClientCommand 在部分环境里根本不生效。</item>
///   </list>
/// 共享内存对两边都是本机进程内的一次普通内存读写，没有中间层可拦截。
///
/// 布局必须和 client/src/state.h 的 SharedState 逐字节一致（共 80 字节），
/// 每个字段只有一个写者（服务端字段由服务端写、客户端字段由客户端写），所以不需要加锁。
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class FeelLink
{
    private const string MapName = @"Local\csgo_feel_v2";
    private const int MapSize = 80;

    private const uint Magic = 0x4C454546; // 'FEEL'
    private const uint Version = 3;
    private const uint Build = 102;

    private const int OffMagic = 0;
    private const int OffVersion = 4;
    private const int OffEnabled = 8;
    private const int OffQieqiang = 12;
    private const int OffAmount = 16;
    private const int OffTime = 20;
    private const int OffBob = 24;
    private const int OffSwitchSeq = 28;
    private const int OffServerTick = 32;
    private const int OffServerPid = 40;
    private const int OffServerBuild = 44;
    private const int OffClientTick = 48;
    private const int OffClientPid = 56;
    private const int OffClientFlags = 60;
    private const int OffReqSeq = 64;
    private const int OffReqCmd = 68;
    private const int OffReqAck = 72;

    /// <summary>客户端上报的能力位，和 state.h 里的 kFlag* 对应。</summary>
    public const uint FlagWindowHook = 1u << 0;
    public const uint FlagKeyInput = 1u << 1;
    public const uint FlagLink = 1u << 2;

    /// <summary>客户端请求的命令，和 state.h 里的 kReq* 对应。</summary>
    public const uint ReqToggleQieqiang = 1u;
    public const uint ReqToggleFeel = 2u;

    private static MemoryMappedFile? _file;
    private static MemoryMappedViewAccessor? _view;
    private static readonly object Gate = new();

    public static string? LastError { get; private set; }

    private static bool EnsureOpen()
    {
        if (_view != null) return true;

        lock (Gate)
        {
            if (_view != null) return true;
            try
            {
                // CreateOrOpen：谁先起谁创建，另一个直接打开同一个对象
                _file = MemoryMappedFile.CreateOrOpen(MapName, MapSize, MemoryMappedFileAccess.ReadWrite);
                _view = _file.CreateViewAccessor(0, MapSize, MemoryMappedFileAccess.ReadWrite);

                // 注意：这里刻意不写 magic / 心跳。
                // 只有真正 Publish 过玩家偏好之后，客户端才认为「服务端在线」，
                // 否则刚加载插件的那一瞬间会用全 0 的页面把玩家的切枪动画关掉。
                _view.Write(OffVersion, Version);
                _view.Write(OffServerPid, (uint)Environment.ProcessId);
                _view.Write(OffServerBuild, Build);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _view?.Dispose();
                _file?.Dispose();
                _view = null;
                _file = null;
                return false;
            }
        }
    }

    /// <summary>只建立通道，不发布任何偏好（插件加载时调用）。</summary>
    public static bool Open() => EnsureOpen();

    /// <summary>把当前玩家的偏好写进共享内存，客户端 200ms 内生效。</summary>
    public static void Publish(bool enabled, bool qieqiang, float amount, float time, bool bob = true)
    {
        if (!EnsureOpen()) return;

        try
        {
            var v = _view!;
            v.Write(OffEnabled, enabled ? 1 : 0);
            v.Write(OffQieqiang, qieqiang ? 1 : 0);
            v.Write(OffAmount, amount);
            v.Write(OffTime, time);
            v.Write(OffBob, bob ? 1 : 0);
            v.Write(OffMagic, Magic);
            v.Write(OffServerTick, (ulong)Environment.TickCount64);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>报告一次真实的换枪，客户端据此播一次切枪动画（滚轮 / 数字键 / 拾取都算）。</summary>
    public static void BumpSwitch()
    {
        if (!EnsureOpen()) return;

        try
        {
            var v = _view!;
            // 还没发布过偏好就不要动 seq：客户端此时也不认为服务端在线
            if (v.ReadUInt32(OffMagic) != Magic) return;

            int seq = v.ReadInt32(OffSwitchSeq);
            v.Write(OffSwitchSeq, seq + 1);
            v.Write(OffServerTick, (ulong)Environment.TickCount64);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    /// <summary>读取客户端心跳。返回 true 表示客户端 DLL 正在运行，且 5 秒内上报过。</summary>
    public static bool TryReadClient(out int ageMs, out uint flags)
    {
        ageMs = -1;
        flags = 0;

        if (!EnsureOpen()) return false;

        try
        {
            var v = _view!;
            ulong tick = v.ReadUInt64(OffClientTick);
            if (tick == 0) return false;

            flags = v.ReadUInt32(OffClientFlags);
            ulong now = (ulong)Environment.TickCount64;
            ageMs = tick > now ? 0 : (int)(now - tick);
            return ageMs <= 5000;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 客户端按 F8 / !qieqiang 时，把请求写在共享内存里，服务端轮询到这里执行。
    /// 这样即使服务器拦截了 bind 指令，按键照样能触发开关并回显。
    /// </summary>
    public static bool TryReadRequest(out int seq, out uint cmd)
    {
        seq = 0;
        cmd = 0;

        if (!EnsureOpen()) return false;

        try
        {
            var v = _view!;
            if (v.ReadUInt32(OffMagic) != Magic) return false;

            seq = (int)v.ReadUInt32(OffReqSeq);
            cmd = v.ReadUInt32(OffReqCmd);
            return seq != 0;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public static void AckRequest(int seq)
    {
        if (_view == null) return;

        try
        {
            _view.Write(OffReqAck, (uint)seq);
        }
        catch (Exception)
        {
            // 回执失败无所谓，客户端只是拿它写日志
        }
    }

    /// <summary>卸载时把 magic 清零：客户端会立刻判定「服务端不在线」，退回 ini 配置。</summary>
    public static void Close()
    {
        lock (Gate)
        {
            try
            {
                if (_view != null)
                {
                    _view.Write(OffServerTick, 0UL);
                    _view.Write(OffMagic, 0u);
                }
            }
            catch (Exception)
            {
                // 关闭阶段的异常没有意义，忽略
            }

            _view?.Dispose();
            _file?.Dispose();
            _view = null;
            _file = null;
        }
    }
}
