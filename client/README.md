# 客户端插件 · CSGOFeelClient

MSVC（C++20，`/MT` 静态链接）编译的 x64 DLL，由 `csgo-feel-injector.exe` 加载进 `cs2.exe`，
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
- DLL 起一个 ~250Hz 的线程，按 `bob.cpp` 的 CS:GO 公式算出 lat / vert / lower / shift，
  写进 `viewmodel_offset_x / y`（z 不动，避免穿模）
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
#    F9 = 让枪模上下扫 3 秒（验证写入通道）
#    F8 = 关闭 / 恢复 bob 与切枪动画
#    滚轮 / 侧键 / 1~5 / Q = 切枪时枪模先下沉再回弹（切枪动画）
```

日志：`build\csgo_feel_client_<pid>.log`（带 pid，避免多开时抢句柄；`_SH_DENYNO` 共享，注入中也能读）。

### 参数：`csgo_feel.ini`（与 DLL 同目录）

```ini
enabled=1        # 注入即生效
bob=1
new_bob=0
lower=21         # cl_bob_lower_amt
lat=0.4          # cl_bobamt_lat
vert=0.25        # cl_bobamt_vert
cycle=0.98       # cl_bobcycle
shift_left=1.5   # cl_viewmodel_shift_left_amt
shift_right=0.75
xscale=0.020     # bob 设计像素 -> viewmodel_offset 单位（觉得太猛就调小）
yscale=0.020
run_speed=250
walk_speed=130
crouch_speed=85
qieqiang=1          # 1 开启切枪动画，0 关闭
qieqiang_amount=0.55  # 下沉幅度（viewmodel_offset 单位）
qieqiang_time=0.35    # 回弹时长（秒）
wheel=1          # 1 滚轮 / 侧键也播切枪动画；0 只认 1~5 与 Q
server_sync=1    # 1 服务端在线时开关与幅度听服务端的；0 完全按本文件
diagnostics=0    # 1 = 注入时跑内存诊断（排查 ConVar RVA 失效用）；平时关着，否则注入会卡几秒
toggle_key=119   # F8
```

### 切枪动画怎么触发

`GetAsyncKeyState` 读不到滚轮（滚轮没有按下/抬起状态），侧键同理。所以按键轮询被换掉了，现在是两条链路去处重：

| 链路 | 位置 | 覆盖 |
| --- | --- | --- |
| 窗口消息 | `input_hook.cpp`：子类化游戏窗口的 `GWLP_WNDPROC`，拦 `WM_MOUSEWHEEL` / `WM_XBUTTONDOWN` / `WM_KEYDOWN` | 滚轮、侧键、1~5、Q |
| 服务端事件 | `state.cpp`：轮询共享内存里的 `switchSeq` | 任何方式的换枪：拾取、空枪自动切 |

两个源会在几十毫秒内先后到达，150ms 去重窗口合并成一次动画。服务端报告过一次换枪后，客户端就只认真实换枪事件、不再看按键（避免在聊天框打「2」误播）。窗口过程被游戏重置（换图 / 改分辨率）时，看门狗每 2 秒重新挂一次，日志里会留一行「窗口消息过程被重置，重新接管」。

没有用 `WH_MOUSE_LL`：低级鼠标钩子会让每帧的鼠标移动都绕到本线程再派发，等于给整个游戏加输入延迟，对打磨手感来说是负收益。

### 与服务端插件的通道

`Local\csgo_feel_v2`，80 字节命名共享内存，字段布局见根 README。要点：

- 服务端写 `enabled` / `qieqiang` / `amount` / `time` / `switchSeq` / 心跳，客户端 200ms 读一次；
- 客户端每秒写 `clientTick` 与能力位，服务端靠它显示「客户端已注入」；
- 服务端 15 秒没心跳（插件卸载或换图失败）客户端就退回 ini，不会跟着卡住；
- ini 里的 `qieqiang=0` 是本地否决，服务端也开不起来；
- **F8 也会走这条通道**：按下时把「切换总开关」写在 `reqSeq` / `reqCmd` 里，服务端 50ms 内执行并在聊天回显。因为服务端下发的 `bind F8 "say !csgo"` 有被拦掉的可能，这条链路是它的兜底；
- `server_sync=0` 或没有服务端插件时，行为与以前完全一致。

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
