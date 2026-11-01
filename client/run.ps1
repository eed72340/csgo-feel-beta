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
$injector = Join-Path $root "..\tools\injector\bin\Release\net10.0-windows\csgo-feel-injector.exe"

if (-not (Test-Path $Dll)) { throw "找不到 DLL：$Dll（先运行 build.ps1）" }
if (-not (Test-Path $injector)) { throw "找不到注入器：$injector（先运行 build.ps1）" }

$proc = Get-Process $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    throw "进程 $ProcessName 未运行。请先启动 CS2（建议加启动项 -insecure）。"
}

Write-Host "目标进程: $($proc.ProcessName) (pid $($proc.Id))" -ForegroundColor Cyan
Write-Host "使用范围: 仅在 -insecure / 离线自建环境使用；在线模式下加载会失败。" -ForegroundColor Yellow

$log = Join-Path $root "build\csgo_feel_client_$($proc.Id).log"
Get-ChildItem (Join-Path $root "build") -Filter "csgo_feel_client*.log" -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

& $injector $ProcessName $Dll
if ($LASTEXITCODE -ne 0) { throw "注入失败 (exit $LASTEXITCODE)" }

Start-Sleep -Seconds 3
if (Test-Path $log) {
    Write-Host ""
    Write-Host "---- $([IO.Path]::GetFileName($log)) ----" -ForegroundColor Cyan
    Get-Content $log -Tail $Tail
} else {
    Write-Host "没有生成日志，检查 DLL 是否被目标进程加载。" -ForegroundColor Red
}
