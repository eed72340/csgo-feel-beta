# 客户端插件 · CSGOFeelClient

MSVC（C++20，`/MT` 静态链接）编译的 x64 DLL，由一个**从源码自行编译**的注入器
（`build-injector.ps1` → `build\injector\csgo-feel-injector.exe`，仓库不分发该 exe）加载进 `cs2.exe`，
把 **CS:GO 的手臂/枪模晃动（cl_bob）** 在 CS2 里还原出来。

> ⚠️ **仅限 `-insecure` 自建服务器 / 离线环境**。在线模式下游戏进程受完整性保护，
> 加载会直接失败；这是预期行为，本项目不含、也不会尝试任何绕过保护或反作弊的代码。

## 实现方式：不挂钩子，直接驱动 `viewmodel_offset_*`

CS2 删掉了 `cl_bob*`、`viewmodel_recoil`、`cl_viewmodel_shift_*`，但**保留了 viewmodel 偏移三件套**，
而且每帧都会读它们并作用到枪模上。所以不需要给渲染管线打钩子：

```
client.dll!0x8CEB90  ==  GetViewmodelOffsets(pawn, float* offsetXYZ, float* fov)
    lea rcx, [rip+...]        ; ConVarRef viewmodel_offset_x
    call 0x160AF0             ; ConVarRef::GetFloat：rax=[rcx+8]（ConVar*），xmm0=[rax+0x58]
    ...
    再 clamp 到 [-2.0, 2.5] 后交给 viewmodel 变换
```

- `ConVarRef` 对象（`client.dll` 内固定地址）：`0x2536920` = x，`0x2536930` = y，`0x2536940` = z
- `ConVarRef + 0x08` → `ConVar*`，`ConVar + 0x58` → `float m_fValue`
- DLL 起一个工作线程，按 `bob.cpp` 的 CS:GO 公式算出 lat / vert / lower / shift，
  写进 `viewmodel_offset_x`（左右 + 横移）与 `viewmodel_offset_z`（上下 - 下沉）；
  `y` 是前后轴，**不动**（旧版把它当上下轴是错的）
- 同一线程再叠加两层动画：**切枪下沉回弹**（本地窗口消息触发，服务端事件做回执 / 兜底）与**枪口上扬**
  （跟随服务端下发的引擎真实后坐角 `AimPunchServices.PredictableBaseAngle`，
  按 `viewmodel_recoil_scale` 缩放；读不到时退回开火计数 + `recoil_kick` 近似）
- 速度来自服务端遥测（真实 `AbsVelocity`），不再按 WASD 猜
- 这三个地址写死会随 Steam 更新失效，`cvar.cpp` 会按注册函数的代码特征重新扫描（见下）

### 已确认的事实（2026-10-31，client.dll 39,224,472 字节）

| 项 | 结果 |
| --- | --- |
| `cl_bob` / `cl_bobamt_lat` / `cl_bobamt_vert` / `cl_bob_lower_amt` / `cl_bobcycle` | 字符串 MISS |
| `cl_usenewbob` / `viewmodel_recoil` / `cl_viewmodel_shift_*` | 字符串 MISS |
| `viewmodel_offset_x/y/z`、`view_punch_decay` | 存在 |
| `client.dll` SHA256 | `d618bbf8e6afb36baa09b3a282035996a88300458c55d2aa5cb81f0495c0ec54` |

> Steam 更新后 `client.dll` 变了，上面的 RVA 会失效——DLL 会自动按代码特征重新定位，
> 并在日志里打印实际使用的 rva（`viewmodel_offset_x ref=... rva=0x...`）。
> 想手工重算时用 `tools\an\*.py`，见文末。
> DLL 运行时会**校验 ConVar 名字**，对不上会拒绝写入并把候选对象 dump 到日志，不会写坏游戏。

## 用法

```powershell
# 1) 编译（-Tag 避免覆盖正在被游戏占用的 DLL）
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Tag bob1

# 2) 完全退出 CS2（受保护进程无法 taskkill，只能在 Steam 里正常退出）

# 3) 先开监视器，再启动 CS2（启动项加 -insecure）
powershell -ExecutionPolicy Bypass -File .\watch.ps1 -Tag bob1

# 4) 主菜单被注入后，进图测试：
#    F8 = 关闭 / 恢复本地视觉（不动服务端偏好）
#    滚轮 / 侧键 / 1~5 / Q = 切枪时枪模先下沉再回弹（切枪动画）
#    开火 = 枪模上扬后回正（需要服务端插件在线）
```

