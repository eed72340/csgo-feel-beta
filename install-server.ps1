#requires -Version 5.1
<#
    一键安装服务端插件：只使用本地已经编译好的产物，不自动编译。

    运行前提：
      - 已经构建过，存在以下任一文件：
          dist\server\CSGOFeel.dll
          server\bin\Release\net10.0\CSGOFeel.dll
      - 本机安装了 CS2 专用服务器 + Metamod:Source + CounterStrikeSharp

    用法：
      .\install-server.ps1
      .\install-server.ps1 -Cs2Path "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
      .\install-server.ps1 -Undeploy

    本机没有编译产物时，脚本会直接失败并提示先运行 build.ps1，不会尝试现场编译。
#>
param(
    [string]$Cs2Path = "",
    [string]$Dll = "",
    [switch]$Undeploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Fail($message) {
    Write-Host ""
    Write-Host "[失败] $message" -ForegroundColor Red
    exit 1
}

if (-not $Dll) {
    $candidates = @(
        (Join-Path $root "dist\server\CSGOFeel.dll"),
        (Join-Path $root "server\bin\Release\net10.0\CSGOFeel.dll"),
        (Join-Path $root "server\bin\Release\CSGOFeel.dll")
    )
    $Dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Dll -or -not (Test-Path $Dll)) {
    Fail "本机没有找到已编译的 CSGOFeel.dll。请先运行 .\build.ps1 -SkipGui 完成编译，再执行本脚本。"
}

$deploy = Join-Path $root "deploy-server.ps1"
if (-not (Test-Path $deploy)) { Fail "找不到 deploy-server.ps1，无法部署。" }

Write-Host "使用编译产物：$Dll" -ForegroundColor Cyan

$deployArgs = @{ Dll = $Dll }
if ($Cs2Path) { $deployArgs.Cs2Path = $Cs2Path }
if ($Undeploy) { $deployArgs.Undeploy = $true }

try {
    & $deploy @deployArgs
}
catch {
    Fail $_.Exception.Message
}

if (-not $Undeploy) {
    Write-Host ""
    Write-Host "安装完成。重启服务器，或执行：css_plugins load CSGOFeel" -ForegroundColor Green
}

exit 0
