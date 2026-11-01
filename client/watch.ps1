#requires -Version 5.1
<#
    进程出现后尝试加载一次（仅用于 -insecure / 离线自建环境）。
    在线模式下 CS2 会标记为受保护进程，加载会失败——本项目不做任何绕过，
    只在检测到游戏进程加载 client.dll 后尝试一次，失败即退出。

    用法（在仓库根目录执行）：
      powershell -ExecutionPolicy Bypass -File .\client\watch.ps1
      # 然后立刻启动 CS2；脚本会每 200ms 探测一次，加载成功后退出

    参数：
      -Timeout 180     最多等待多少秒
      -Dll            要注入的 DLL（默认 build\CSGOFeelClient.dll）
      -Tag             用带 Tag 的 DLL，例如 -Tag diag2
#>
param(
    [int]$Timeout = 180,
    [string]$Dll = "",
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not $Dll) {
    $Dll = if ($Tag) { Join-Path $root "build\CSGOFeelClient_$Tag.dll" } else { Join-Path $root "build\CSGOFeelClient.dll" }
}
if (-not (Test-Path $Dll)) { throw "找不到 DLL：$Dll" }

$injector = Join-Path $root "..\tools\injector\bin\Release\net10.0-windows\csgo-feel-injector.exe"
if (-not (Test-Path $injector)) { throw "找不到注入器：$injector（先运行 build.ps1）" }

$logDir = Join-Path $root "build"
Get-ChildItem $logDir -Filter "csgo_feel_client*.log" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "等待 CS2 启动（最多 $Timeout 秒）…" -ForegroundColor Cyan
Write-Host "现在去启动 CS2 即可，注入会在进程出现后的第一次轮询完成。" -ForegroundColor Cyan

$deadline = (Get-Date).AddSeconds($Timeout)
$attempts = 0

while ((Get-Date) -lt $deadline) {
    foreach ($proc in (Get-Process cs2 -ErrorAction SilentlyContinue)) {
        $injected = $false
        try {
            $injected = $null -ne ($proc.Modules | Where-Object { $_.ModuleName -like "CSGOFeelClient*" })
        }
        catch {
            continue
        }

        if ($injected) { continue }

        # 只注入真正加载了 client.dll 的进程（Steam 启动器也是 cs2.exe，但没有 client.dll）
        $hasClient = $false
        try {
            $hasClient = $null -ne ($proc.Modules | Where-Object { $_.ModuleName -ieq "client.dll" })
        }
        catch { continue }
        if (-not $hasClient) { continue }

        $attempts++
        Write-Host "`n发现 cs2 pid=$($proc.Id)，尝试注入…" -ForegroundColor Yellow
        & $injector "cs2:$($proc.Id)" $Dll

        if ($LASTEXITCODE -eq 0) {
            Start-Sleep -Seconds 3
            Write-Host ""
            Write-Host "---- csgo_feel_client_$($proc.Id).log ----" -ForegroundColor Cyan
            $log = Join-Path $logDir "csgo_feel_client_$($proc.Id).log"
            if (Test-Path $log) {
                Get-Content $log
            } else {
                Get-ChildItem $logDir -Filter "csgo_feel_client*.log" -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1 |
                    ForEach-Object { Get-Content $_.FullName }
            }
            exit 0
        }
    }

    Start-Sleep -Milliseconds 200
}

Write-Host "超时：$Timeout 秒内没有找到可注入的 cs2 进程。" -ForegroundColor Red
exit 1
