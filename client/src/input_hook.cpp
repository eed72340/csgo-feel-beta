#define NOMINMAX
#include "input_hook.h"

#include "log.h"

#include <windows.h>
#include <atomic>

namespace feel
{
namespace
{
HWND g_hwnd = nullptr;

// 游戏原本的窗口过程。
//
// 这个指针一旦装好就**再也不置空**：HookProc 跑在游戏自己的 UI 线程上，
// 而 InputHookKeepAlive / InputHookStop 跑在 bob 线程上。如果在这里写 nullptr，
// 游戏线程可能正卡在 HookProc 里读到空指针，于是那条消息被 DefWindowProcW 处理掉——
// 等于把游戏的一条窗口消息吞了（拖窗口、激活、键盘消息都可能中招）。
// 卸载时只要把 WNDPROC 还原即可，这个值留着没有任何副作用。
WNDPROC g_origProc = nullptr;

std::atomic<uint32_t> g_switchSeq{0};
std::atomic<bool> g_active{false};
std::atomic<bool> g_loggedWheel{false};

bool IsSwitchKey(int vk)
{
    return (vk >= '1' && vk <= '5') || vk == 'Q';
}

void MarkSwitch()
{
    g_switchSeq.fetch_add(1, std::memory_order_release);
}

LRESULT CALLBACK HookProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp)
{
    switch (msg)
    {
    case WM_MOUSEWHEEL:
    {
        short delta = GET_WHEEL_DELTA_WPARAM(wp);
        if (delta != 0)
        {
            MarkSwitch();
            if (!g_loggedWheel.exchange(true))
                Log("已捕获鼠标滚轮输入（切枪动画现在支持滚轮）");
        }
        break;
    }
    case WM_XBUTTONDOWN:
        // 侧键默认就是切枪键，按下沿算一次
        MarkSwitch();
        break;
    case WM_KEYDOWN:
    case WM_SYSKEYDOWN:
        if (IsSwitchKey(static_cast<int>(wp))) MarkSwitch();
        break;
    default:
        break;
    }

    // 一律原样放行：游戏自己的切枪、滚轮绑定都还要正常工作
    if (!g_origProc || g_origProc == HookProc)
        return DefWindowProcW(hwnd, msg, wp, lp);
    return CallWindowProcW(g_origProc, hwnd, msg, wp, lp);
}

struct FindCtx
{
    DWORD pid;
    HWND best;
    LONG bestArea;
};

BOOL CALLBACK EnumProc(HWND hwnd, LPARAM lp)
{
    auto* ctx = reinterpret_cast<FindCtx*>(lp);

    DWORD pid = 0;
    GetWindowThreadProcessId(hwnd, &pid);
    if (pid != ctx->pid) return TRUE;
    if (!IsWindowVisible(hwnd)) return TRUE;

    // 游戏窗口就是本进程里客户区最大的那个可见窗口
    RECT rc{};
    if (!GetClientRect(hwnd, &rc)) return TRUE;
    LONG area = (rc.right - rc.left) * (rc.bottom - rc.top);
    if (area > ctx->bestArea)
    {
        ctx->bestArea = area;
        ctx->best = hwnd;
    }
    return TRUE;
}

HWND FindGameWindow()
{
    FindCtx ctx{GetCurrentProcessId(), nullptr, 0};
    EnumWindows(EnumProc, reinterpret_cast<LPARAM>(&ctx));
    return ctx.best;
}

bool Install()
{
    HWND hwnd = FindGameWindow();
    if (!hwnd)
    {
        // 注入得比窗口创建早是正常的，交给看门狗重试，不刷日志
        return false;
    }

    WNDPROC current = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(hwnd, GWLP_WNDPROC));
    if (current == HookProc)
    {
        g_hwnd = hwnd;
        g_active.store(true);
        return true;
    }

    // 顺序不能反：先记下原过程再替换 WNDPROC。
    // 一旦 SetWindowLongPtrW 生效，HookProc 就可能被调用，那时它链式调用的
    // 必须已经是这个新值。（反过来写会有一个读到旧值的窗口期。）
    g_origProc = current;
    if (!SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(HookProc)))
    {
        Log("SetWindowLongPtr 失败 err=%lu，滚轮切枪识别不可用", GetLastError());
        return false;
    }

    g_hwnd = hwnd;
    g_active.store(true);
    Log("已接管游戏窗口 0x%p 的消息过程（滚轮 / 侧键 / 1~5 / Q 切枪识别）",
        static_cast<void*>(hwnd));
    return true;
}
} // namespace

bool InputHookStart()
{
    return Install();
}

void InputHookStop()
{
    g_active.store(false);
    if (!g_hwnd) return;

    if (IsWindow(g_hwnd))
    {
        WNDPROC current = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(g_hwnd, GWLP_WNDPROC));
        if (current == HookProc && g_origProc && g_origProc != HookProc)
            SetWindowLongPtrW(g_hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_origProc));
    }

    // 只清 hwnd，不动 g_origProc：可能还有一条消息正在 HookProc 里链式调用它。
    g_hwnd = nullptr;
    Log("已还原游戏窗口消息过程");
}

void InputHookKeepAlive()
{
    if (g_hwnd && IsWindow(g_hwnd))
    {
        WNDPROC current = reinterpret_cast<WNDPROC>(GetWindowLongPtrW(g_hwnd, GWLP_WNDPROC));
        if (current == HookProc)
        {
            g_active.store(true);
            return;
        }
        // 被游戏重置了（换图 / 切分辨率 / SDL 重建窗口）：重新挂一次。
        // 这里也不清 g_origProc——Install 会把它换成当前真实的过程。
        Log("窗口消息过程被重置，重新接管");
        g_active.store(false);
        g_hwnd = nullptr;
    }
    else if (g_hwnd)
    {
        // 窗口没了：等新窗口出现再挂
        g_hwnd = nullptr;
        g_active.store(false);
    }

    Install();
}

uint32_t InputSwitchSeq()
{
    return g_switchSeq.load(std::memory_order_acquire);
}

bool InputHookActive()
{
    return g_active.load(std::memory_order_relaxed);
}
} // namespace feel
