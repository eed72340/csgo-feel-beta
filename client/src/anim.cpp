#include "anim.h"

#include <algorithm>
#include <cmath>

namespace feel
{
namespace
{
constexpr float kTau = 6.2831853f;

// 曲线结束后仍要留一点余量，让 baseline（重触发接管的残余值）淡干净。
constexpr float kTail = 1.25f;

// exp(-2t)·sin(2πt) 在 t=0.25 处的峰值：用它把深度归一到 1.0，
// 这样 ini 里的 qieqiang_amount 就是肉眼看到的下沉量。
constexpr float kShapePeak = 0.6065307f;

// baseline 的淡出速度：0.2s 掉到 13%，0.35s 掉到 3%。
constexpr float kBaselineDecay = 10.0f;

float Clamp(float value, float low, float high)
{
    return value < low ? low : (value > high ? high : value);
}

// 归一化的下沉曲线：Shape(0)=0，Shape(0.25)=-1，Shape(1)=0，中途在 0.75 附近
// 有一次 +20% 的小回弹（像 CS:GO 起枪那一下）。
float Shape(float t)
{
    if (!(t > 0.0f) || t >= 1.0f) return 0.0f;
    return -(std::exp(-2.0f * t) * std::sin(kTau * t)) / kShapePeak;
}
} // namespace

// ------------------------------------------------------------------ DeployAnim

void DeployAnim::Start(float now, float amt, float dur)
{
    if (!std::isfinite(now) || !std::isfinite(amt) || !std::isfinite(dur)) return;

    // 连按切枪时不能从满幅重来：先把当前位置接管成 baseline，由它按指数淡出，
    // 新曲线从 0 起步。两者相加保证位置连续（C0），视觉上不会「跳一下再掉下去」。
    baseline = Sample(now);

    active = true;
    start = now;
    amount = std::fabs(amt);
    // 太短会跟「瞬移」一样突兀，太长会盖住下一次开火；下限 0.10s 与 ini 校验一致。
    duration = std::max(0.10f, dur);
}

void DeployAnim::Abort(float now)
{
    if (!active || !std::isfinite(now)) return;

    float value = Sample(now);
    baseline = value;
    amount = 0.0f;
    duration = 0.10f;
    start = now;
    // 已经贴近原位就不用再淡了，直接结束。
    active = std::fabs(value) > 1e-4f;
    if (!active) baseline = 0.0f;
}

void DeployAnim::Reset()
{
    active = false;
    start = 0.0f;
    baseline = 0.0f;
}

bool DeployAnim::Playing(float now) const
{
    if (!active) return false;
    if (!std::isfinite(now)) return false;
    return (now - start) < duration * kTail;
}

float DeployAnim::Sample(float now) const
{
    if (!Playing(now)) return 0.0f;

    float elapsed = now - start;
    // 时钟回退（理论上不会发生）：宁可不动，也不要往回甩一帧。
    if (!(elapsed > 0.0f)) return baseline;

    const float t = elapsed / duration;
    float value = baseline * std::exp(-kBaselineDecay * t) + amount * Shape(t);

    // 连按时 baseline 会叠加到新曲线上，可能比配置的深度更深。夹住上限，
    // 保证 ini 里的 qieqiang_amount 就是肉眼能看到的极限深度。
    if (amount > 0.0f) value = Clamp(value, -amount, amount);
    return value;
}

// ------------------------------------------------------------------ PunchMap

float PunchToOffset(float punchPitch, float gain)
{
    if (!std::isfinite(punchPitch) || !std::isfinite(gain)) return 0.0f;

    // 后坐角本身不会超过十几度；超出说明读到了脏数据，这一帧直接不用。
    if (std::fabs(punchPitch) > 30.0f) return 0.0f;

    return Clamp(-punchPitch * gain, -1.5f, 1.5f);
}

// ---------------------------------------------------------------- RecoilSpring

void RecoilSpring::Reset()
{
    offset = 0.0f;
    velocity = 0.0f;
}

void RecoilSpring::Kick(float amount)
{
    if (!std::isfinite(amount)) return;
    // 冲量给到速度上：offset 立刻开始变化，但由弹簧负责把它拉回来，
    // 所以连续开火会叠加出「越来越压不住」的手感，而不是每发都跳一下。
    // 正号 = 向上顶起（z 增大），与 lower 的减 z 下沉方向相反。
    velocity += std::fabs(amount) * 40.0f;
}

void RecoilSpring::Update(float dt, float punchDecay)
{
    if (!std::isfinite(dt) || dt <= 0.0f) return;
    if (!std::isfinite(offset) || !std::isfinite(velocity))
    {
        Reset();
        return;
    }

    dt = std::min(dt, 0.05f);

    constexpr float stiffness = 260.0f;
    // view_punch_decay 默认 18：映射成阻尼比 0.3~3.0，太小会抖，太大像被黏住。
    float zeta = Clamp(punchDecay / 18.0f, 0.3f, 3.0f);
    float damping = 2.0f * std::sqrt(stiffness) * zeta;

    float accel = -stiffness * offset - damping * velocity;
    velocity += accel * dt;
    offset += velocity * dt;

    // 收敛判定：避免无限小抖动被当成「还在动」。
    if (std::fabs(offset) < 1e-4f && std::fabs(velocity) < 1e-3f)
    {
        offset = 0.0f;
        velocity = 0.0f;
    }

    // 再兜一层：跨图 / 卡顿导致的大 dt 理论上不会把值顶飞，但别让它溢出。
    offset = Clamp(offset, -6.0f, 6.0f);
}

// ------------------------------------------------------------------ SwitchGate

void SwitchGate::Reset()
{
    serverSeq = 0;
    localSeq = 0;
    seededServer = false;
    seededLocal = false;
    localAt = -1e9f;
}

bool SwitchGate::OnLocal(uint32_t seq, float now)
{
    // 第一次看到本地序号：只对齐，不播——避免刚注入就凭空弹一下。
    if (!seededLocal)
    {
        seededLocal = true;
        localSeq = seq;
        return false;
    }

    if (seq == localSeq) return false;

    // 计数器回退（DLL 重新注入）只重新对齐，不当成一次切枪。
    if (seq < localSeq)
    {
        localSeq = seq;
        return false;
    }

    localSeq = seq;
    localAt = now;
    return true;
}

bool SwitchGate::OnServer(uint32_t seq, float now)
{
    if (!seededServer)
    {
        seededServer = true;
        serverSeq = seq;
        return false;
    }

    if (seq == serverSeq) return false;

    // 插件热重载 / 换图会把 switchSeq 归零。这不是「换了一次枪」，只重新对齐。
    if (seq < serverSeq)
    {
        serverSeq = seq;
        return false;
    }

    serverSeq = seq;

    // 本地刚按过：这条服务端事件就是那次输入的回执，动画已经播了，别播第二遍。
    return !LocalRecent(now);
}

bool SwitchGate::LocalRecent(float now) const
{
    return (now - localAt) < kLocalOwnWindow;
}
} // namespace feel