日志：`build\csgo_feel_client_<pid>.log`（带 pid，避免多开时抢句柄；`_SH_DENYNO` 共享，注入中也能读）。

### 参数：`csgo_feel.ini`（与 DLL 同目录）

```ini
enabled=1        # 注入即生效
bob=1            # 手臂晃动（幅度来自服务端 PlayerPrefs，不在 ini 里）
xscale=0.020     # bob 的横向换算（觉得太猛就调小）
yscale=0.020     # bob 的纵向换算（历史名字，作用在 Z 轴）
diagnostics=0    # 1 = 注入时跑内存诊断（排查 ConVar RVA 失效用）；平时关着

qieqiang=1            # 1 开启切枪动画；0 是本地否决，服务端也开不起来
qieqiang_amount=0.55  # 下沉深度（曲线最低点就是它，首尾都回到原位）
qieqiang_time=0.35    # 从按下到归位的总时长（秒）
input_trigger=1       # 1 = 窗口消息当主触发，按下即播；0 = 只等服务端事件（会晚 40~80ms）

recoil=1        # 枪口上扬（跟随引擎真实后坐角后回正）
recoil_punch_gain=0.35  # 真实后坐角换算增益；方向反了就取反
recoil_kick=0.30  # 只在拿不到真实后坐角时使用的每发下沉量
```

### 切枪动画怎么触发（同步的关键）

`GetAsyncKeyState` 读不到滚轮（滚轮没有按下/抬起状态），侧键同理。所以按键轮询被换掉了，
改成子类化游戏窗口的 `GWLP_WNDPROC`，拦 `WM_MOUSEWHEEL` / `WM_XBUTTONDOWN` / `WM_KEYDOWN`。

触发顺序是**本地优先**，不是「两源去重」：

| 链路 | 位置 | 角色 |
| --- | --- | --- |
| 窗口消息 | `input_hook.cpp` | **主触发**：按 3 那一帧就播 |
| 服务端 `switchSeq` | `telemetry.h`（服务端 `EventItemEquip` 计数） | **回执 + 兜底**：确认这次输入真的换了枪；拾取 / 空枪自动切时补播 |

为什么必须本地优先：CS2 的起枪动画是客户端预测的，按下即开始；而服务端要等
usercmd 上行 → `item_equip` 派发 → 下个 tick 发布快照，40~80ms 已经过去了。
只靠服务端触发，每一次切枪的动画都会晚半拍——这就是「完全不同步」的来源。

配套的三条时序约束：

- **回执被吞**：本地按过后 `kLocalOwnWindow`（300ms）内的服务端事件不再播第二次；
- **误触撤销**：本地按过后 200ms 仍未收到回执（聊天框打字、按了当前手持的枪位），
  从当前值平滑淡出——撤销后仍刷新窗口，稍后到达的回执不会再补播一遍；
- **序号回退只对齐**：插件热重载把 `switchSeq` 归零、DLL 重新注入把本地计数归零，
  都不会被当成一次换枪。

曲线本身也规定了首尾必须是 0：`t=0` 从原位起步（旧版 `cos(0)=1` 会直接跳到满幅下沉，
叠加触发延迟后看起来就是「枪模突然掉下去」），`t=duration` 精确回到 0。
连按切枪时残余值由 `baseline` 接管并按 `exp(-10t)` 淡出，位置连续且不会比配置的深度更深。

没有用 `WH_MOUSE_LL`：低级鼠标钩子会让每帧的鼠标移动都绕到本线程再派发，等于给整个游戏加输入延迟，对打磨手感来说是负收益。

日志里能直接看到时序：`切枪动画 本地→服务端确认 63ms（seq=18）`、
`切枪动画 撤销一次未确认的输入（服务端 200ms 内没有换枪）`。

### 与服务端插件的通道

两条共享内存，都是本机进程间，不依赖 ConVar 与 `ExecuteClientCommand`：

| 通道 | 大小 | 方向 | 用途 |
| --- | --- | --- | --- |
| `Local\csgo_feel_telemetry_v1` | 128 B | 服务端 → 客户端 | 每 tick 快照：速度、着地、朝向、bob 幅度、切枪参数、开火计数 |
| `Local\csgo_feel_v2` | 80 B | 双向 | 客户端心跳 / 能力位；F8 请求（`reqSeq` / `reqCmd`），服务端 50ms 轮询并在聊天回显 |

