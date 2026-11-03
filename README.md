# CS:GO Feel v0.2.0 — 本地 Legacy 对照重建预览版

本次在 `D:\code\csgo-feel-rebuild` 重建，基于提交 `870e868`，已并入 `main` 并以 `v0.2.0` 发布。没有覆盖旧版本仓库，没有修改游戏安装目录。

**状态：可构建的研究预览版，不是“全部 CS:GO 手感已复刻”，也没有完成端到端 1 显示帧延迟验收。**

## 已修正

- 以用户本地 `csgo legacy/csgo/cfg/default.cfg` 为证据：lat `0.33`、vert `0.14`、lower `21`、shift left/right `1.5/0.75`。cycle `0.98` 等未出现在该文件中的值仍是旧项目假设，不冒充本地实测默认。
- 客户端删除 WASD 猜速度、Space 固定 0.55 秒猜落地。
- **切枪动画按 v2 架构重新接上**：**本地窗口消息（滚轮 / 侧键 / 1~5 / Q）是主触发，按下即播**——
  CS2 的起枪动画是客户端预测的，服务端 `item_equip` 要等 usercmd 上行 + 事件派发 + 下个 tick 发布快照，
  40~80ms 已经过去了，只靠服务端触发必然「不同步」。服务端事件退化为**回执**（确认这次输入真的换了枪）
  与**兜底**（拾取 / 空枪自动切时补播）；200ms 内没等到回执就判定误触（聊天框打字、按了当前手持的枪位）
  并平滑撤销。曲线首尾都严格回到原位，`qieqiang_time` 就是从按下到归位的总时长。
- **枪口上扬跟随引擎真实后坐角**：`CCSPlayerPawn` 自己没有 `AimPunchAngle` 属性，但后坐角挂在组件上——
  `pawn.AimPunchServices.PredictableBaseAngle`（`QAngle`）。服务端每 tick 读一次，随遥测下发，
  客户端按 `viewmodel_recoil_scale` 缩放后直接跟随，衰减仍由引擎的 `view_punch_decay` 负责。
  组件未就绪（刚连入 / 重生 / 切图）时自动退回「`EventWeaponFire` 计数 + 固定 kick」的近似路径，
  两条路径都有开关，`!csgo diag` 会回显当前走的是哪一条。
- 新增本机服务端每 tick 发布真实水平/垂直速度、朝向、着地标志、存活状态和配置；客户端只消费完整快照。缺失、过期、死亡或 NaN 数据停用并恢复枪模基准。
- 枪模的纵向效果改写 `viewmodel_offset_z`，不再把前后轴 `y` 当上下轴。
- 停止按未经核实的 deploy 表改写武器攻击时间；切枪、换弹、射击、弹道和命中保留 CS2 原生。
- 显式配置本机玩家 SteamID，不再用最后连接的人作为共享内存拥有者。
- 客户端内部也要求 `-insecure`。不会绕过反作弊或完整性保护。

## 手感矩阵（客户端实际合成什么）

客户端只会写 `viewmodel_offset_x / z` 两个浮点（`y` 是前后轴，保持不动）。所有效果最后叠加成：

```
x = base_x + (bobLat + bobShift) * xscale
z = base_z + (bobVert - bobLower) * yscale + deploy(切枪) + recoil(上扬)
```

| 手感项 | 触发源 | 是否有实测依据 |
|---|---|---|
| 左右晃动 `bobLat` | 服务端真实速度 + 朝向 | 幅度 `cl_bobamt_lat 0.33` 来自 legacy `default.cfg` |
| 上下晃动 `bobVert` | 同上，双频 | `0.14` 同上 |
| 跑动下沉 `bobLower` | 同上，速度平方 | `cl_bob_lower_amt 21` 同上 |
| 横移滞后 `bobShift` | 速度在视线法线上的分量 | `1.5 / 0.75` 同上 |
| 切枪下沉回弹 | 本地窗口消息（主）+ 服务端换枪事件（回执 / 兜底） | 幅度/时长可调，默认 0.55 / 0.35s，属观感参数 |
| 枪口上扬 | 引擎真实后坐角 `AimPunchServices.PredictableBaseAngle` | 真数据：跟随引擎给的角度，衰减是引擎自己的 `view_punch_decay` |
| （兜底）上扬近似 | `EventWeaponFire` 计数 | 只在读不到后坐角时启用，`recoil_kick` 是观感参数 |

可调项在客户端 `csgo_feel.ini`（`qieqiang` / `qieqiang_amount` / `qieqiang_time` / `input_trigger` /
`recoil` / `recoil_punch_gain` / `recoil_kick`），
服务端侧用 `!qieqiang`、`csgo_qieqiang amt 0.7`、`csgo_qieqiang time 0.4`、`!shangyang` 实时调。

