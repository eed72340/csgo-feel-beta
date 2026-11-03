#requires -Version 5.1
<#
    一键安装服务端插件：只使用本地已经编译好的产物，不自动编译。

    运行前提：
      - 本机存在已编译好的 CSGOFeel.dll，来源按以下顺序确定：
          1) -Dll 参数显式指定
          2) set-dll-path.ps1 保存的路径（dll-path.txt）
          3) 发布目录 D:\code\csgo-feel-releases\server\CSGOFeel.dll 或 server\bin\Release\net10.0\CSGOFeel.dll
      - 本机安装了 CS2 专用服务器 + Metamod:Source + CounterStrikeSharp

    用法：
      .\set-dll-path.ps1                      # 先指定一次 DLL 路径，之后双击 install-server.bat 即可
      .\install-server.ps1
      .\install-server.ps1 -Cs2Path "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
      .\install-server.ps1 -Dll "D:\下载\CSGOFeel.dll"
      .\install-server.ps1 -Undeploy

    本脚本只复制现成产物，不会现场编译，也不做全盘搜索。
#>
param(
    [string]$Cs2Path = "",
    [string]$Dll = "",
    [switch]$Undeploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$configFile = Join-Path $root "dll-path.txt"

function Fail($message) {
    Write-Host ""
    Write-Host "[失败] $message" -ForegroundColor Red
    exit 1
}

# 显式参数优先，其次是 set-dll-path.ps1 保存的路径，最后才用包内构建产物。
if (-not $Dll -and (Test-Path -LiteralPath $configFile)) {
    $saved = (Get-Content -LiteralPath $configFile -Raw -ErrorAction SilentlyContinue)
    if ($saved) {
        $saved = $saved.Trim().Trim('"')
        if (Test-Path -LiteralPath $saved) {
            $Dll = $saved
            Write-Host "使用已保存的 DLL 路径（dll-path.txt）" -ForegroundColor DarkGray
        } else {
            Write-Host "已保存的路径已失效：$saved" -ForegroundColor Yellow
            Write-Host "请重新运行 .\set-dll-path.ps1 设置，本次改用包内产物。" -ForegroundColor Yellow
        }
    }
}

if (-not $Dll) {
    $candidates = @(
        (Join-Path 'D:\code\csgo-feel-releases' "server\CSGOFeel.dll"),
        (Join-Path $root "server\bin\Release\net10.0\CSGOFeel.dll"),
        (Join-Path $root "server\bin\Release\CSGOFeel.dll")
    )
    $Dll = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Dll -or -not (Test-Path -LiteralPath $Dll)) {
    Fail "找不到 CSGOFeel.dll。请先运行 .\set-dll-path.ps1 指定 DLL 路径，或运行 .\build.ps1 -SkipGui 编译。"
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
