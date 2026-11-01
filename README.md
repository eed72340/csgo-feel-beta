# CS:GO Feel（非官方手感还原模组）

在 CS2 中还原 CS:GO 的视角模型晃动（viewmodel bob）、横移滞后与枪口上扬，并附带切枪表现。
项目包含三部分：离线调参器、服务端配置插件、客户端视觉模组。

> 非官方、非商业的开源 MOD，与 Valve Corporation 无任何关联，也未获其认可。

---

## 合规声明（请先阅读）

- 本项目只改变**本地视角模型的视觉表现**，不改变命中判定、伤害、弹道、移动速度或任何影响对抗公平性的数值。
- 客户端部分**仅用于以 `-insecure` 启动的离线 / 自建环境**。在线模式下游戏进程受完整性保护，
  加载会直接失败——这是预期行为；本项目**不包含、也不会尝试任何绕过保护或反作弊的代码**。
- 仓库**只发布源代码与构建脚本**，不提供预编译的可执行文件或 DLL。请自行从源码构建、审计并使用。
- 本项目不是外挂，不提供透视、自瞄、无后坐力等任何作弊功能。

---

## 三种形态

| 形态 | 构建产物 | 能做什么 | 使用环境 |
| --- | --- | --- | --- |
| 离线调参器 | `dist\CSGORestore.exe` | 离线调参，实时预览枪模晃动，导出 cfg | 任意，完全不接触游戏 |
| 服务端插件 | `dist\server\CSGOFeel.dll` | 保存每名玩家的参数，进服 / 出生时下发 | 自建 CS2 服务器 |
| 客户端视觉模组 | `dist\client\CSGOFeelClient.dll` | 在离线环境中改变枪模位置 | **仅 `-insecure` / 离线** |

CS2 客户端已经删除了 `cl_bob`、`cl_bobamt_lat`、`viewmodel_recoil`、`cl_viewmodel_shift_*`
这些指令，`client.dll` 里连字符串都搜不到。因此服务端插件下发的指令会被原生客户端忽略，
服务端插件只是配置中心；真正让参数生效的是装了客户端视觉模组的离线机器。

## 目录结构

```
.
├── build.ps1                  一键构建（GUI / 服务端 / 客户端）
├── deploy-server.ps1          构建后直接部署服务端插件
├── install-server.ps1         一键安装（只使用本地已编译产物，不自动编译）
├── install-server.bat         双击运行上面的脚本
├── set-dll-path.ps1           记录 CSGOFeel.dll 的位置（写进 dll-path.txt）
├── set-dll-path.bat           双击运行上面的脚本
├── client/                    客户端视觉模组源码（C++ / MSVC）
├── server/                    服务端插件源码（CounterStrikeSharp / C#）
├── gui/                       离线调参器（WPF）
├── tools/                     静态分析脚本、注入加载器、状态探针
├── LICENSE                    MIT
└── NOTICE.md                  第三方组件与来源说明
```

## 构建

需要 .NET SDK 10、MSVC（客户端 DLL）和 Windows SDK。

```powershell
# 全部构建（GUI + 服务端 + 客户端）
.\build.ps1

# 只构建服务端与客户端（跳过 GUI）
.\build.ps1 -SkipGui

# 构建并跑服务端离线测试
.\build.ps1 -SkipGui -Test
```

只想要注入加载器：

```powershell
.\build-injector.ps1                 # 产物 dist\client\csgo-feel-injector.exe
.\build-injector.ps1 -SelfContained  # 目标机没装 .NET 10 时用
```

Windows 下也可以双击 `build.bat`，按菜单选择。