聊天指令（命中后不广播）：

| 指令 | 作用 |
| --- | --- |
| `!csgo` | 看配置，只回 4 行：启用状态 + 晃动 / 切枪 / 上扬 |
| `!csgo 0\|1\|toggle` | 开关总手感（F8 绑的是 `!csgo toggle`） |
| `!qieqiang [0\|1]` | 看 / 切切枪动画 |
| `!preset [csgo\|pro\|cs2\|off]` | 套预设；不带参数看当前 |
| `!shangyang 0\|1`、`!bob 0\|1` | 单项开关 |

需要排查时才用 `css_csgo diag`：在 4 行配置后面追加「后坐角是真实还是近似 / 客户端是否注入 / F8 绑定状态」。

## 遥测布局（128 字节，v2）

服务端 `FeelTelemetry.cs` 单线程写，`sequence` 奇数表示正在写；客户端校验序号、时效（250ms）、
有限值与存活后才消费。v2 在原来 40 字节预留区里补了动画参数，长度不变：

| 偏移 | 字段 | 偏移 | 字段 |
|---|---|---|---|
| 0/4/8/12 | magic / version / sequence / pid | 16/24 | tick / steamId |
| 32..44 | vx / vy / vz / yaw | 48/52/56/60 | flags / alive / enabled / bob |
| 64..84 | lower / lat / vert / cycle / shiftLeft / shiftRight | 88/92/96 | qieqiangAmount / qieqiangTime / animFlags |
| 100/104 | fireSeq（真实开火数） | 108/112 | recoilScale / punchDecay |
| 116/120 | punchPitch / punchYaw（引擎真实后坐角） | 124 | punchFlags（bit0 = 本帧有真实后坐） |

`animFlags`：bit0 切枪动画、bit1 枪口上扬、bit2 枪模上扬。
`punchFlags` bit0 为 0 时客户端自动退回开火计数近似。
老客户端只认 `version == 1` 时结构体里这些字段是 0，行为退回纯 bob。

## 独立移动补丁

产物：`D://code//csgo-feel-releases//movement//CSGOFeel.Movement.dll`（需 `build.ps1 -WithMovement`），与视觉客户端、GUI、原服务端插件独立。

它是**可选的松键快速停步玩法补丁**，不是 CS:GO 原版惯性：当已启用玩家存活、着地、普通行走、没有方向键和跳跃输入时，在 OnTick 中把水平速度归零，保留垂直速度。跳跃、空中、梯子、noclip、超过 350 u/s 的冲量不处理。

- 两道闸门都开才会改速度：服务端进程必须带 `CSGO_FEEL_MOVEMENT_LAB=1`，且受影响的 SteamID 被显式启用。
- `CSGO_FEEL_LOCAL_STEAMID` 指定的本机玩家默认算已启用，本机实验不必额外敲控制台命令。
- 放到自建服务器 `game/csgo/addons/counterstrikesharp/plugins/CSGOFeel.Movement/`。
- **服务器控制台**：`css_feelmove` 看闸门状态与计数；`css_feelmove <玩家slot> 0|1` 手动增减。普通玩家不能自己开启。
- 当前输入判定使用服务端数字按钮，不声称完整支持模拟摇杆。
- 无 NextFrame、延迟定时器或速度上限提升；但 OnTick 的输入/模拟阶段、客户端预测修正和真实停步时间仍需要进游戏测量。

### “1 帧”边界

这里实现的是“下一次服务端 OnTick 观察到松键即处理”的逻辑，不等于按键到屏幕 1 显示帧。64 tick 间隔约 15.625ms，而 144Hz 一帧约 6.94ms。不能用 2ms 轮询冒充 1 帧。客户端视觉仍是服务端快照+工作线程，未接入预测/渲染帧回调，不作显示延迟保证。

如果“1 帧快速”指一帧满速、一帧反向或别的动作，而不是松键快速停步，需要按该定义更换独立策略；本预览版没有把这些概念混为一谈。

## 构建

依赖：Windows、.NET SDK 10（服务端 / 注入器）、MSVC v143 + Windows SDK（客户端 DLL，C++20、`/MT`）。

