#pragma once

#include <cstdint>

namespace feel
{
// 客户端 DLL 与服务端插件（CSGOFeel.dll）之间的唯一通道。
//
// 双方都是本机进程，用一段命名共享内存交换「当前该不该播切枪动画」这类瞬时状态，
// 避免依赖 ConVar（客户端 DLL 没有注册 ConVar 的能力）和 ExecuteClientCommand
// （部分服务器会拦截服务端下发的客户端指令）。
//
// 布局必须与 server/FeelLink.cs 的 [StructLayout(Explicit)] 完全一致：
//   偏移 0  magic        4  version      8  enabled       12 qieqiang
//        16 amount       20 time         24 bob           28 switchSeq
//        32 serverTick   40 serverPid    44 serverBuild
//        48 clientTick   56 clientPid    60 clientFlags
//        64 reqSeq       68 reqCmd       72 reqAck        76 保留
// 共 80 字节。每个字段只有一个写者，因此不需要加锁。
#pragma pack(push, 8)
struct SharedState
{
    uint32_t magic;           // 'FEEL'，服务端写入；为 0 表示服务端还没上线
    uint32_t version;
    int32_t enabled;          // 总开关
    int32_t qieqiang;         // 切枪动画开关
    float qieqiangAmount;     // 下沉幅度
    float qieqiangTime;       // 回弹时长（秒）
    int32_t bob;              // 预留：服务端视角的晃动开关
    int32_t switchSeq;        // 服务端每检测到一次换枪 +1
    uint64_t serverTick;      // 服务端最后写入时间（GetTickCount64，毫秒）
    uint32_t serverPid;
    uint32_t serverBuild;
    uint64_t clientTick;      // 客户端心跳
    uint32_t clientPid;
    uint32_t clientFlags;     // bit0 窗口子类化已挂上 / bit1 按键识别 / bit2 共享内存可用
    uint32_t reqSeq;          // 客户端递增：有新的开关请求
    uint32_t reqCmd;          // 1 = 切换切枪动画，2 = 切换总开关
    uint32_t reqAck;          // 服务端已处理到哪个 reqSeq
    uint32_t reserved;
};
#pragma pack(pop)

static_assert(sizeof(SharedState) == 80, "SharedState 必须与服务端 FeelLink.cs 的 80 字节布局一致");

constexpr uint32_t kStateMagic = 0x4C454546u; // 'FEEL'
constexpr uint32_t kStateVersion = 3u;

constexpr uint32_t kFlagWindowHook = 1u << 0;
constexpr uint32_t kFlagKeyInput = 1u << 1;
constexpr uint32_t kFlagLink = 1u << 2;

constexpr uint32_t kReqToggleQieqiang = 1u;
constexpr uint32_t kReqToggleFeel = 2u;

// 创建（或打开）共享内存并映射视图。可以重复调用。
bool StateOpen();
void StateClose();

// 只读视图，未挂载时返回 nullptr。不要长期缓存指针。
const SharedState* StateView();

// 服务端是否在线：magic/version 对得上，且心跳没超过 maxAgeMs。
bool StateServerLive(int maxAgeMs = 15000);

// 客户端心跳：每秒调一次，顺便把能力位报给服务端（服务端用它在聊天里显示「客户端已注入」）。
void StateHeartbeat(uint32_t flags);

// 请求服务端执行一次开关（F8 / 本地按键走到这里）。
// 服务端每 50ms 轮询一次共享内存，处理后在聊天里回显，所以不依赖会被人拦截的 bind 指令。
void StateRequestToggle(uint32_t cmd);
} // namespace feel
