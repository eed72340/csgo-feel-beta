#pragma once

#include "mem.h"

namespace feel
{
// 通过 client.dll 里的静态 ConVarRef 反查 ConVar，再直接读写它的浮点值。
// 当前 build 的 ConVarRef 在 .data 的固定 RVA（由 "viewmodel_offset_x" 的注册函数反汇编得到），
// 运行时还会校验名字，对不上就退回「字符串 -> 指针反查」。
struct FloatCvar
{
    const char* name = nullptr;
    uintptr_t ref = 0;       // ConVarRef*（client.dll 内）
    void* convar = nullptr;  // ConVar*
    float* value = nullptr;  // &ConVar::m_fValue (+0x58)
    uintptr_t rva = 0;       // ConVarRef 相对 client.dll 的偏移，首次定位失败后自动扫描出来的值

    bool valid() const { return value != nullptr; }
};

// 解析一个浮点 ConVar（只读校验，不写）。
bool BindFloatCvar(const ModuleInfo& client, const char* name, uintptr_t hintRva, FloatCvar& out);

} // namespace feel
