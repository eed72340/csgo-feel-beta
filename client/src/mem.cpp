#include "mem.h"

#include <cctype>
#include <cstring>
#include <vector>

namespace feel
{
namespace
{
struct PatternByte
{
    uint8_t value;
    bool wildcard;
};

std::vector<PatternByte> ParsePattern(const char* pattern)
{
    std::vector<PatternByte> bytes;
    const char* p = pattern;

    while (*p)
    {
        while (*p == ' ' || *p == '\t') ++p;
        if (!*p) break;

        if (p[0] == '?' && p[1] == '?')
        {
            bytes.push_back({ 0, true });
            p += 2;
        }
        else if (std::isxdigit(static_cast<unsigned char>(p[0])) &&
                 p[1] && std::isxdigit(static_cast<unsigned char>(p[1])))
        {
            char hex[3]{ p[0], p[1], 0 };
            bytes.push_back({ static_cast<uint8_t>(std::strtoul(hex, nullptr, 16)), false });
            p += 2;
        }
        else
        {
            ++p;
        }
    }

    return bytes;
}
}

ModuleInfo GetModuleInfo(const char* name)
{
    HMODULE module = GetModuleHandleA(name);
    if (!module) return {};

    auto* base = reinterpret_cast<uint8_t*>(module);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return {};

    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return {};

    return { reinterpret_cast<uintptr_t>(base), nt->OptionalHeader.SizeOfImage };
}

void* ResolveExport(const char* moduleName, const char* exportName)
{
    HMODULE module = GetModuleHandleA(moduleName);
    if (!module) return nullptr;
    return reinterpret_cast<void*>(GetProcAddress(module, exportName));
}

bool SafeRead(const void* address, void* out, size_t size)
{
    if (!address || !out || !size) return false;

    auto* cursor = reinterpret_cast<const uint8_t*>(address);
    size_t remaining = size;

    while (remaining)
    {
        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(cursor, &mbi, sizeof(mbi))) return false;
        if (mbi.State != MEM_COMMIT) return false;
        if (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) return false;

        size_t chunk = min(static_cast<size_t>(mbi.RegionSize) -
                              (cursor - static_cast<const uint8_t*>(mbi.BaseAddress)),
                          remaining);

        std::memcpy(static_cast<uint8_t*>(out) + (size - remaining), cursor, chunk);
        cursor += chunk;
        remaining -= chunk;
    }

    return true;
}

bool SafeReadString(const void* address, std::string& out, size_t maxLen)
{
    out.clear();
    if (!address || !maxLen) return false;

    // 一次最多拷一小段：不能整段 memcpy，因为中间可能跨到未提交的页。
    // 但也不能像以前那样每个字符调一次 SafeRead（每个字符一次 VirtualQuery），
    // ConVar 名字校验会调用几十次，扫描时更夸张。
    char buffer[256];
    size_t want = maxLen < sizeof(buffer) ? maxLen : sizeof(buffer);
    size_t have = 0;

    while (have < want)
    {
        const uint8_t* cursor = static_cast<const uint8_t*>(address) + have;

        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(cursor, &mbi, sizeof(mbi))) break;
        if (mbi.State != MEM_COMMIT) break;
        if (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)) break;

        size_t regionLeft = static_cast<size_t>(mbi.RegionSize) -
                            (cursor - static_cast<const uint8_t*>(mbi.BaseAddress));
        size_t chunk = want - have < regionLeft ? want - have : regionLeft;
        if (!chunk) break;

        std::memcpy(buffer + have, cursor, chunk);
        have += chunk;
    }

    if (!have) return false;

    const void* nul = std::memchr(buffer, '\0', have);
    out.assign(buffer, nul ? static_cast<const char*>(nul) - buffer : have);
    return true;
}

