#pragma once

#include <windows.h>
#include <cstdint>
#include <string>
#include <vector>

namespace feel
{
struct ModuleInfo
{
    uintptr_t base = 0;
    size_t size = 0;

    explicit operator bool() const { return base != 0; }
};

ModuleInfo GetModuleInfo(const char* name);
void* ResolveExport(const char* moduleName, const char* exportName);

// 读取自身进程内的内存，先做 VirtualQuery 校验，避免野指针崩溃。
bool SafeRead(const void* address, void* out, size_t size);
bool SafeReadString(const void* address, std::string& out, size_t maxLen = 128);

// IDA 风格特征码，例如 "48 8B 05 ?? ?? ?? ?? 48 85 C0"
uintptr_t FindPattern(const ModuleInfo& module, const char* pattern);

// 在模块的可读节里搜索 ASCII 字符串，返回首个匹配地址（0 = 未找到）。
uintptr_t FindStringInModule(const ModuleInfo& module, const char* text);

// 在该模块的可写节里扫描 8 字节指针，收集所有指向 target 的地址。
std::vector<uintptr_t> FindPointersTo(const ModuleInfo& module, uintptr_t target);
}
