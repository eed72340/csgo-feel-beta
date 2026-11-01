#pragma once

#include <cstdint>

namespace feel
{
// 接管游戏窗口的消息过程，捕获「切枪」类输入。
//
// 为什么不继续用 GetAsyncKeyState：
//   它只能读按键状态，滚轮不是按键（没有按下/抬起状态），侧键同理。
// 为什么不装 WH_MOUSE_LL：
//   低级鼠标钩子会让所有鼠标事件（包括每一帧的移动）都绕到我们的线程再派发，
//   等于给整个游戏加 1ms 级输入延迟——对一个打磨手感的东西来说不可接受。
// 子类化只多一次窗口过程回调，鼠标移动完全不受影响。
bool InputHookStart();
void InputHookStop();

// 看门狗：游戏换图 / 改分辨率时可能重建窗口或重置窗口过程，每 2 秒调一次即可。
void InputHookKeepAlive();

// 切枪输入的单调递增序号（0 = 还没捕获到）。
//
// 为什么不是时间戳：GetTickCount64 只有毫秒精度，而窗口消息在同一毫秒内可能来两条
// （快速滚两格）。调用方只能比较「变没变」，同一毫秒的两次变化会被当成一次漏掉。
// 序号每来一条消息 +1，比较序列号就不会丢。
uint32_t InputSwitchSeq();

bool InputHookActive();
} // namespace feel