uintptr_t FindStringInModule(const ModuleInfo& module, const char* text)
{
    if (!module || !text || !*text) return 0;

    auto* base = reinterpret_cast<uint8_t*>(module.base);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);

    size_t needleLen = std::strlen(text);
    auto* section = IMAGE_FIRST_SECTION(nt);

    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section)
    {
        if (!(section->Characteristics & IMAGE_SCN_MEM_READ)) continue;

        auto* start = base + section->VirtualAddress;
        size_t size = section->Misc.VirtualSize;
        if (size <= needleLen) continue;

        for (size_t offset = 0; offset + needleLen <= size; ++offset)
        {
            if (std::memcmp(start + offset, text, needleLen) != 0) continue;

            // 必须是完整的 C 字符串，避免命中更长的名字
            char after = 0;
            if (!SafeRead(start + offset + needleLen, &after, 1)) continue;
            if (after == '\0') return reinterpret_cast<uintptr_t>(start + offset);
        }
    }

    return 0;
}

std::vector<uintptr_t> FindPointersTo(const ModuleInfo& module, uintptr_t target)
{
    std::vector<uintptr_t> hits;
    if (!module || !target) return hits;

    auto* base = reinterpret_cast<uint8_t*>(module.base);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);

    auto* section = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section)
    {
        if (!(section->Characteristics & IMAGE_SCN_MEM_WRITE)) continue;

        auto* start = base + section->VirtualAddress;
        size_t size = section->Misc.VirtualSize;

        // 按「已提交区域」整段扫，而不是每 8 字节一次 SafeRead。
        // 后者会在每个 qword 上做一次 VirtualQuery，几十 MB 的可写数据要跑几十万次系统调用，
        // 注入时那几秒的卡顿基本都来自这里。
        size_t offset = 0;
        while (offset + sizeof(uintptr_t) <= size)
        {
            const uint8_t* cursor = start + offset;

            MEMORY_BASIC_INFORMATION mbi{};
            if (!VirtualQuery(cursor, &mbi, sizeof(mbi)) ||
                mbi.State != MEM_COMMIT ||
                (mbi.Protect & (PAGE_NOACCESS | PAGE_GUARD)))
            {
                offset += 0x1000; // 整页跳过，别在这里逐字节试探
                continue;
            }

            size_t regionLeft = static_cast<size_t>(mbi.RegionSize) -
                                (cursor - static_cast<const uint8_t*>(mbi.BaseAddress));
            size_t end = offset + regionLeft;
            if (end > size) end = size;
            if (end <= offset) { offset += 0x1000; continue; }

            for (; offset + sizeof(uintptr_t) <= end; offset += sizeof(uintptr_t))
            {
                uintptr_t value = 0;
                std::memcpy(&value, start + offset, sizeof(value));
                if (value == target)
                    hits.push_back(reinterpret_cast<uintptr_t>(start + offset));
            }

            offset = end; // 处理掉区域尾部不足一个指针的那几个字节
        }
    }

    return hits;
}

uintptr_t FindPattern(const ModuleInfo& module, const char* pattern)
{
    if (!module) return 0;

    auto bytes = ParsePattern(pattern);
    if (bytes.empty()) return 0;

    auto* base = reinterpret_cast<uint8_t*>(module.base);
    auto* dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto* nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);

    auto* section = IMAGE_FIRST_SECTION(nt);
    for (unsigned i = 0; i < nt->FileHeader.NumberOfSections; ++i, ++section)
    {
        if (!(section->Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;

        auto* start = base + section->VirtualAddress;
        size_t size = section->Misc.VirtualSize;
        if (size < bytes.size()) continue;

        for (size_t offset = 0; offset <= size - bytes.size(); ++offset)
        {
            bool match = true;
            for (size_t j = 0; j < bytes.size(); ++j)
            {
                if (bytes[j].wildcard) continue;
                if (start[offset + j] != bytes[j].value)
                {
                    match = false;
                    break;
                }
            }

            if (match) return reinterpret_cast<uintptr_t>(start + offset);
        }
    }

    return 0;
}
}
