#define NOMINMAX
#include "bob_apply.h"

#include "bob.h"
#include "cvar.h"
#include "input_hook.h"
#include "log.h"
#include "mem.h"
#include "state.h"

#include <windows.h>
#include <timeapi.h>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

namespace feel
{
namespace
{
// 这些 RVA 来自 client.dll（2026-10-31，39,224,472 字节）：
//   ConVarRef viewmodel_offset_x = 0x2536920，依次 +0x10 是 y / z。
// Steam 每次更新都会变，所以这里只当「快速路径」：对不上就由
// cvar.cpp 的 FindConVarRefByCode 按注册函数的代码特征重新定位（已验证可自愈）。
constexpr uintptr_t kOffsetXRefRva = 0x2536920;
constexpr uintptr_t kOffsetYRefRva = 0x2536930;
constexpr uintptr_t kOffsetZRefRva = 0x2536940;

constexpr float kOffsetMin = -2.0f;
constexpr float kOffsetMax = 2.5f;

struct Runtime
{
    HANDLE thread = nullptr;
    HANDLE stopEvent = nullptr;
    HANDLE instanceMutex = nullptr;
    volatile LONG running = 0;

    FloatCvar offsetX;
    FloatCvar offsetY;
    FloatCvar offsetZ;

    float baseX = 0.0f;
    float baseY = 0.0f;
    float baseZ = 0.0f;

    BobApplySettings settings;
    BobState bob;
    bool applied = false;

    float lastX = 1e9f;
    float lastY = 1e9f;

