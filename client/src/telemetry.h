#pragma once
#include <cstdint>
#include <cstddef>
namespace feel {
// 服务端每 tick 发布的完整快照。v2 在原来 40 字节预留区里补了动画参数，
// 总长仍是 128 字节，老客户端读到 v2 只是看到多出来的字段，不会错位。
#pragma pack(push, 8)
struct Telemetry {
    uint32_t magic, version, sequence, pid;
    uint64_t tick, steamId;
    float vx, vy, vz, yaw;
    uint32_t flags;
    int32_t alive, enabled, bob;
    float lower, lat, vert, cycle, shiftLeft, shiftRight;

    // ---- v2：手感动画（老服务端这里全是 0）
    float qieqiangAmount;   // 88 切枪下沉幅度
    float qieqiangTime;     // 92 回弹时长（秒）
    uint32_t animFlags;     // 96 kAnim* 位
    uint32_t fireSeq;       // 100 本机玩家真实开火次数
    uint32_t switchSeq;     // 104 本机玩家真实换枪次数
    float recoilScale;      // 108 viewmodel_recoil_scale
    float punchDecay;       // 112 view_punch_decay
    float punchPitch;       // 116 引擎真实后坐角 pitch（正 = 视角向下）
    float punchYaw;         // 120 引擎真实后坐角 yaw
    uint32_t punchFlags;    // 124 kPunchLive
};
#pragma pack(pop)
static_assert(sizeof(Telemetry) == 128);
static_assert(offsetof(Telemetry, vx) == 32);
static_assert(offsetof(Telemetry, shiftRight) == 84);
static_assert(offsetof(Telemetry, qieqiangAmount) == 88);
static_assert(offsetof(Telemetry, switchSeq) == 104);
static_assert(offsetof(Telemetry, punchDecay) == 112);
static_assert(offsetof(Telemetry, punchFlags) == 124);

// animFlags 位定义，与服务端 FeelTelemetry.AnimFlags 一一对应。
constexpr uint32_t kAnimQieqiang = 1u << 0;
constexpr uint32_t kAnimShangyang = 1u << 1;
constexpr uint32_t kAnimViewmodelRecoil = 1u << 2;

// punchFlags 位定义：本帧服务端读到了引擎真实后坐角。
constexpr uint32_t kPunchLive = 1u << 0;

// Stale/missing telemetry is not replaced by guessed keyboard movement.
bool ReadTelemetry(Telemetry& value);
void CloseTelemetry();
}