一条命令，把**双端插件统一编成 DLL**，产物固定落在 `D:\code\csgo-feel-releases`：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1            # 只编双端
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test      # 编完顺带跑回归测试
powershell -ExecutionPolicy Bypass -File .\build.ps1 -OutDir "D:\somewhere\else"   # 换输出目录
```

产物（只有 DLL、配置和清单，**没有任何 exe**）：

```
D:\code\csgo-feel-releases\
├─ server\CSGOFeel.dll            服务端插件（CounterStrikeSharp）
├─ client\CSGOFeelClient.dll      客户端视觉模组（注入 cs2.exe）
├─ client\csgo_feel.ini           客户端配置
└─ BUILD.txt                      构建时间 / Git 提交 / SHA256 清单
```

可选产物都要显式加开关，默认不构建：

| 开关 | 产物 | 说明 |
| --- | --- | --- |
| `-WithMovement` | `movement\CSGOFeel.Movement.dll` | 独立移动补丁，默认关闭 |
| `-WithGui` | `gui\CSGORestore.exe` | 离线 2D 近似模拟器（32MB 单文件） |
| `-WithInjector` | 源码树 `build\injector\csgo-feel-injector.exe` | **只从源码现编，不放进交付目录** |

几个实现细节：

- 客户端 DLL 被正在运行的游戏加载时会锁文件，构建自动改用 `-Tag auto` 换名编译，`releases\client` 里仍按正式名同步；
- `build.ps1` 每次会清掉交付目录里的 `csgo-feel-injector.*` 与根目录遗留 exe，保证「交付目录只有 DLL」；
- 服务端 DLL 覆盖后 CounterStrikeSharp 会**热重载**，客户端 DLL 必须重启游戏重新注入。

## 注入器：为什么不随包分发

**仓库和发布包里都不会有 `csgo-feel-injector.exe`。** 只提供两样东西：

- 源码：`tools/injector/`（`Program.cs` 248 行 + `injector.csproj`，.NET 10 控制台程序）
- 编译脚本：`build-injector.ps1`

原因很直接：GitHub 的自动化风控对「远程线程注入 / 内存加载器」这类二进制非常敏感，
直接分发 exe 容易被判定为恶意软件而连带封号、下架仓库（本项目已经踩过一次）。
源码是可读的、行为可审计的，也不触发二进制扫描，所以只留源码。

### 自己编译（30 秒）

```powershell
# 只要 .NET SDK 10，不需要 MSVC
powershell -ExecutionPolicy Bypass -File .\build-injector.ps1

# 产物：build\injector\csgo-feel-injector.exe
# 目标机没装 .NET 10 运行时？打成单文件自包含：
powershell -ExecutionPolicy Bypass -File .\build-injector.ps1 -SelfContained
```

也可以手搓，一行就够：

```powershell
dotnet build .\tools\injector\injector.csproj -c Release
# 产物：tools\injector\bin\Release\net10.0-windows\csgo-feel-injector.exe
```

### 使用教程

1. Steam 里给 CS2 加启动项 `-insecure`（离线自建环境；在线模式是受保护进程，注入会失败，本项目不做任何绕过）。
2. 启动 CS2，进到主菜单或地图。
3. 把 `csgo-feel-injector.exe` 和 `CSGOFeelClient.dll` 放同一目录，执行：

   ```powershell
   .\csgo-feel-injector.exe cs2 CSGOFeelClient.dll
   # 多开时指定 pid：.\csgo-feel-injector.exe cs2:12345 CSGOFeelClient.dll
   ```

4. 看到 `注入成功，LoadLibraryW 返回模块句柄 0x...` 即成功；客户端日志在
   `client\build\csgo_feel_client_<pid>.log`，里面应出现「已接管游戏窗口」「共享内存已挂载」。
5. 嫌麻烦可以直接用 `client\run.ps1`：它自己找注入器，找不到就调 `build-injector.ps1` 现编一个再注入。

> 注入器只做 `OpenProcess` + `VirtualAllocEx` + `WriteProcessMemory` + `CreateRemoteThread(LoadLibraryW)`，
> 不改磁盘上的游戏文件、不碰反作弊、不联网。代码就在 `tools/injector/Program.cs`，几屏能读完。

## 部署到 CS2 根目录

`deploy-cs2.ps1` 把上面三部分装进 CS2，并把配置对齐到本机 legacy 的实测值。
幂等：重复执行只覆盖产物，每次覆盖前生成带时间戳的 `.bak_*` 备份。

```powershell
# 部署并对齐配置（不写环境变量）
powershell -ExecutionPolicy Bypass -File .\deploy-cs2.ps1 -LocalSteamId <你的SteamID64>

# 同时写入用户级环境变量：CSGO_FEEL_LOCAL_STEAMID / CSGO_FEEL_MOVEMENT_LAB=1
powershell -ExecutionPolicy Bypass -File .\deploy-cs2.ps1 -LocalSteamId <你的SteamID64> -SetEnvironment