- 遥测用 `sequence` 奇偶 + 250ms 时效 + 有限值校验，任何一项不满足就**不写枪模**并恢复基准；
- 服务端换图 / 死亡 / 掉线时 `magic` 清零或快照失效，客户端会自动停手，不会冻在最后一帧；
- 老服务端（version 1）只给 bob，切枪动画那段字段为 0，客户端自动退回「只有 bob」。

排查用 `tools\state-probe.ps1`（加 `-Watch` 持续刷新），能直接看到两侧心跳和 `switchSeq` 变化。

### 关于进程保护

在线模式下 CS2 会把自身标记为受保护进程（PPL），此时远程加载会失败并返回系统错误
（`CreateRemoteThread` 87、`STATUS_PROCESS_IS_PROTECTED`）。

**这是预期行为，本项目不会尝试绕过。** 客户端模组只面向 `-insecure` 离线环境；
在该环境下进程不受保护，加载正常。失败时查看日志里的错误码即可。

## 已知局限

1. **只有平移，没有旋转**：`viewmodel_offset_*` 只能移动枪模位置，做不出 CS:GO 的 viewmodel roll/pitch
   （这也是下一步要挂钩子才能补的部分）。切枪动画同理：只能做「下沉 → 回弹」的位移曲线，
   不是 CS:GO 那种绕手部关节翻转的完整动作，但节奏（起枪 → 稳住）与 CS:GO 一致。
2. **速度来自键盘推算**（WASD/Shift/Ctrl/Space），不是真实 `m_vecAbsVelocity`：贴墙走、被减速时会略有不符。
3. **只影响枪模，不影响世界镜头**：这是有意的（CS:GO 的 bob 本来也只晃枪）。
4. 玩家在游戏内改「视角模型偏移」设置会被下一帧覆盖，直到按 F8 关闭。
5. **共享内存只有一个槽位**：只跟随本机最后连入的那名玩家。同一台机器多开客户端时，动画触发只到其中一人。
6. 注入后换新版 DLL 仍需重启游戏（进程内已加载的模块不会被替换）。
7. 同一个 `cs2.exe` 只允许一份 DLL 生效（进程级命名互斥体）：重复注入时后进来的那份只加载、不接管，
   日志会写明。否则两个窗口子类会互相把对方的 `WNDPROC` 当成「被游戏重置」，每 2 秒互抢一次。

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `src/main.cpp` | DllMain + 初始化线程（先启动 bob，再跑诊断） |
| `src/cvar.cpp/.h` | 定位 `viewmodel_offset_x/y/z` 并读写 `ConVar::m_fValue` |
| `src/bob_apply.cpp/.h` | 250Hz 线程：按键 → bob / 切枪动画 → 写 ConVar；F8 急停、F9 扫动、退出还原 |
| `src/input_hook.cpp/.h` | 子类化游戏窗口拿滚轮 / 侧键 / 切枪按键，带 2 秒看门狗重挂 |
| `src/state.cpp/.h` | 与服务端插件共用的 64 字节共享内存（心跳、开关、`switchSeq`） |
| `src/bob.cpp/.h` | 晃动物理（与 GUI 的 `Sim.cs` 同源） |
| `src/log.cpp` | 线程安全 UTF-8 日志（每进程一个文件，`_SH_DENYNO`） |
| `src/mem.cpp` | 模块信息、`SafeRead`（VirtualQuery 校验）、特征码/指针扫描 |
| `src/sdk.cpp` | `CreateInterface` → `VEngineCvar007`、ICvar vtable dump（只读诊断） |
| `host\host.cpp` | 最小宿主，验证 DLL 在非游戏进程不崩 |

## 重新定位（Steam 更新后）

```powershell
cd tools\an

# 1) 找字符串 + 引用它的注册函数
python scan_xref.py "<...>\client.dll" viewmodel_offset_x viewmodel_offset_y viewmodel_offset_z

# 2) 按 .pdata 边界看注册函数，拿 ConVarRef 的 lea rcx,[rip+...] 目标
python func_at.py "<...>\client.dll" <注册函数 rva>

# 3) 检查 ConVarRef 被哪里使用（应能看到 GetViewmodelOffsets 与两个上层调用者）
python scan_obj.py "<...>\client.dll" <ConVarRef VA> 0x100
```