    int lastLogSecond = -1;
};

Runtime g_rt;

float Clamp(float v, float lo, float hi)
{
    return v < lo ? lo : (v > hi ? hi : v);
}

bool KeyDown(int vk)
{
    return (GetAsyncKeyState(vk) & 0x8000) != 0;
}

std::wstring ModuleDir()
{
    wchar_t path[MAX_PATH]{};
    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       reinterpret_cast<LPCWSTR>(&ModuleDir), &self);
    if (self && GetModuleFileNameW(self, path, MAX_PATH))
    {
        std::wstring full(path);
        size_t slash = full.find_last_of(L"\\/");
        return slash == std::wstring::npos ? L"." : full.substr(0, slash);
    }
    return L".";
}

// 极简 ini：key=value，# 或 ; 开头是注释
void LoadSettings(BobApplySettings& s)
{
    std::wstring file = ModuleDir() + L"\\csgo_feel.ini";
    FILE* f = nullptr;
    if (_wfopen_s(&f, file.c_str(), L"rb") != 0 || !f)
    {
        Log("没有 csgo_feel.ini，使用内置 csgo 预设");
        return;
    }

    char line[512];
    while (fgets(line, sizeof(line), f))
    {
        char* p = line;
        while (*p == ' ' || *p == '\t') ++p;
        if (*p == '#' || *p == ';' || !*p) continue;

        char* eq = strchr(p, '=');
        if (!eq) continue;
        *eq = 0;

        char* key = p;
        char* value = eq + 1;
        char* end = key + strlen(key);
        while (end > key && (end[-1] == ' ' || end[-1] == '\t')) *--end = 0;
        while (*value == ' ' || *value == '\t') ++value;
        end = value + strlen(value);
        while (end > value && (end[-1] == '\r' || end[-1] == '\n' || end[-1] == ' ')) *--end = 0;

        float fv = static_cast<float>(atof(value));
        if (!strcmp(key, "enabled")) s.enabled = fv != 0.0f;
        else if (!strcmp(key, "bob")) s.bob = fv != 0.0f;
        else if (!strcmp(key, "new_bob")) s.newBob = fv != 0.0f;
        else if (!strcmp(key, "lower")) s.lowerAmt = fv;
        else if (!strcmp(key, "lat")) s.amtLat = fv;
        else if (!strcmp(key, "vert")) s.amtVert = fv;
        else if (!strcmp(key, "cycle")) s.bobCycle = fv;
        else if (!strcmp(key, "shift_left")) s.shiftLeft = fv;
        else if (!strcmp(key, "shift_right")) s.shiftRight = fv;
        else if (!strcmp(key, "xscale")) s.xScale = fv;
        else if (!strcmp(key, "yscale")) s.yScale = fv;
        else if (!strcmp(key, "run_speed")) s.runSpeed = fv;
        else if (!strcmp(key, "walk_speed")) s.walkSpeed = fv;
        else if (!strcmp(key, "crouch_speed")) s.crouchSpeed = fv;
        else if (!strcmp(key, "qieqiang")) s.qieqiang = fv != 0.0f;
        else if (!strcmp(key, "qieqiang_amount")) s.qieqiangAmount = fv;
        else if (!strcmp(key, "qieqiang_time")) s.qieqiangTime = fv;
        else if (!strcmp(key, "wheel")) s.wheel = fv != 0.0f;
        else if (!strcmp(key, "server_sync")) s.serverSync = fv != 0.0f;
        else if (!strcmp(key, "diagnostics")) s.diagnostics = fv != 0.0f;
        else if (!strcmp(key, "toggle_key")) s.toggleKey = static_cast<int>(fv);
    }

    fclose(f);
    Log("已加载 csgo_feel.ini");
}

void WriteOffsets(float x, float y)
{
    float cx = Clamp(x, kOffsetMin, kOffsetMax);
    float cy = Clamp(y, kOffsetMin, kOffsetMax);

    if (g_rt.offsetX.valid() && std::fabs(cx - g_rt.lastX) > 0.0002f)
    {
        *g_rt.offsetX.value = cx;
        g_rt.lastX = cx;
        g_rt.applied = true;
    }
    if (g_rt.offsetY.valid() && std::fabs(cy - g_rt.lastY) > 0.0002f)
    {
        *g_rt.offsetY.value = cy;
        g_rt.lastY = cy;
        g_rt.applied = true;
    }
}

void Restore()
{
    if (!g_rt.applied) return;
    if (g_rt.offsetX.valid()) *g_rt.offsetX.value = g_rt.baseX;
    if (g_rt.offsetY.valid()) *g_rt.offsetY.value = g_rt.baseY;
    if (g_rt.offsetZ.valid()) *g_rt.offsetZ.value = g_rt.baseZ;
    g_rt.applied = false;
    g_rt.lastX = 1e9f;
    g_rt.lastY = 1e9f;
    Log("已恢复原始 viewmodel_offset 数值");
}

bool IsGameFocused()
{
    HWND fg = GetForegroundWindow();
    if (!fg) return false;
    DWORD pid = 0;
    GetWindowThreadProcessId(fg, &pid);
    return pid == GetCurrentProcessId();
}

// 绑定失败时把 ConVarRef / ConVar 头 0x60 字节打出来，方便下一轮修偏移。
void DumpCvarCandidate(uintptr_t refAddress, const char* name)
{
    void* convar = nullptr;
    if (!SafeRead(reinterpret_cast<const void*>(refAddress + 0x8), &convar, sizeof(convar)))
    {
        Log("%s: ref=%p 读不到 +8", name, reinterpret_cast<void*>(refAddress));
        return;
    }

    Log("%s: ref=%p convar=%p", name, reinterpret_cast<void*>(refAddress), convar);
    if (!convar) return;

    for (size_t offset = 0; offset < 0x60; offset += 0x10)
    {
        uint8_t row[16]{};
        if (!SafeRead(reinterpret_cast<uint8_t*>(convar) + offset, row, sizeof(row))) break;

        char hex[16 * 3 + 1]{};
        for (int i = 0; i < 16; ++i) sprintf_s(hex + i * 3, 4, "%02X ", row[i]);

        const char* maybe = *reinterpret_cast<const char**>(row);
        std::string text;
        Log("  +%02zX: %s  (ptr %p%s)", offset, hex, static_cast<const void*>(maybe),
            SafeReadString(maybe, text, 64) ? (" -> \"" + text + "\"").c_str() : "");
    }
}

DWORD WINAPI ThreadProc(LPVOID)
{
    // 1ms 定时器精度，保证 4ms 级更新不会睡成 15ms
    timeBeginPeriod(1);

    const BobApplySettings& s = g_rt.settings;
    BobReset(g_rt.bob);

    LARGE_INTEGER freq{}, prev{};
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&prev);

    bool toggleWasDown = false;
    bool sweepWasDown = false;
    float sweepUntil = -1.0f;
    bool airborne = false;
    float airborneUntil = 0.0f;
    float landDip = 0.0f;
    float elapsed = 0.0f;
    bool active = s.enabled;
    float qieqiangStart = -1.0f;
    float qieqiangDur = 0.35f;

