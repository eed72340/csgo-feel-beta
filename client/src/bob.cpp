#include "bob.h"

#include <algorithm>
#include <cmath>

namespace feel
{
namespace
{
constexpr float kTau = 6.2831853f;
constexpr float kDesignWidth = 1600.0f;

float Clamp(float value, float low, float high)
{
    return value < low ? low : (value > high ? high : value);
}
}

void BobReset(BobState& state)
{
    state = BobState{};
}

void BobFire(BobState& state, float kickDegrees)
{
    state.velocity += kickDegrees * 70.0f; // 与 GUI 的 Sim.Fire 相同
    state.flash = 1.0f;
}

float BobDecayPunch(float punch, float punchDecay, float dt)
{
    return punch * std::exp(-std::max(1.0f, punchDecay) * 0.95f * dt);
}

void BobUpdate(BobState& state, const BobSettings& s, float dt,
               float speed, float strafe)
{
    if (!std::isfinite(dt) || !std::isfinite(speed) || !std::isfinite(strafe) || dt <= 0.0f) return;
    dt = std::min(dt, 0.05f);

    speed = std::max(0.0f, speed);
    state.speed = speed;
    float ratio = Clamp(speed / 250.0f, 0.0f, 1.35f);
    float bob = s.bob > 0.5f ? 1.0f : 0.0f;

    // No idle clock: phase advances with travelled distance, not key states.
    float cyclesPerSecond = ratio / std::max(0.12f, s.bobCycle);
    state.phase += dt * kTau * cyclesPerSecond;
    if (state.phase > kTau * 1000.0f) state.phase -= kTau * 1000.0f;

    float lat = std::sin(state.phase) * s.amtLat * ratio * 44.0f * bob;
    float vert = std::sin(state.phase * 2.0f + 1.5707963f) * s.amtVert * ratio * 30.0f * bob;
    float lower = s.lowerAmt / 45.0f * ratio * ratio * 34.0f * bob;

    if (s.newBob > 0.5f)
    {
        vert = std::sin(state.phase * 2.0f) * s.amtVert * ratio * 20.0f * bob;
        lower *= 0.5f;
    }

    float shiftAmt = strafe >= 0.0f ? s.shiftRight : s.shiftLeft;
    state.lat = lat;
    state.vert = vert;
    state.lower = lower;
    state.shift = -strafe * shiftAmt * 9.0f * Clamp(speed / 120.0f, 0.0f, 1.0f) * bob;

    // 枪模上扬弹簧
    constexpr float stiffness = 320.0f;
    float zeta = Clamp(s.punchDecay / 18.0f, 0.3f, 3.0f);
    float damping = 2.0f * std::sqrt(stiffness) * zeta;
    state.velocity += (-stiffness * state.vmPitch - damping * state.velocity) * dt;
    state.vmPitch += state.velocity * dt;

    state.punch = BobDecayPunch(state.punch, s.punchDecay, dt);
    state.flash = std::max(0.0f, state.flash - dt * 11.0f);

    (void)kDesignWidth;
}
}
