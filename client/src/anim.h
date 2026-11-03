#pragma once

#include <cstdint>

namespace feel
{
// 手感动画层：只吃「时间 / 序号 / 幅度」这些纯数值，不碰 CS2 类型，
// 因此可以在离线测试里逐条断言（tests/anim_test.cpp）。
//
// 所有输出都是 viewmodel_offset 单位，符号约定与 bob_apply 一致：
//   z 越小 → 枪模越靠下（下沉）；x 越大 → 越靠右。

// 切枪动画：换枪瞬间枪模先下沉，再回到原位。
//
// 曲线的四条硬约束——「同步」与否全看这几条：
//   1. t=0 严格为 0：起手不能瞬移。之前用 cos(0)=1，触发那一帧直接从 0 跳到 -amount，
//      再叠加几十毫秒的触发延迟，看起来就是「枪模突然掉下去」。
//   2. t=duration 严格为 0：收尾不留台阶，duration 就是玩家在 ini 里写的那个时长。
//   3. 最低点在 t≈0.25·duration，深度恰好等于 amount：ini 里的数值就是肉眼看到的深度。
//   4. 重复触发时用 baseline 接管上一段的残余值并按指数淡出：连按切枪位置连续，不会满屏乱跳。
struct DeployAnim
{
    bool active = false;
    float start = 0.0f;     // 触发时刻（秒，单调时钟）
    float amount = 0.55f;   // 下沉幅度（正数表示「向下的量」）
    float duration = 0.35f; // 整个动画时长（秒），首尾都为 0
    float baseline = 0.0f;  // 重触发时接管的残余偏移，按 exp(-10t) 淡出

    void Start(float now, float amount, float duration);

    // 误触撤销（例如在聊天框里按 2）：从当前值平滑淡出，不产生台阶。
    void Abort(float now);

    void Reset();

    // 是否还在播放（曲线结束后再留一点 baseline 淡出余量）。
    bool Playing(float now) const;

    // 当前纵向偏移，<= 0（向下）。未播放时返回 0。
    float Sample(float now) const;
};

// 引擎真实后坐角 → 枪模纵向偏移。
//
// Source 的 QAngle.X 正方向是「视角向下」，而后坐是把视角顶上去（X 变负）；
// 枪模要跟着上抬（+z），所以这里取负号。gain 允许为负，方便在游戏里校正方向：
// 如果开火时枪模往下走，把 csgo_feel.ini 的 recoil_punch_gain 取反即可。
// 结果再夹到 ±1.5，避免异常数据把枪模推飞。
float PunchToOffset(float punchPitch, float gain);

// 枪口上扬（开火计数兜底路径）：每发子弹叠加一次向上的冲量，之后按 view_punch_decay 收敛回 0。
// 符号约定与 bob 一致：z 越大越靠上，所以「上扬」是正冲量（下沉的 lower 是减 z）。
// 这是可见性近似——不按武器表编造后坐角度，幅度由调用方给的倍率决定。
struct RecoilSpring
{
    float offset = 0.0f;   // 当前纵向偏移（>= 0 表示被顶上去）
    float velocity = 0.0f; // 变化率（单位/秒）

    void Reset();
    void Kick(float amount);                  // amount > 0 表示向上顶起的量
    void Update(float dt, float punchDecay);  // punchDecay 越大回正越快
};

// 本地按下沿与服务端换枪事件的仲裁窗口。
//
// 触发顺序改成「本地优先」之后，这两者不再是「去重」，而是「主路径 + 回执」：
//   · 本地按下 → 立刻播（零延迟，和客户端预测的起枪动画同一帧）；
//   · 服务端事件晚 40~80ms 到 → 只当回执吞掉，用来确认这次输入真的换了枪；
//   · 服务端事件前 kLocalOwnWindow 内没有本地输入 → 是拾取 / 空枪自动切，补播。
constexpr float kLocalOwnWindow = 0.30f;

// 本地按下后多久还没等到服务端确认，就判定为误触（聊天框打字 / 按了当前手持的枪位）。
// 真实换枪的回执通常 40~80ms 就到，200ms 足够富余；此时曲线也已走过最低点，
// 撤掉几乎是看不见的。
constexpr float kUnconfirmedCancel = 0.20f;

struct SwitchGate
{
    uint32_t serverSeq = 0;
    uint32_t localSeq = 0;
    bool seededServer = false;
    bool seededLocal = false;
    float localAt = -1e9f; // 最近一次本地按下沿的时刻

    void Reset();

    // 本地窗口消息序号（滚轮 / 侧键 / 1~5 / Q）。首次调用只对齐基准。
    bool OnLocal(uint32_t seq, float now);

    // 服务端真实换枪序号。首次调用只对齐；序号回退（插件热重载归零）也只对齐。
    // 本地刚按过的那次不重复播，返回 false。
    bool OnServer(uint32_t seq, float now);

    // 上一次本地按下距今是否还在仲裁窗口内。
    bool LocalRecent(float now) const;
};
} // namespace feel
