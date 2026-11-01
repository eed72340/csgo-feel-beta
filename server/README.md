# 服务端插件 · CSGOFeel

CounterStrikeSharp 插件（net10.0，API 1.0.377）。定位是「配置中心」：保存每名玩家的手感参数，
在进服和出生时通过 `ExecuteClientCommand` 下发到客户端。

## 构建 / 部署

```powershell
# 在仓库根目录
.\build.ps1 -SkipGui
.\deploy-server.ps1
```

默认由脚本自动探测 CS2 安装位置，也可以手动指定：

```powershell
.\deploy-server.ps1 -Cs2Path "D:\Steam\steamapps\common\Counter-Strike Global Offensive"
```

卸载：`.\deploy-server.ps1 -Undeploy`。

## 玩家指令

聊天框输入（`!` 前缀，命中后不广播）：

| 指令 | 说明 |
| --- | --- |
| `!csgo` / `!feel` | 查看总开关 / 预设 / 切枪 / 晃动全部状态；带参数时切换总开关 |
| `!qieqiang 0\|1\|toggle` | 切枪手感开关（不写参数则取反） |
| `!preset csgo\|pro\|cs2\|off` | 切换预设 |
| `!shangyang 0\|1` / `toggle` | `cs_go_shangyang` 枪口上扬 |
| `!bob 0\|1` / `toggle` | `cl_bob` 手臂晃动 |
| `!csgocfg` | 重新下发当前配置 |

控制台（`css_` 开头的为控制台指令）：

| 指令 | 说明 |
| --- | --- |
| `css_csgo` | 查看当前配置 |
| `csgo_feel 0\|1\|toggle` | CS:GO 手感总开关 |
| `csgo_qieqiang 0\|1\|toggle` | 切枪手感开关 |
| `css_preset` / `css_shangyang` / `css_bob` / `css_csgocfg` | 同聊天版 |

## F8 提示

玩家连入后插件自动下发 `bind F8 "say !csgo"`，按 F8 时聊天框回显 `csgo手感 开/关`，
同时把总开关取反。若服务器拦截「服务端 → 客户端」指令，这个绑定不会生效，
仍可用控制台 `csgo_feel` 切换。

## 切枪手感

`EventItemEquip` 触发后，下一帧把该武器实体的 `m_flNextPrimaryAttack` /
`m_flNextSecondaryAttack` 写成 `当前时间 + CS:GO deploy 秒数`，让切枪后的可开火时间
回到 CS:GO 的节奏（手枪 0.5s / 步枪与冲锋枪 1.0s / 狙击 1.25s / 机枪 1.5s，逐武器见
`DeployByName`）。offset 由 `Schema.GetSchemaOffset` 取得并校验范围，取不到时整个功能
自动禁用，不会写坏内存。


## 预设

| 预设 | 含义 |
| --- | --- |
| `csgo` | CS:GO 默认（lower 21 / lat 0.4 / vert 0.25 / cycle 0.98） |
| `pro` | 职业哥静音晃动（lower 5 / lat 0.1 / vert 0.1） |
| `cs2` | CS2 原生（`cl_usenewbob 1`） |
| `off` | 全部关闭 |

## 下发内容

`BuildCommands()` 按顺序下发：

```
cl_bob / cl_usenewbob / cl_bob_lower_amt / cl_bobamt_lat / cl_bobamt_vert / cl_bobcycle
cl_viewmodel_shift_left_amt / cl_viewmodel_shift_right_amt
viewmodel_recoil / view_punch_decay / cs_go_shangyang / viewmodel_recoil_scale
```

## 注意

- 当前 CS2 客户端已删除 `cl_bob*`、`viewmodel_recoil` 等 convar，原生客户端会**静默忽略**这些指令；
  只有装了客户端插件的机器才会真正改变手感。`view_punch_decay` 在 CS2 中仍然有效。
- 部分线上服务器会拦截服务器 → 客户端指令，`PushToClient()` 已 try/catch，失败只写一次控制台日志，不刷屏。
- 玩家数据存 `plugins\CSGOFeel\players.json`（按 SteamID 索引）。
- 切枪手感分两部分：服务端控制的是「切枪后可开火的时间」（`csgo_qieqiang` / `!qieqiang`），
  客户端枪模的下沉回弹动画由 `csgo_feel.ini` 的 `qieqiang=` 控制（F8 一起开关）。
  服务端不下发 convar，所以两者彼此独立。

## 代码结构

单文件 `CSGOFeelPlugin.cs`：

- `PlayerPrefs`：每玩家配置 + `FromPreset()`
- `CSGOFeelPlugin.Load()`：注册指令、聊天监听与事件
- `PushToClient()` / `BuildCommands()`：下发
- `OnChatCommand()`：`!csgo` / `!qieqiang`
- `BindToggleKey()`：给玩家下发 F8 绑定
- `OnItemEquip()` / `ApplyDeployTiming()` / `DeployByName`：切枪手感
- `LoadPrefs()` / `SavePrefs()`：JSON 持久化
