#pragma once

namespace feel
{
struct BobApplySettings
{
    bool enabled = true;
    bool bob = true;
    bool newBob = false;
    float lowerAmt = 21.0f;
    float amtLat = 0.33f;
    float amtVert = 0.14f;
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
    // ini 里 qieqiang=0 是本地否决：服务端 csgo_qieqiang 1 也开不起来。
    bool qieqiang = true;
    float qieqiangAmount = 0.55f; // 下沉幅度（viewmodel_offset 单位）
    float qieqiangTime = 0.35f;   // 整个动画时长（秒），首尾都回到原位

    // 本地窗口消息（滚轮 / 侧键 / 1~5 / Q）作为**主**触发源：按下即播，零延迟。
    // 关掉它就只能等服务端事件（晚 40~80ms，会明显跟不上游戏的起枪动画）。
    bool inputTrigger = true;

    // 枪口上扬：每发真实开火给枪模一次下沉冲量，再按 view_punch_decay 回正。
    // 幅度是可见性近似，不按武器表编造后坐角度。
    bool recoil = true;
    float recoilKick = 0.30f;

    // 有真实后坐角（服务端 AimPunchServices）时改用直接跟随：
    // offset = -punchPitch * punchGain * viewmodel_recoil_scale。
    // gain 可以为负——游戏里方向反了就取反。
    float punchGain = 0.35f;

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
