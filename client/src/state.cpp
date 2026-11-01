#include "state.h"

#include "log.h"

#include <windows.h>
#include <atomic>

namespace feel
{
namespace
{
// 带 Local\ 前缀：只在当前登录会话内可见，够用且不会和其他用户串数据
constexpr wchar_t kMapName[] = L"Local\\csgo_feel_v2";

HANDLE g_map = nullptr;
SharedState* g_view = nullptr;

uint32_t g_flags = 0;
} // namespace

bool StateOpen()
{
    if (g_view) return true;

    g_map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0,
                               sizeof(SharedState), kMapName);
    if (!g_map)
    {
        Log("共享内存创建失败 err=%lu（服务端联动功能不可用，退回 ini 本地控制）", GetLastError());
        return false;
    }

    g_view = static_cast<SharedState*>(
        MapViewOfFile(g_map, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedState)));
    if (!g_view)
    {
        Log("MapViewOfFile 失败 err=%lu", GetLastError());
        CloseHandle(g_map);
        g_map = nullptr;
        return false;
    }

    // 本页可能是别的进程刚创建的（内容全 0），也可能沿用上一局服务端留下的旧值；
    // 这里不碰服务端字段，只用 magic / 心跳判断服务端是否真的在线。
    Log("共享内存已挂载 view=%p size=%d magic=0x%08X serverTick=%llu",
        static_cast<void*>(g_view), static_cast<int>(sizeof(SharedState)),
        g_view->magic, static_cast<unsigned long long>(g_view->serverTick));
    return true;
}

void StateClose()
{
    if (g_view)
    {
        UnmapViewOfFile(g_view);
        g_view = nullptr;
    }
    if (g_map)
    {
        CloseHandle(g_map);
        g_map = nullptr;
    }
}

const SharedState* StateView()
{
    return g_view;
}

bool StateServerLive(int maxAgeMs)
{
    if (!g_view) return false;
    if (g_view->magic != kStateMagic || g_view->version != kStateVersion) return false;

    uint64_t tick = g_view->serverTick;
    if (!tick) return false;

    uint64_t now = GetTickCount64();
    if (now < tick) return false; // 心跳来自「未来」说明是上一轮残留，按离线处理
    return (now - tick) <= static_cast<uint64_t>(maxAgeMs);
}

void StateHeartbeat(uint32_t flags)
{
    if (!g_view) return;

    uint64_t now = GetTickCount64();
    // 这些字段只有客户端写，和服务端字段不重叠，不用加锁
    g_view->clientPid = GetCurrentProcessId();
    g_view->clientTick = now;

    if (flags != g_flags)
    {
        g_flags = flags;
        g_view->clientFlags = flags;
    }
}

void StateRequestToggle(uint32_t cmd)
{
    if (!g_view) return;

    // 先写命令再递增序号：服务端看到序号变化时，命令一定已经落地。
    // 这两条是普通写，编译器可以把它们重排（x86 硬件本身不会重排 store），
    // 所以中间加一条 release fence 锁住顺序。
    g_view->reqCmd = cmd;
    std::atomic_thread_fence(std::memory_order_release);
    g_view->reqSeq = g_view->reqSeq + 1;
}
} // namespace feel
