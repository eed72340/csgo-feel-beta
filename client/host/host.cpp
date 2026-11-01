#include <windows.h>
#include <cstdio>

int wmain(int argc, wchar_t** argv)
{
    if (argc < 2)
    {
        wprintf(L"usage: host.exe <dll>\n");
        return 1;
    }

    wprintf(L"loading %s\n", argv[1]);
    HMODULE module = LoadLibraryW(argv[1]);
    if (!module)
    {
        wprintf(L"LoadLibraryW failed: %lu\n", GetLastError());
        return 1;
    }

    wprintf(L"loaded at %p, sleeping...\n", module);
    Sleep(3000);
    return 0;
}
