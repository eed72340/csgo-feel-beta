#include "log.h"

#include <cstdarg>
#include <cstring>
#include <cstdio>
#include <share.h>
#include <mutex>

namespace feel
{
namespace
{
std::mutex g_mutex;
FILE* g_file = nullptr;
}

std::wstring ModuleDirectory(HMODULE module)
{
    wchar_t path[MAX_PATH]{};
    if (!GetModuleFileNameW(module, path, MAX_PATH))
        return L".";

    std::wstring full(path);
    size_t slash = full.find_last_of(L"\\/");
    return slash == std::wstring::npos ? L"." : full.substr(0, slash);
}

void LogInit(HMODULE self)
{
    std::wstring dir = ModuleDirectory(self);
    // 文件名带 pid：多开 cs2 / 已注入旧版时不会互相抢独占句柄
    wchar_t name[64]{};
    swprintf_s(name, L"\\csgo_feel_client_%lu.log", GetCurrentProcessId());
    std::wstring file = dir + name;

    std::lock_guard lock(g_mutex);
    if (g_file) return;
    // 显式 _SH_DENYNO：注入进程里写日志时，外部（PowerShell/记事本）仍能读
    g_file = _wfsopen(file.c_str(), L"ab", _SH_DENYNO);
}

void Log(const char* fmt, ...)
{
    char buffer[2048];
    va_list args;
    va_start(args, fmt);
    vsnprintf_s(buffer, sizeof(buffer), _TRUNCATE, fmt, args);
    va_end(args);

    OutputDebugStringA("[csgo-feel] ");
    OutputDebugStringA(buffer);
    OutputDebugStringA("\n");

    std::lock_guard lock(g_mutex);
    if (!g_file) return;

    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(g_file, "[%02d:%02d:%02d] ", st.wHour, st.wMinute, st.wSecond);
    fwrite(buffer, 1, strlen(buffer), g_file);
    fputc('\n', g_file);
    fflush(g_file);
}
}

