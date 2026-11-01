#include "sdk.h"

#include "log.h"
#include "mem.h"

#include <cmath>
#include <cstring>
#include <string>
#include <vector>

namespace feel
{
using namespace std::string_literals;

namespace
{
void* g_cvarSystem = nullptr;

using CreateInterfaceFn = void* (*)(const char* name, int* returnCode);

void* CallCreateInterface(void* fn, const char* name)
{
    if (!fn) return nullptr;

    void* result = nullptr;
    int code = 0;

    __try
    {
        result = reinterpret_cast<CreateInterfaceFn>(fn)(name, &code);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        Log("CreateInterface(%s) 调用异常，接口布局可能已变化", name);
        return nullptr;
    }

    Log("CreateInterface(%s) -> %p (code=%d)", name, result, code);
    return result;
}

struct NamedModule
{
    const char* name;
    ModuleInfo info;
};

NamedModule g_modules[4] = {
    { "client.dll", {} },
    { "tier0.dll", {} },
    { "engine2.dll", {} },
    { "server.dll", {} },
};

void LoadModules()
{
    for (auto& m : g_modules)
        m.info = GetModuleInfo(m.name);
}

bool AddressInAnyModule(uintptr_t address, const char** moduleName = nullptr)
{
    for (auto& m : g_modules)
    {
        if (!m.info) continue;
        if (address >= m.info.base && address < m.info.base + m.info.size)
        {
            if (moduleName) *moduleName = m.name;
            return true;
        }
    }
    return false;
}

bool ReadPrintableString(uintptr_t address, std::string& out, size_t maxLen = 64)
{
    if (!address) return false;
    if (!SafeReadString(reinterpret_cast<const void*>(address), out, maxLen)) return false;
    if (out.empty()) return false;

    for (char c : out)
    {
        if (static_cast<unsigned char>(c) < 0x20 || static_cast<unsigned char>(c) > 0x7E)
            return false;
    }
    return true;
}

void DumpHex(const uint8_t* object, size_t size)
{
    for (size_t offset = 0; offset < size; offset += 16)
    {
        uint8_t row[16]{};
        if (!SafeRead(object + offset, row, sizeof(row))) break;

        char hex[16 * 3 + 1]{};
        for (int i = 0; i < 16; ++i)
            sprintf_s(hex + i * 3, 4, "%02X ", row[i]);
        Log("  %02zX: %s", offset, hex);
    }
}

// 把对象里所有"指向模块内可打印字符串"的字段列出来，这是定位 name / help 字段最快的方式。
void DumpStringPointers(uintptr_t object, size_t size)
{
    for (size_t offset = 0; offset + 8 <= size; offset += 8)
    {
        uintptr_t pointer = 0;
        if (!SafeRead(reinterpret_cast<const void*>(object + offset), &pointer, 8) || !pointer) continue;

        const char* module = nullptr;
        if (!AddressInAnyModule(pointer, &module)) continue;

        std::string text;
        if (!ReadPrintableString(pointer, text)) continue;

        Log("  +0x%02zX -> \"%s\"  (%s)", offset, text.c_str(), module);
    }
}

void DumpFloats(uintptr_t object, size_t size)
{
    for (size_t offset = 0; offset + 4 <= size; offset += 4)
    {
        float value = 0.0f;
        if (!SafeRead(reinterpret_cast<const void*>(object + offset), &value, 4)) continue;
        if (!std::isfinite(value) || std::fabs(value) > 100000.0f || value == 0.0f) continue;

        Log("  float @0x%02zX = %.4f", offset, value);
    }
}

// 反查：先找到名字字符串，再在可写数据里找指向它的指针。
// ConVar 对象通常有一堆字段，name 指针就在其中，不需要 ICvar::FindVar 参与。
void LocateByName(const char* name)
{
    Log("---- 定位 ConVar \"%s\" ----", name);

    bool found = false;

    for (auto& m : g_modules)
    {
        if (!m.info) continue;

        uintptr_t text = FindStringInModule(m.info, name);
        if (!text)
        {
            Log("  %-12s 字符串未找到", m.name);
            continue;
        }

        Log("  %-12s 字符串 @ %p", m.name, reinterpret_cast<void*>(text));

        int total = 0;
        for (auto& target : g_modules)
        {
            if (!target.info) continue;

            auto hits = FindPointersTo(target.info, text);
            for (uintptr_t hit : hits)
            {
                ++total;
                Log("  引用 @ %p (%s .data)", reinterpret_cast<void*>(hit), target.name);
                Log("  对象起始 = %p", reinterpret_cast<void*>(hit));

                DumpHex(reinterpret_cast<const uint8_t*>(hit), 0x60);
                DumpStringPointers(hit, 0x80);
                DumpFloats(hit, 0x60);
                found = true;
            }
        }

        if (total == 0)
            Log("  没有任何可写数据指向它（可能名字是运行时拼接的）");
    }

    if (!found)
        Log("  结论：\"%s\" 无法用指针反查定位", name);
}

void DumpICvarVtable()
{
    if (!g_cvarSystem) return;

    auto** vtable = *reinterpret_cast<void***>(g_cvarSystem);
    Log("ICvar vtable @ %p", reinterpret_cast<void*>(vtable));

    for (int index = 0; index < 24; ++index)
    {
        void* fn = nullptr;
        if (!SafeRead(vtable + index, &fn, sizeof(fn)) || !fn) continue;

        const char* module = nullptr;
        uintptr_t address = reinterpret_cast<uintptr_t>(fn);
        if (AddressInAnyModule(address, &module))
        {
            uintptr_t base = 0;
            for (auto& m : g_modules)
                if (std::strcmp(m.name, module) == 0) base = m.info.base;

            Log("  [%2d] %p  %s+0x%zX", index, fn, module, address - base);
        }
        else
        {
            Log("  [%2d] %p", index, fn);
        }
    }
}
}

bool InitCvar()
{
    void* createInterface = ResolveExport("tier0.dll", "CreateInterface");
    if (!createInterface)
        createInterface = ResolveExport("engine2.dll", "CreateInterface");
    if (!createInterface)
    {
        Log("找不到 CreateInterface 导出（tier0.dll / engine2.dll），当前进程可能不是 CS2");
        return false;
    }

    LoadModules();
    Log("CreateInterface @ %p", createInterface);
    g_cvarSystem = CallCreateInterface(createInterface, "VEngineCvar007");
    if (!g_cvarSystem)
    {
        Log("VEngineCvar007 解析失败");
        return false;
    }

    Log("VEngineCvar007 @ %p", g_cvarSystem);
    return true;
}

void RunDiagnostics()
{
    LoadModules();
    if (!InitCvar()) return;

    DumpICvarVtable();

    // 只读反查：字符串 -> 指针 -> ConVar 对象
    for (const char* name : { "view_punch_decay", "viewmodel_fov", "cl_righthand",
                              "cl_crosshair_recoil", "cl_bob", "viewmodel_recoil" })
    {
        LocateByName(name);
    }

    Log("---- 结论提示 ----");
    Log("view_punch_decay 出现过对象 = 内存里可以改；cl_bob / viewmodel_recoil 若无对象 = 已被 CS2 删除。");
}
}
