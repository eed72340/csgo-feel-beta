#requires -Version 5.1
<#
    记录 CSGOFeel.dll 的位置，install-server.ps1 会直接使用它，不用每次指定。

    用法：
      .\set-dll-path.ps1                                   # 交互输入，可把文件拖进窗口
      .\set-dll-path.ps1 -Path "D:\下载\CSGOFeel.dll"
      .\set-dll-path.ps1 -Clear                            # 清除已保存的路径

    路径保存在本脚本同目录的 dll-path.txt。
#>
param(
    [string]$Path = "",
    [switch]$Clear
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$configFile = Join-Path $root "dll-path.txt"

function Fail($message) {
    Write-Host ""
    Write-Host "[失败] $message" -ForegroundColor Red
    exit 1
}

if ($Clear) {
    if (Test-Path -LiteralPath $configFile) {
        Remove-Item -LiteralPath $configFile -Force
        Write-Host "已清除保存的 DLL 路径。" -ForegroundColor Yellow
    } else {
        Write-Host "没有保存过 DLL 路径，无需清除。" -ForegroundColor Yellow
    }
    exit 0
}

if (-not $Path) {
    Write-Host "把你的 CSGOFeel.dll 拖进这个窗口，然后回车：" -ForegroundColor Cyan
    $Path = Read-Host "DLL 路径"
}

# 拖拽进 PowerShell 的路径通常带引号，去掉
$Path = $Path.Trim().Trim('"').Trim()
if (-not $Path) { Fail "没有输入路径。" }

try {
    $full = [System.IO.Path]::GetFullPath($Path)
} catch {
    Fail "路径无效：$Path"
}

if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
    Fail "文件不存在：$full"
}

$name = [System.IO.Path]::GetFileName($full)
if ($name -ne 'CSGOFeel.dll') {
    Fail "文件名必须是 CSGOFeel.dll，你选的是 $name"
}

Set-Content -LiteralPath $configFile -Value $full -Encoding UTF8

$info = Get-Item -LiteralPath $full
Write-Host ""
Write-Host "已保存：" -ForegroundColor Green
Write-Host "  $full" -ForegroundColor Green
Write-Host "  $([math]::Round($info.Length / 1KB, 1)) KB，修改于 $($info.LastWriteTime)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "现在双击 install-server.bat 就会用这个 DLL。" -ForegroundColor Cyan
exit 0
