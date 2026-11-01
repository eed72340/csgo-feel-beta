#include "cvar.h"

#include <cstring>
#include <vector>

namespace feel
{
namespace
{
constexpr size_t kConVarRefConVarPtr = 0x08; // ConVarRef { ...; ConVar* m_pConVar; }
constexpr size_t kConVarValue = 0x58;        // ConVar::m_fValue

bool NameMatches(void* convar, const char* expected, size_t* nameOffset = nullptr)
{
    if (!convar) return false;

    for (size_t offset = 0; offset < 0x60; offset += 0x8)
    {
        const char* name = nullptr;
        if (!SafeRead(reinterpret_cast<const uint8_t*>(convar) + offset, &name, sizeof(name)) || !name)
            continue;

        std::string text;
        if (!SafeReadString(name, text, 96)) continue;
        if (text != expected) continue;

        if (nameOffset) *nameOffset = offset;
        return true;
    }
    return false;
}

bool BindAt(uintptr_t refAddress, const char* expected, FloatCvar& out)
{
    void* convar = nullptr;
    if (!SafeRead(reinterpret_cast<const void*>(refAddress + kConVarRefConVarPtr), &convar, sizeof(convar)))
        return false;
    if (!NameMatches(convar, expected)) return false;

    uintptr_t valueAddress = reinterpret_cast<uintptr_t>(convar) + kConVarValue;
    out.ref = refAddress;
    out.convar = convar;
    out.value = reinterpret_cast<float*>(valueAddress);
    return true;
}

// 兜底 1：用代码特征重新定位 ConVarRef。
//
// 注册函数的形状是稳定的（多个版本都一样）：
//     lea rdx, [rip + A]   ; A -> "viewmodel_offset_x" 名字字符串
//     ...
//     lea rcx, [rip + B]   ; B -> ConVarRef（紧接着那句）
//     mov r9d, 0x8288
//     call <ConVar 注册>
// 所以先找「谁的 lea rdx 指向这个名字字符串」，再取它后面 0x40 字节内的 lea rcx。
// 硬编码 RVA 一旦因为 Steam 更新失效，就靠这条路径自愈。
uintptr_t FindConVarRefByCode(const ModuleInfo& module, const char* name)
{
    uintptr_t stringVa = FindStringInModule(module, name);
    if (!stringVa) return 0;

    auto* base = reinterpret_cast<uint8_t*>(module.base);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);

    auto* section = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section)
    {
        if (!(section->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;

        auto* start = base + section->VirtualAddress;
        size_t size = section->Misc.VirtualSize;

        for (size_t offset = 0; offset + 7 <= size; ++offset)
        {
            if (start[offset] != 0x48 || start[offset + 1] != 0x8D || start[offset + 2] != 0x15) continue;

            int32_t disp = 0;
            std::memcpy(&disp, start + offset + 3, sizeof(disp));

            uintptr_t insn = reinterpret_cast<uintptr_t>(start + offset);
            if (insn + 7 + static_cast<intptr_t>(disp) != stringVa) continue;

            size_t limit = offset + 7 + 0x40;
            if (limit > size) limit = size;

            for (size_t j = offset + 7; j + 7 <= limit; ++j)
            {
                if (start[j] != 0x48 || start[j + 1] != 0x8D || start[j + 2] != 0x0D) continue;

                int32_t disp2 = 0;
                std::memcpy(&disp2, start + j + 3, sizeof(disp2));
                return reinterpret_cast<uintptr_t>(start + j) + 7 + static_cast<intptr_t>(disp2);
            }
        }
    }

    return 0;
}

// 兜底 2：在模块里找名字字符串，再找指向它的指针；ConVarRef 不存名字，
// 但有些版本会把名字直接内联在对象里，能命中就顺便用。
bool BindByString(const ModuleInfo& client, const char* name, FloatCvar& out)
{
    uintptr_t text = FindStringInModule(client, name);
    if (!text) return false;

    for (uintptr_t hit : FindPointersTo(client, text))
    {
        if (BindAt(hit, name, out)) return true;
        // 也有可能指针落在 ConVar 对象上，再往回找 0x10 字节的 ConVarRef
        for (size_t back = 0x8; back <= 0x40; back += 0x8)
        {
            if (hit < back) break;
            if (BindAt(hit - back, name, out)) return true;
        }
    }
    return false;
}
} // namespace

bool BindFloatCvar(const ModuleInfo& client, const char* name, uintptr_t hintRva, FloatCvar& out)
{
    out = FloatCvar{};
    out.name = name;

    if (hintRva && BindAt(client.base + hintRva, name, out))
    {
        out.rva = hintRva;
        return true;
    }

    // 写死的 RVA 在 Steam 更新后必然失效，这里按代码特征重新找一遍
    uintptr_t refVa = FindConVarRefByCode(client, name);
    if (refVa && BindAt(refVa, name, out))
    {
        out.rva = refVa - client.base;
        return true;
    }

    return BindByString(client, name, out);
}
} // namespace feel
