#define NOMINMAX
#include <windows.h>
#include <cmath>
#include <cstring>
#include "telemetry.h"
namespace feel {
namespace { HANDLE file = nullptr; const Telemetry* view = nullptr; }
bool ReadTelemetry(Telemetry& value) {
    if (!file) file = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\csgo_feel_telemetry_v1");
    if (!file) return false;
    if (!view) view = static_cast<const Telemetry*>(MapViewOfFile(file, FILE_MAP_READ, 0, 0, 128));
    if (!view) return false;
    for (int retry = 0; retry < 3; ++retry) {
        auto seq = reinterpret_cast<const volatile Telemetry*>(view)->sequence;
        if (seq & 1) continue;
        MemoryBarrier();
        std::memcpy(&value, view, sizeof(value));
        MemoryBarrier();
        if (seq != reinterpret_cast<const volatile Telemetry*>(view)->sequence) continue;
        const auto now = GetTickCount64();
        if (!(value.magic == 0x544C4546u && value.version >= 1 && value.version <= 2 &&
              value.sequence == seq && value.tick <= now && now - value.tick < 250 &&
              value.alive && std::isfinite(value.vx) && std::isfinite(value.vy) &&
              std::isfinite(value.vz) && std::isfinite(value.yaw) &&
              std::isfinite(value.lower) && std::isfinite(value.lat) &&
              std::isfinite(value.vert) && std::isfinite(value.cycle) &&
              std::isfinite(value.shiftLeft) && std::isfinite(value.shiftRight) &&
              std::fabs(value.vx) < 10000 && std::fabs(value.vy) < 10000))
            continue;

        if (value.version >= 2) {
            // 动画参数同样要求是有限值，否则宁可这一帧不播，也不要写进枪模。
            if (!std::isfinite(value.qieqiangAmount) || !std::isfinite(value.qieqiangTime) ||
                !std::isfinite(value.recoilScale) || !std::isfinite(value.punchDecay))
                continue;
            // 有真实后坐数据时必须是有限值；没有就把两个角清零，别让上一帧的残值参与计算。
            if (value.punchFlags & kPunchLive) {
                if (!std::isfinite(value.punchPitch) || !std::isfinite(value.punchYaw))
                    continue;
            } else {
                value.punchPitch = 0.0f;
                value.punchYaw = 0.0f;
            }
        } else {
            // v1 服务端：没有动画段，显式清零，避免结构体里残留上一帧的值。
            value.qieqiangAmount = 0.0f;
            value.qieqiangTime = 0.0f;
            value.animFlags = 0;
            value.fireSeq = 0;
            value.switchSeq = 0;
            value.recoilScale = 0.0f;
            value.punchDecay = 18.0f;
            value.punchPitch = 0.0f;
            value.punchYaw = 0.0f;
            value.punchFlags = 0;
        }
        return true;
    }
    return false;
}
void CloseTelemetry() {
    if (view) UnmapViewOfFile(view);
    if (file) CloseHandle(file);
    view = nullptr; file = nullptr;
}
}
