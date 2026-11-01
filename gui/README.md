# GUI · CS:GO 手感还原器

WPF（.NET 10）单文件桌面工具。左侧调参，右侧实时预览视图模型。

## 运行

- 直接用发布产物：`..\dist\CSGORestore.exe`（免安装，self-contained）
- 或本地调试：`dotnet run --project CSGORestore.csproj`

## 源码说明

| 文件 | 职责 |
| --- | --- |
| `Cvar.cs` | 全部指令定义（名称、范围、默认值、提示、是否仅模拟器） |
| `Sim.cs` | 晃动物理：`cl_bob` 双频 sin、`cl_usenewbob` 分支、下沉、横移滞后、上扬弹簧与指数回正 |
| `ViewmodelView.cs` | 2D 伪 3D 视图模型绘制（枪身、手套、准星、动效），60 FPS 定时器驱动 |
| `MainWindow.xaml(.cs)` | 布局、滑杆/开关绑定、预设、cfg 导出、控制台指令解析 |
| `CvarRow.cs` | 单条指令的 UI 行（滑杆 + 数值 + 重置） |

## 交互

- 按住 `W` 走动、`A`/`D` 横移、鼠标左键开火
- 控制台支持 `cl_bob 0` 这类指令，也支持 `preset csgo`、`export`、`copy`
- 「无人操作时自动走位」打开后会自己走动，方便观察曲线

## 移植到客户端插件

`Sim.cs` 与 `..\client\src\bob.cpp` 是同一套数学，改参数时请同步两边，避免观感不一致。