产物落在 `dist\`：

- `dist\CSGORestore.exe`
- `dist\server\CSGOFeel.dll`
- `dist\client\CSGOFeelClient.dll` + `csgo-feel-injector.exe` + `csgo_feel.ini`

## 使用

### 1. 离线调参器

直接运行 `dist\CSGORestore.exe`：

- 单文件、免安装，不需要 .NET 运行时；
- 不依赖游戏、不注入进程，随时可用；
- 左侧调参、右侧 2D 预览，底部可导出可直接 `exec` 的 cfg。

这是本项目最安全的形态，推荐先从这里开始。

### 2. 服务端插件

前置：CS2 专用服务器已安装 Metamod:Source 与 CounterStrikeSharp。

**方式 A：一键安装（使用本地已编译产物）**

先构建一次：

```powershell
.\build.ps1 -SkipGui
```

然后双击 `install-server.bat`，或在 PowerShell 里执行：

```powershell
.\install-server.ps1
.\install-server.ps1 -Cs2Path "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
.\install-server.ps1 -Undeploy
```

脚本只使用现成产物（依次查找 `dist\server\CSGOFeel.dll`、`server\bin\Release\net10.0\CSGOFeel.dll`），
**不会自动编译**，也不做全盘搜索；本机找不到 DLL 时直接失败并提示指定路径。

如果 DLL 不在构建目录里（例如别人编译好发给你的），先双击 `set-dll-path.bat`，
把 `CSGOFeel.dll` 拖进窗口回车记下位置，之后直接双击 `install-server.bat` 即可；
路径保存在同目录的 `dll-path.txt`，失效时会自动回退并提示重设。也可以用
`.\install-server.ps1 -Dll "D:\下载\CSGOFeel.dll"` 临时指定一次。

**方式 B：手动安装**

1. 构建：`.\build.ps1 -SkipGui`，得到 `dist\server\CSGOFeel.dll`；
2. 在 CS2 服务器上创建目录：
   `<CS2 根目录>\game\csgo\addons\counterstrikesharp\plugins\CSGOFeel\`
3. 把 `CSGOFeel.dll` 复制进去；
4. 重启服务器，或在服务器控制台执行 `css_plugins load CSGOFeel`
   （已加载过时用 `css_plugins reload CSGOFeel`）；
5. 验证：控制台 `css_csgo` 有输出，玩家聊天框 `!csgo` 有回显。

`deploy-server.ps1`（构建后直接部署）与上面的一键安装等价，仍然可用：

```powershell
.\deploy-server.ps1
.\deploy-server.ps1 -Cs2Path "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
.\deploy-server.ps1 -Undeploy
```

部署后重启服务器。玩家配置保存在 `plugins\CSGOFeel\players.json`，按 SteamID 索引；
进服、出生时会自动下发一次。

### 3. 客户端视觉模组（仅离线）

> 这是唯一会触及游戏进程的部分。请只在 `-insecure` 启动的离线 / 自建环境中使用。

步骤：

1. 构建：`.\build.ps1 -SkipGui`，产物在 `dist\client\`；
2. 完全退出 CS2；启动 CS2，启动项加 `-insecure`；
3. 把 `csgo-feel-injector.exe`、`CSGOFeelClient.dll`、`csgo_feel.ini` 放在同一目录；
4. 在该目录执行：

   ```cmd
   csgo-feel-injector.exe cs2 CSGOFeelClient.dll
   ```

5. 进图验证：`F9` 让枪模上下扫 3 秒；`F8` 关闭 / 恢复晃动与切枪动画；
   切枪时枪模先下沉再回弹。

也可以使用 `client\watch.ps1`，在进程加载 `client.dll` 后自动尝试加载一次。

**在线模式下加载会失败，这是预期行为，本项目不做任何绕过。** 日志会记录失败原因。

### 怎么确认加载成功

- 同目录出现 `csgo_feel_client_<pid>.log`，开头有 `[csgo-feel]` 初始化行，
  并打印实际使用的 `viewmodel_offset_x ref=... rva=0x...`；
- 装了服务端插件时，聊天框 `!csgo` 会多显示一行「客户端 已加载」；
- `tools\state-probe.ps1` 可以直接读共享内存，看到客户端心跳与 `switchSeq`。

### 停止 / 换版本

- 临时关闭：按 `F8`，或在聊天框输入 `!csgo`；
- 彻底卸载：退出 CS2，DLL 随进程消失，不会改动磁盘上的游戏文件；
- 换新 DLL：必须先完全退出游戏，再重新加载。

## 指令

### 聊天框（服务端插件）

| 指令 | 作用 |
| --- | --- |
| `!csgo` / `!feel` | 查看总开关 / 预设 / 切枪 / 晃动全部状态；带参数时切换总开关 |
| `!qieqiang 0\|1\|toggle` | 切枪手感开关 |
| `!preset csgo` / `pro` / `cs2` / `off` | 切换预设 |
| `!shangyang 0\|1\|toggle` | 枪口上扬开关 |
| `!bob 0\|1\|toggle` | 手臂晃动开关 |
| `!csgocfg` | 重新下发当前配置 |

### 控制台

`css_csgo`、`csgo_feel 0|1|toggle`、`csgo_qieqiang 0|1|toggle`、`csgo_qieqiang amt 0.7`、
`csgo_qieqiang time 0.35`、`css_preset`、`css_shangyang`、`css_bob`、`css_csgocfg`。

### 预设

| 预设 | 内容 |
| --- | --- |
| `csgo` | CS:GO 默认，lower 21 / lat 0.4 / vert 0.25 / cycle 0.98 |
| `pro` | 低幅度晃动，lower 5 / lat 0.1 / vert 0.1 |
| `cs2` | CS2 原生，`cl_usenewbob 1` |
| `off` | 全部关闭 |

## 参数文件

`client\csgo_feel.ini` 与 DLL 放在同一目录：

```ini
enabled=1
bob=1
new_bob=0
lower=21          # cl_bob_lower_amt
lat=0.4           # cl_bobamt_lat
vert=0.25         # cl_bobamt_vert
cycle=0.98        # cl_bobcycle
shift_left=1.5
shift_right=0.75
xscale=0.020
yscale=0.020
run_speed=250
walk_speed=130
crouch_speed=85
qieqiang=1        # 切枪动画开关
qieqiang_amount=0.55
qieqiang_time=0.35
wheel=1           # 滚轮 / 侧键切枪也播动画
server_sync=1     # 服务端在线时以服务端为准
diagnostics=0     # 1 = 加载时跑内存诊断，平时关着
toggle_key=119    # F8
```

## 已知边界

- 客户端视觉模组仅在 `-insecure` 或完全离线的自建环境可用；**不适用于任何在线对局**。
- `client.dll` 更新后 ConVarRef RVA 会失效；定位脚本在 `tools\an\`，更新后需重新计算。
- 部分服务器会拦截服务端到客户端的指令；`feel.log` 会记录一次失败原因，之后不重复记录。
- 客户端 `server_sync=1` 且服务端在线时以服务端为准；但 ini 里的 `qieqiang=0` 是本地否决。

## 许可证与第三方

- 本项目代码以 [MIT](LICENSE) 发布。
- 服务端插件针对 [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) 编译，
  运行期由其加载。CounterStrikeSharp 为 GPL-3.0，并附带允许插件以 MIT 发布的例外条款，
  本项目据此使用 MIT。
- 晃动数值的灵感来源与 `client.dll` 偏移的获取方式见 [NOTICE.md](NOTICE.md)。
- Counter-Strike 2、CS:GO 及相关名称、标志归 Valve Corporation 所有。本项目与其无关联，
  也未获其认可；相关名称仅用于说明兼容性。

## 免责声明

本项目是离线 / 自建服务器的玩法 MOD 与手感研究工具，不包含任何用于线上对战的作弊功能，
也不提供绕过保护或反作弊的手段。请仅在 `-insecure` 或离线环境使用客户端部分，并自行承担使用风险。
