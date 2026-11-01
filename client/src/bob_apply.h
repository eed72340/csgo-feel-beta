#pragma once

namespace feel
{
struct BobApplySettings
{
    bool enabled = true;
    bool bob = true;
    bool newBob = false;
    float lowerAmt = 21.0f;
    float amtLat = 0.4f;
    float amtVert = 0.25f;
    float bobCycle = 0.98f;
    float shiftLeft = 1.5f;
    float shiftRight = 0.75f;

    // bob 的「设计像素」到 viewmodel_offset 单位的换算（可调）
    float xScale = 0.020f;
    float yScale = 0.020f;

    float runSpeed = 250.0f;
    float walkSpeed = 130.0f;
    float crouchSpeed = 85.0f;

    // 切枪动画：换枪时枪模先下沉再回弹，模拟 CS:GO 的起枪动作。
    // 触发源是「真实换枪事件」（服务端）+「窗口消息」（滚轮/侧键/1~5/Q），两源去重。
    bool qieqiang = true;
    float qieqiangAmount = 0.55f; // 下沉幅度（viewmodel_offset 单位）
    float qieqiangTime = 0.35f;   // 回弹时长（秒）

    // 滚轮 / 侧键也算切枪（靠子类化游戏窗口拿到消息）
    bool wheel = true;

    // 服务端插件在线时，开关和幅度以服务端为准（csgo_qieqiang / !qieqiang 立刻生效）
    bool serverSync = true;

    // 启动时跑内存诊断（字符串 -> 指针反查、ConVar 对象 dump）。
    // 这是开发期定位用的重活：要在 client.dll 的可写节里逐指针扫好几遍，
    // 并往日志里刷几十 KB，注入时的那几秒卡顿主要来自它。默认关闭。
    bool diagnostics = false;

    int toggleKey = 0x77; // F8：急停 / 恢复
};

// 启动后台线程：按帧读写 viewmodel_offset_x/y/z，让枪模按 CS:GO 的规律晃动。
bool BobApplyStart();
void BobApplyStop();
void BobApplyRestore();

// ini 里写了 diagnostics=1 才为 true（只在 BobApplyStart 读配置之后有效）。
bool BobApplyDiagnostics();
}