# 只移除移动补丁（服务端插件与客户端文件保留）
powershell -ExecutionPolicy Bypass -File .\deploy-cs2.ps1 -Undeploy
```

部署会做这些事：

- `plugins\CSGOFeel\CSGOFeel.dll`、`plugins\CSGOFeel.Movement\CSGOFeel.Movement.dll`
- `csgo-feel\CSGOFeelClient.dll` + `csgo_feel.ini` + 注入器
- `players.json` 的 `Lat/Vert` 改成 `0.33 / 0.14`，`Qieqiang` 置 `true`（切枪动画重新接上）
- `cfg\autoexec.cfg` 里的 `cl_bobamt_lat / cl_bobamt_vert` 对齐（这些指令在 CS2 已失效，仅作记录）
- 覆盖后逐文件比对 SHA-256，并校验 `players.json` 仍是合法 JSON

环境变量是补丁的闸门：缺 `CSGO_FEEL_MOVEMENT_LAB=1` 时移动补丁完全不改速度；
缺 `CSGO_FEEL_LOCAL_STEAMID` 时服务端不发布本机遥测，客户端模组就不会晃动。
两处都是**用户级**变量，只对之后新启动的进程生效——先完全退出 CS2，再从 Steam 启动。

## 视觉模组使用（本机离线实验）

1. 自建 Windows CS2 服务器安装 Metamod 和 CounterStrikeSharp API 1.0.377 对应环境，将服务端 DLL 放在 `plugins/CSGOFeel/`。
2. **启动服务端前**在同一 PowerShell 设置 `$env:CSGO_FEEL_LOCAL_STEAMID='你的SteamID64'`，再从该进程启动服务器。若同时测试移动补丁，再设置 `$env:CSGO_FEEL_MOVEMENT_LAB='1'`；服务器设 `sv_lan 1`。
3. CS2 客户端以 `-insecure` 启动，仅连接本机离线实验服务器。不要用于公网对局。共享内存不支持另一台主机或 Linux 遥测。
4. 用自己编译的注入器加载 `CSGOFeelClient.dll`（见上一节）。客户端需要本机遥测才晃动，缺失时不会用键盘伪造。
5. F8 只切换本地视觉；服务端 `!preset csgo` 重置参数。旧 players.json 可能保留旧幅度，应显式重置。
6. 停用后恢复 x/y/z 基准；换 DLL 必须退出游戏。DLL 固定至进程退出，避免工作线程代码被 FreeLibrary 卸载。

## 验证与尚未完成

- 全部项目已构建；711 项移动策略断言通过。
- C++ 测试覆盖静止相位、移动、NaN dt、重置、128 字节共享布局、完整快照、奇数序号、超时、NaN 参数、死亡状态。
- 新增 `tests/anim_test.cpp`：切枪曲线（t=0 不瞬移、1/4 时长处达最低点且深度等于 amount、回到原点、尾部无台阶；连按重触发位置连续且不超过配置深度；误触撤销平滑淡出）、上扬弹簧（冲量方向、收敛回零、NaN dt / NaN 状态自愈）、后坐角映射（符号、线性缩放、±1.5 夹取、脏数据丢弃）、触发仲裁（本地优先、服务端回执被吞、拾取补播、序号回退只对齐）。
- 原服务端预设文本冒烟测试通过。
- `evidence/legacy-reference.json` 记录本地版本、5 个 bob 配置和二进制 SHA-256；VPK 目录读取 133676 条。目录中没有独立 `scripts/weapon_*.txt`，没有据此编造武器参数。
- 没有启动 CS2/Legacy 进行对照实测；未验证运行时 ConVar 绑定、CSS 加载、预测一致性、模拟摇杆、窗口热键与聊天输入场景。
- 当前机器 REA 没有可用的 Ghidra/Hopper/IDA 原生分析引擎；未恢复本地 CS:GO 的完整 bob、移动、后坐、散布、动画序列等算法。
- GUI 和客户端仍是近似模型，且 GUI 有独立演示平滑，不作逐帧一致承诺。
- 武器、后坐、换弹、声音、相机弹簧、原版移动、碰撞/台阶/坡面行为等完整复刻仍未完成。

背景参考：[Valve Source SDK 2013](https://github.com/ValveSoftware/source-sdk-2013)。Source SDK 的公开算法不能直接视作本地 CS:GO 算法，本预览版没有这样宣称。

仅限离线/本机自建研究，不含反作弊绕过。原 MIT/LICENSE/NOTICE 保留。
