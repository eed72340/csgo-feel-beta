#pragma once

#include <windows.h>
#include <string>

namespace feel
{
void LogInit(HMODULE self);
void Log(const char* fmt, ...);
std::wstring ModuleDirectory(HMODULE module);
}