    // 切枪触发：窗口消息（滚轮/侧键/1~5/Q）+ 服务端换枪事件，两个源合并去重
    uint32_t lastInputSeq = InputSwitchSeq();
    int lastSwitchSeq = -1;
    float lastTrigger = -1e9f;

    // 服务端联动的本地镜像
    bool serverLive = false;
    bool qqOn = s.qieqiang;
    float qqAmount = s.qieqiangAmount;
    float qqTime = s.qieqiangTime;

    float lastStatePoll = -1.0f;
    float lastHookCheck = 0.0f;
    float lastHeartbeat = -1.0f;

    while (WaitForSingleObject(g_rt.stopEvent, 4) == WAIT_TIMEOUT)
    {
        LARGE_INTEGER now{};
        QueryPerformanceCounter(&now);
        float dt = static_cast<float>(now.QuadPart - prev.QuadPart) / static_cast<float>(freq.QuadPart);
        prev = now;
        elapsed += dt;

        // ---- 服务端联动：每 200ms 同步一次开关与幅度（放在 F8 之前，按键时判定才是最新的）
        if (elapsed - lastStatePoll > 0.20f)
        {
            lastStatePoll = elapsed;
            const SharedState* st = StateView();
            serverLive = s.serverSync && StateServerLive();

            if (serverLive && st)
            {
                // ini 里的 qieqiang=0 算本地否决：玩家自己不想开，服务端也开不了
                qqOn = s.qieqiang && st->enabled != 0 && st->qieqiang != 0;
                // 服务端 Publish 时一定先写 amount/time 再写 magic，读到合法 magic 就意味着这两个值可信
                qqAmount = Clamp(st->qieqiangAmount, 0.0f, 2.5f);
                if (st->qieqiangTime > 0.10f) qqTime = Clamp(st->qieqiangTime, 0.10f, 2.0f);
            }
            else
            {
                // 服务端不在线（单机 / 别人的服务器）：退回 ini 本地配置
                qqOn = s.qieqiang;
                qqAmount = s.qieqiangAmount;
                qqTime = s.qieqiangTime;
                lastSwitchSeq = -1; // 重连时不要补播一次
            }

            if (!qqOn && qieqiangStart >= 0.0f)
            {
                qieqiangStart = -1.0f;
                Log("切枪动画已关闭（服务端 %d / ini %d）", serverLive ? 1 : 0, s.qieqiang ? 1 : 0);
            }
        }

        bool toggleDown = KeyDown(s.toggleKey);
        if (toggleDown && !toggleWasDown)
        {
            active = !active;
            if (active)
            {
                g_rt.bob = BobState{};
                Log("bob 已开启（F8 可再次关闭）");
            }
            else
            {
                Restore();
                Log("bob 已关闭（F8 可再次开启）");
            }

            // 服务端在线时，把这次切换同步过去：bind 那条链路可能被服务器拦掉，
            // 走共享内存则一定能到达，服务端收到后会在聊天框回显「csgo手感 开/关」
            if (serverLive)
            {
                StateRequestToggle(kReqToggleFeel);
                Log("F8：已把总开关切换请求发给服务端");
            }
        }
        toggleWasDown = toggleDown;

        // F9：让枪模上下扫 3 秒，用来确认「写 ConVar 真的能移动枪模」
        bool sweepDown = KeyDown(0x78);
        if (sweepDown && !sweepWasDown)
        {
            sweepUntil = elapsed + 3.0f;
            Log("F9 扫动测试开始（3 秒）");
        }
        sweepWasDown = sweepDown;

        if (sweepUntil > 0.0f && elapsed < sweepUntil)
        {
            float phase = (sweepUntil - elapsed) * 6.2831853f * 0.8f;
            float y = g_rt.baseY + std::sin(phase) * 0.6f;
            WriteOffsets(g_rt.baseX, y);
            g_rt.applied = true;
            Sleep(4);
            continue;
        }
        if (sweepUntil > 0.0f && elapsed >= sweepUntil)
        {
            sweepUntil = -1.0f;
            Restore();
            Log("F9 扫动测试结束");
        }

        // ---- 看门狗：换图 / 改分辨率后窗口可能被重置，每 2 秒校验一次
        if (elapsed - lastHookCheck > 2.0f)
        {
            lastHookCheck = elapsed;
            InputHookKeepAlive();
        }

        // ---- 心跳：服务端靠它显示「客户端已注入」
        if (elapsed - lastHeartbeat > 1.0f)
        {
            lastHeartbeat = elapsed;
            uint32_t flags = kFlagLink | kFlagKeyInput;
            if (InputHookActive()) flags |= kFlagWindowHook;
            StateHeartbeat(flags);
        }

        if (!active)
        {
            // 关闭期间把触发源吸收掉，重新开启时不会补播一次
            lastInputSeq = InputSwitchSeq();
            const SharedState* st = StateView();
            if (serverLive && st) lastSwitchSeq = st->switchSeq;

            Sleep(20);
            continue;
        }

        bool focus = IsGameFocused();

        // 触发源 1：游戏窗口消息。滚轮不是按键，GetAsyncKeyState 读不到，只能靠这个。
        // 服务端一旦报告过换枪（seq 已同步），就只认真实换枪事件，不再看按键——
        // 否则在聊天框里打 "2" 也会误播一次动画。这里仍然吸收序号，避免服务端掉线后补播。
        bool trustServer = serverLive && lastSwitchSeq >= 0;
        bool localHit = false;
        if (s.wheel && focus)
        {
            uint32_t seq = InputSwitchSeq();
            if (seq != lastInputSeq)
            {
                lastInputSeq = seq;
                localHit = !trustServer;
            }
        }

        // 触发源 2：服务端真的发生了换枪（滚轮 / 数字键 / Q / 拾取 / 空枪自动切都算）
        bool serverHit = false;
        if (serverLive)
        {
            const SharedState* st = StateView();
            if (st)
            {
                if (lastSwitchSeq < 0)
                    lastSwitchSeq = st->switchSeq;
                else if (st->switchSeq != lastSwitchSeq)
                {
                    lastSwitchSeq = st->switchSeq;
                    serverHit = true;
                }
            }
        }

        // 同一把枪换枪时两个源会在几十毫秒内先后到达，150ms 去重窗口把它们合并成一次动画
        if ((localHit || serverHit) && qqOn && (elapsed - lastTrigger) > 0.15f)
        {
            lastTrigger = elapsed;
            qieqiangStart = elapsed;
            qieqiangDur = qqTime > 0.10f ? qqTime : 0.10f;
        }

        float qy = 0.0f;
        if (qieqiangStart >= 0.0f)
        {
            float t = (elapsed - qieqiangStart) / qieqiangDur;
            if (t >= 1.2f)
            {
                qieqiangStart = -1.0f;
                // 收尾回到基准位，避免残留几毫米的偏移
                if (!s.bob) WriteOffsets(g_rt.baseX, g_rt.baseY);
            }
            else
            {
                // 阻尼振荡：从下沉位置回弹，越过原位一次后收敛到 0
                qy = -qqAmount * std::exp(-3.0f * t) * std::cos(6.2831853f * t);
            }
        }

        if (!s.bob && qy == 0.0f)
        {
            Sleep(20);
            continue;
        }
        float forward = focus ? (KeyDown('W') ? 1.0f : 0.0f) - (KeyDown('S') ? 1.0f : 0.0f) : 0.0f;
        float side = focus ? (KeyDown('D') ? 1.0f : 0.0f) - (KeyDown('A') ? 1.0f : 0.0f) : 0.0f;
        bool crouch = focus && (KeyDown(VK_CONTROL) || KeyDown('C'));
        bool walk = focus && KeyDown(VK_SHIFT);

        // 空格当作起跳：离地期间不晃，落地补一个下沉
        bool jump = focus && KeyDown(VK_SPACE);
        if (jump && !airborne)
        {
            airborne = true;
            airborneUntil = elapsed + 0.55f;
        }
        if (airborne && elapsed > airborneUntil)
        {
            airborne = false;
            landDip = 1.0f;
        }

        float mag = std::sqrt(forward * forward + side * side);
        if (mag > 1.0f) mag = 1.0f;

        float base = crouch ? s.crouchSpeed : (walk ? s.walkSpeed : s.runSpeed);
        float speed = airborne ? 0.0f : base * mag;
        float strafe = mag > 0.01f ? side / mag : 0.0f;

        BobSettings bs;
        bs.bob = s.bob ? 1.0f : 0.0f;
        bs.newBob = s.newBob ? 1.0f : 0.0f;
        bs.lowerAmt = s.lowerAmt;
        bs.amtLat = s.amtLat;
        bs.amtVert = s.amtVert;
        bs.bobCycle = s.bobCycle;
        bs.shiftLeft = s.shiftLeft;
        bs.shiftRight = s.shiftRight;

        BobUpdate(g_rt.bob, bs, dt, speed, strafe);

        if (landDip > 0.0f)
        {
            landDip = std::max(0.0f, landDip - dt * 6.0f);
            g_rt.bob.lower += landDip * 10.0f;
        }

        // 设计像素 -> viewmodel_offset 单位；z 不动，避免穿模
        float x, y;
        if (s.bob)
        {
            x = g_rt.baseX + (g_rt.bob.lat + g_rt.bob.shift) * s.xScale;
            y = g_rt.baseY + (g_rt.bob.vert - g_rt.bob.lower) * s.yScale;
        }
        else
        {
            x = g_rt.baseX;
            y = g_rt.baseY;
        }
        y += qy;
        WriteOffsets(x, y);
        g_rt.applied = true;

        int second = static_cast<int>(elapsed);
        if (second != g_rt.lastLogSecond)
        {
            g_rt.lastLogSecond = second;
            Log("bob spd=%.0f lat=%.2f vert=%.2f lower=%.2f qq=%.3f%s -> x=%.3f y=%.3f",
                speed, g_rt.bob.lat, g_rt.bob.vert, g_rt.bob.lower, qy,
                serverLive ? (qqOn ? " (服务端 开)" : " (服务端 关)") : s.qieqiang ? " (本地 开)" : " (本地 关)",
                x, y);
        }
    }

