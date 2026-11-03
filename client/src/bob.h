#pragma once

namespace feel
{
// Source-style approximation, not a recovered CS:GO viewmodel solver.
// Driven by measured velocity; engine draw/recoil animations remain native.

struct BobSettings
{
    float bob = 1.0f;          // cl_bob
    float newBob = 0.0f;       // cl_usenewbob
    float lowerAmt = 21.0f;    // cl_bob_lower_amt
    float amtLat = 0.33f;      // local legacy csgo/cfg/default.cfg
    float amtVert = 0.14f;
    float bobCycle = 0.98f;    // cl_bobcycle
    float shiftLeft = 1.5f;    // cl_viewmodel_shift_left_amt
    float shiftRight = 0.75f;  // cl_viewmodel_shift_right_amt
    float shangyang = 1.0f;    // cs_go_shangyang
    float viewmodelRecoil = 1.0f; // viewmodel_recoil
    float punchDecay = 18.0f;  // view_punch_decay
    float recoilScale = 1.0f;  // viewmodel_recoil_scale
    float runSpeed = 250.0f;   // sim_run_speed
};

struct BobState
{
    float phase = 0.0f;
    float speed = 0.0f;
    float lat = 0.0f;      // 设计空间像素（1600x1000）
    float vert = 0.0f;
    float lower = 0.0f;
    float shift = 0.0f;
    float vmPitch = 0.0f;  // 度
    float punch = 0.0f;    // 度
    float velocity = 0.0f;
    float flash = 0.0f;
};

void BobReset(BobState& state);
void BobFire(BobState& state, float kickDegrees);
void BobUpdate(BobState& state, const BobSettings& settings, float dt,
               float speed, float strafe);

// 视角上扬的指数衰减，单独暴露方便外部（相机挂钩）复用。
float BobDecayPunch(float punch, float punchDecay, float dt);
}
