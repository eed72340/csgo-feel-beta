#requires -Version 5.1
<#
    客户端插件注入脚本（仅用于 -insecure / 离线自建测试）。

    用法：
      1. 先启动 CS2（建议加启动项 -insecure），进到主菜单或地图
      2. powershell -ExecutionPolicy Bypass -File .\client\run.ps1
      3. 打开 client\build\csgo_feel_client_<pid>.log 查看诊断结果

    注意：本脚本只是把 DLL 加载进 cs2.exe 进程，不改动磁盘上的游戏文件。
          仅用于 -insecure / 离线自建环境；在线模式下加载会失败，本项目不做绕过。
#>
param(
    [string]$ProcessName = "cs2",
    [string]$Dll = "",
    [int]$Tail = 40
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $Dll) { $Dll = Join-Path $root "build\CSGOFeelClient.dll" }

# 注入器不随包分发，仓库只有源码。按「自编产物 -> 源码工程」的顺序找，
# 找不到就当场用 build-injector.ps1 编一个。
function Resolve-Injector {
    $candidates = @(
        (Join-Path $root "..\build\injector\csgo-feel-injector.exe"),
        (Join-Path $root "..\tools\injector\bin\Release\net10.0-windows\csgo-feel-injector.exe")
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }

    Write-Host "没有找到注入器，先用 build-injector.ps1 从源码编译……" -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root "..\build-injector.ps1")
    if ($LASTEXITCODE -ne 0) { throw "注入器编译失败" }

    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }
    throw "编译完成仍找不到注入器，检查 build-injector.ps1 的输出目录。"
}

$injector = Resolve-Injector

if (-not (Test-Path $Dll)) { throw "找不到 DLL：$Dll（先运行 build.ps1）" }

$procs = @(Get-Process $ProcessName -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    throw "进程 $ProcessName 未运行。请先启动 CS2（建议加启动项 -insecure），等进到主菜单或地图再运行。"
}

# 不要只认第一个：CS2 常常同时有启动器壳和游戏本体，注入器会自己挑加载了 client.dll 的那个。
Write-Host "候选进程: $(($procs | ForEach-Object { $_.Id }) -join ', ')" -ForegroundColor Cyan
Write-Host "使用范围: 仅在 -insecure / 离线自建环境使用；在线模式下加载会失败。" -ForegroundColor Yellow

$buildDir = Join-Path $root "build"
Get-ChildItem $buildDir -Filter "csgo_feel_client*.log" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

& $injector $ProcessName $Dll
$code = $LASTEXITCODE
if ($code -eq 2) {
    throw "注入已中止：没有任何 cs2 进程加载 client.dll。等游戏真正进到主菜单或地图后再运行本脚本。"
}
if ($code -ne 0) { throw "注入失败 (exit $code)" }

# 日志文件名带的是「真正被注入的那个进程」的 pid。注入器可能选了另一个 cs2.exe
# （启动器壳 vs 游戏进程），所以不能拿 $proc.Id 去猜，直接取最新出现的那个。
$log = $null
for ($i = 0; $i -lt 12; $i++) {
    Start-Sleep -Milliseconds 500
    $log = Get-ChildItem $buildDir -Filter "csgo_feel_client*.log" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($log) { break }
}

if ($log) {
    Write-Host ""
    Write-Host "---- $($log.Name) ----" -ForegroundColor Cyan
    Get-Content $log.FullName -Tail $Tail
} else {
    Write-Host "注入器返回成功，但 6 秒内没有生成日志 —— DLL 很可能没有真正加载。" -ForegroundColor Red
    Write-Host "检查：游戏是否带 -insecure、是否已进主菜单、DLL 是否 x64。" -ForegroundColor Yellow
}