    Restore();
    timeEndPeriod(1);
    return 0;
}
} // namespace

bool BobApplyStart()
{
    ModuleInfo client = GetModuleInfo("client.dll");
    if (!client) return false;

    // 单实例守卫：同一个 cs2 进程里只允许一份客户端 DLL 真正干活。
    //
    // 这不是洁癖，实测会踩：注入过一次之后再注入新版，旧模块还活着，
    // 两个窗口子类会互相把对方的 WNDPROC 当成「被游戏重置」，于是每 2 秒
    // 互相抢一次窗口过程，日志刷满、消息链越套越深。
    // 互斥体名字带 pid（Local\ 是会话级，两个 cs2 进程不该互相干扰），
    // 进程退出自动释放，不会留下脏状态。
    {
        wchar_t name[64]{};
        swprintf_s(name, L"Local\\csgo_feel_client_%lu", GetCurrentProcessId());
        g_rt.instanceMutex = CreateMutexW(nullptr, FALSE, name);
        if (g_rt.instanceMutex && GetLastError() == ERROR_ALREADY_EXISTS)
        {
            Log("已有另一个 CSGOFeelClient 实例在接管本进程：本次只加载、不重复接管。"
                "想换成新版请重启游戏后再注入。");
            CloseHandle(g_rt.instanceMutex);
            g_rt.instanceMutex = nullptr;
            return false;
        }
    }

    // 逐个绑定，允许部分成功：Steam 更新后写死的 RVA 会失效，
    // cvar.cpp 会按代码特征重新定位；定位不到的只影响「动枪模」这一层，
    // 共享内存通道 / F8 / 换枪检测都还要继续跑（否则客户端等于没装）。
    //
    // x / y / z 的 ConVarRef 在 .data 里是连续的（步长 0x10），所以一旦 x 是靠扫描找出来的，
    // y、z 直接用 x 的地址 +0x10 试，不用再各扫一遍 40MB 的 .text。
    // 地址错了也没关系：BindAt 会用 ConVar 里的名字校验，对不上照样退回扫描。
    bool okX = BindFloatCvar(client, "viewmodel_offset_x", kOffsetXRefRva, g_rt.offsetX);
    uintptr_t yHint = (okX && g_rt.offsetX.rva) ? g_rt.offsetX.rva + 0x10 : kOffsetYRefRva;
    bool okY = BindFloatCvar(client, "viewmodel_offset_y", yHint, g_rt.offsetY);
    uintptr_t zHint = (okY && g_rt.offsetY.rva) ? g_rt.offsetY.rva + 0x10 : kOffsetZRefRva;
    bool okZ = BindFloatCvar(client, "viewmodel_offset_z", zHint, g_rt.offsetZ);

    if (okX && okY && okZ)
    {
        Log("viewmodel_offset_x ref=%p rva=0x%zX base=%.3f",
            reinterpret_cast<void*>(g_rt.offsetX.ref), g_rt.offsetX.rva,
            g_rt.offsetX.value ? *g_rt.offsetX.value : 0.0f);
        Log("viewmodel_offset_y ref=%p rva=0x%zX base=%.3f",
            reinterpret_cast<void*>(g_rt.offsetY.ref), g_rt.offsetY.rva,
            g_rt.offsetY.value ? *g_rt.offsetY.value : 0.0f);
        Log("viewmodel_offset_z ref=%p rva=0x%zX base=%.3f",
            reinterpret_cast<void*>(g_rt.offsetZ.ref), g_rt.offsetZ.rva,
            g_rt.offsetZ.value ? *g_rt.offsetZ.value : 0.0f);

        g_rt.baseX = *g_rt.offsetX.value;
        g_rt.baseY = *g_rt.offsetY.value;
        g_rt.baseZ = *g_rt.offsetZ.value;
    }
    else
    {
        Log("定位 viewmodel_offset_x/y/z 失败：x=%d y=%d z=%d（client.dll 可能被更新过）",
            okX, okY, okZ);
        Log("降级运行：共享内存通道 / F8 / 换枪检测照常启动，但枪模不会移动");
        if (!okX) DumpCvarCandidate(client.base + kOffsetXRefRva, "viewmodel_offset_x");
    }

    LoadSettings(g_rt.settings);

    // 共享内存先挂上：服务端插件在线时，切枪动画的开关由 csgo_qieqiang / !qieqiang 实时决定
    if (!g_rt.settings.serverSync)
        Log("server_sync=0：忽略服务端状态，切枪动画只看 ini");
    else if (!StateOpen())
        Log("共享内存不可用：切枪动画只看 ini");

    // 接管游戏窗口消息，拿到滚轮 / 侧键（GetAsyncKeyState 读不到这两类输入）
    if (g_rt.settings.wheel)
    {
        if (!InputHookStart())
            Log("窗口消息暂时挂不上（窗口还没建好），后台线程会继续重试");
    }

    g_rt.stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!g_rt.stopEvent)
    {
        Log("CreateEvent 失败");
        return false;
    }

    g_rt.thread = CreateThread(nullptr, 0, ThreadProc, nullptr, 0, nullptr);
    if (!g_rt.thread)
    {
        Log("CreateThread 失败");
        CloseHandle(g_rt.stopEvent);
        g_rt.stopEvent = nullptr;
        if (g_rt.instanceMutex)
        {
            CloseHandle(g_rt.instanceMutex);
            g_rt.instanceMutex = nullptr;
        }
        return false;
    }

    InterlockedExchange(&g_rt.running, 1);
    Log("bob 线程已启动：toggle=F%d  xscale=%.3f yscale=%.3f  wheel=%d  server_sync=%d",
        g_rt.settings.toggleKey & 0xFF, g_rt.settings.xScale, g_rt.settings.yScale,
        g_rt.settings.wheel, g_rt.settings.serverSync);
    return true;
}

void BobApplyStop()
{
    if (!InterlockedExchange(&g_rt.running, 0)) return;

    if (g_rt.stopEvent) SetEvent(g_rt.stopEvent);
    if (g_rt.thread) WaitForSingleObject(g_rt.thread, 500);
    Restore();

    InputHookStop();
    StateClose();

    if (g_rt.thread) CloseHandle(g_rt.thread);
    if (g_rt.stopEvent) CloseHandle(g_rt.stopEvent);
    if (g_rt.instanceMutex) CloseHandle(g_rt.instanceMutex);
    g_rt.thread = nullptr;
    g_rt.stopEvent = nullptr;
    g_rt.instanceMutex = nullptr;
    Log("bob 线程已停止");
}

void BobApplyRestore()
{
    Restore();
}

bool BobApplyDiagnostics()
{
    return g_rt.settings.diagnostics;
}
} // namespace feel
