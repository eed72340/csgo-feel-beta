# 第三方组件与来源说明

## CounterStrikeSharp

`server\` 针对 [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) 编译，
运行期由其加载。本项目不包含、也不修改 CounterStrikeSharp 的任何源代码。

CounterStrikeSharp 以 GPL-3.0 发布，其许可证附带例外条款：允许 **仅由 CounterStrikeSharp 插件与扩展**
（或引用其已发布 .NET 包构建的软件）以 MIT 协议发布。服务端插件属于该例外范围，本项目据此使用 MIT。
例外条款原文见上游仓库根目录的 `LICENSE`、`LICENSE.GPL3` 与 `LICENSE.MIT`。

## 代码与素材来源

- 本项目全部源代码为独立编写，**不包含 Valve 的任何代码、模型、贴图、音频或其他资源**；
- 仓库内没有任何从游戏安装目录提取的文件（`.vpk` / `.vmdl` / `.vtex` / `.cfg` 等）；
- `client.dll` 的 SHA256 仅用于标记偏移对应的游戏版本，属于运行时定位信息。

## 灵感来源

CS:GO 手臂晃动的数值与观感参考了 B 站视频 [BV1zZJZ67ELZ](https://www.bilibili.com/video/BV1zZJZ67ELZ)
（UP 主 TonySinke）。本项目没有使用该视频附带的任何文件，全部代码独立编写；
`gui\Sim.cs` 与 `client\src\bob.cpp` 是同一套公式的两份实现。

## client.dll 偏移

客户端模组中的函数地址与 ConVarRef 偏移（例如 `0x8CEB90`、`0x2536920`）由本项目自行静态分析得出，
分析脚本在 `tools\an\`。这些是运行时定位用的数值，不包含 Valve 的代码。
Steam 更新后偏移会失效，需要按 `client\README.md` 的步骤重新计算。

## 二进制分发策略

**Git 仓库本身不含任何预编译产物**（`.exe` / `.dll` / `.pdb` 全部在 `.gitignore` 里）。

GitHub 发行版的附件里放三样东西：

- `csgo-feel-src-v<版本>.zip`：对应 tag 的纯源码（不含二进制、不含注入器）；
- `CSGOFeel.dll` / `CSGOFeelClient.dll` + `csgo_feel.ini` + `BUILD.txt`：由**同一份 tag 源码**在本机构建出的双端产物，
  SHA256 记在 `BUILD.txt` 里，想省掉装 MSVC 的人可以直接用，也可以完全忽略它们自己构建。

**注入器（`csgo-feel-injector.exe`）永远不会出现在仓库或发行版里**：`tools\injector\` 只放源码，
`build-injector.ps1` 只负责在本地从源码编译，产物落在源码树的 `build\injector\`。

原因：远程线程注入 / 内存加载类可执行文件会触发 GitHub 的自动化恶意软件扫描，
直接分发容易牵连账号与仓库。源码可读可审计，也不会触发二进制扫描。

## 商标

Counter-Strike 2、CS:GO 及相关名称、标志归 Valve Corporation 所有。
本项目中出现的相关名称仅用于说明兼容性，不表示与 Valve Corporation 存在任何关联，也未获其认可。
