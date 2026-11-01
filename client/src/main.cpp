#include "bob_apply.h"
#include "log.h"
#include "mem.h"
#include "sdk.h"

#include <windows.h>

using feel::Log;
using feel::ModuleInfo;

namespace
{
HMODULE g_self = nullptr;

DWORD WINAPI InitThread(LPVOID)
{
    Log("==================================================");
    Log("CSGO Feel Client (bob build) attached");
    Log("pid=%lu  module=%p", GetCurrentProcessId(), g_self);

    ModuleInfo client = feel::GetModuleInfo("client.dll");
    if (!client)
    {
        Log("client.dll 未加载：当前进程不是 CS2，诊断到此结束（注入链路本身正常）。");
        return 0;
    }

    Log("client.dll base=%p size=0x%zX", reinterpret_cast<void*>(client.base), client.size);

    // 先接管 bob（用户进图就能看到效果），诊断放后面慢慢跑
    if (!feel::BobApplyStart())
        Log("bob 未接管：client.dll 未就绪 / 线程创建失败 / 本进程已有另一个实例（见上一行）");
    else
        Log("bob 已接管：F8 急停 / 再按恢复；调整幅度改 csgo_feel.ini");

    // 诊断要在 client.dll 的可写节里反复反查指针，还要刷几十 KB 日志，
    // 注入时那几秒卡顿就是它。只有 ini 里 diagnostics=1 才跑。
    if (feel::BobApplyDiagnostics())
        feel::RunDiagnostics();
    else
        Log("已跳过内存诊断（需要时在 csgo_feel.ini 里加 diagnostics=1）");

    Log("==================================================");
    return 0;
}
} // namespace

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;
        DisableThreadLibraryCalls(module);
        feel::LogInit(module);
        feel::Log("DllMain(DLL_PROCESS_ATTACH)");

        HANDLE thread = CreateThread(nullptr, 0, InitThread, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        // 两种 detach 必须区别对待：
        //   reserved != nullptr → 进程正在退出。此时仍在加载器锁内，等线程 / 抢日志锁都可能
        //     直接死锁，而且进程马上消失，没有任何需要还原的东西。
        //   reserved == nullptr → 显式 FreeLibrary，才值得做完整收尾。
        if (reserved == nullptr) feel::BobApplyStop();
    }

    return TRUE;
}
