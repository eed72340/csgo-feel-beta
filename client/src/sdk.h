#pragma once

namespace feel
{
// 解析 tier0.dll!CreateInterface -> VEngineCvar007（只读，用来 dump vtable）。
bool InitCvar();

// 完整诊断流程：ICvar vtable + 用「字符串 -> 指针反查」定位 ConVar 对象并 dump 布局。
// 全程只读，不写游戏内存。
void RunDiagnostics();
}
