#requires -Version 5.1
<#
    诊断：读取服务端插件与客户端 DLL 之间的共享内存 Local\csgo_feel_v2。

    用法：
      .\state-probe.ps1              读一次
      .\state-probe.ps1 -Watch       每 0.5 秒刷新，盯着 switchSeq / 心跳看

    典型判断：
      "服务端不在线"           -> 插件没加载，或还没人 spawn（没发布过偏好）
      "客户端心跳 从未上报"    -> 客户端 DLL 没注入
      wall <= 5000ms           -> 两边都在线
    换枪时 switchSeq 会 +1；客户端心跳每 1 秒刷新。
#>
param(
    [switch]$Watch
)

$ErrorActionPreference = "Stop"

# PowerShell 5.1 跑在 .NET Framework 上，没有 [Environment]::TickCount64，
# 而服务端写的是 64 位毫秒数，用 [Environment]::TickCount（32 位、24.8 天就溢出）对不上，只能 P/Invoke。
if (-not ('Win32.K32' -as [type])) {
    Add-Type -Namespace Win32 -Name K32 -MemberDefinition @'
[DllImport("kernel32.dll")]
public static extern ulong GetTickCount64();
'@
}

# 布局必须和 client/src/state.h、server/FeelLink.cs 一致
$Offsets = [ordered]@{
    magic       = @(0, 'u32')
    version     = @(4, 'u32')
    enabled     = @(8, 'i32')
    qieqiang    = @(12, 'i32')
    amount      = @(16, 'f32')
    time        = @(20, 'f32')
    bob         = @(24, 'i32')
    switchSeq   = @(28, 'i32')
    serverTick  = @(32, 'u64')
    serverPid   = @(40, 'u32')
    serverBuild = @(44, 'u32')
    clientTick  = @(48, 'u64')
    clientPid   = @(56, 'u32')
    clientFlags = @(60, 'u32')
    reqSeq      = @(64, 'u32')
    reqCmd      = @(68, 'u32')
    reqAck      = @(72, 'u32')
}
$MapSize = 80

function Read-Block {
    try {
        $map = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('Local\csgo_feel_v2')
    }
    catch {
        return $null
    }

    try {
        # 旧版插件建的是 64 字节映射，按 80 开视图会失败，退回去再读
        $size = $MapSize
        try {
            $view = $map.CreateViewAccessor(0, $size, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
        }
        catch {
            $size = 64
            $view = $map.CreateViewAccessor(0, $size, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
        }

        $buf = New-Object byte[] $MapSize   # 补齐到 80，缺的部分按 0 处理
        $view.ReadArray(0, $buf, 0, $size) | Out-Null
        $view.Dispose()
        return $buf
    }
    finally {
        $map.Dispose()
    }
}

function Get-Field {
    param([byte[]]$Buf, [int]$Offset, [string]$Kind)
    $width = switch ($Kind) { 'u64' { 8 } default { 4 } }
    if ($Offset + $width -gt $Buf.Length) { return 0 }

    switch ($Kind) {
        'u32' { return [BitConverter]::ToUInt32($Buf, $Offset) }
        'i32' { return [BitConverter]::ToInt32($Buf, $Offset) }
        'f32' { return [BitConverter]::ToSingle($Buf, $Offset) }
        'u64' { return [BitConverter]::ToUInt64($Buf, $Offset) }
    }
}

function Show-State {
    $buf = Read-Block
    if ($null -eq $buf) {
        Write-Host "共享内存 Local\csgo_feel_v2 不存在。" -ForegroundColor Yellow
        Write-Host "说明服务端插件没加载过，也没人注入过客户端 DLL。" -ForegroundColor DarkGray
        return
    }

    $v = @{}
    foreach ($k in $Offsets.Keys) {
        $o = $Offsets[$k]
        $v[$k] = Get-Field -Buf $buf -Offset $o[0] -Kind $o[1]
    }

    $now = [Win32.K32]::GetTickCount64()
    $serverAge = if ($v.serverTick -gt 0) { $now - $v.serverTick } else { -1 }
    $clientAge = if ($v.clientTick -gt 0) { $now - $v.clientTick } else { -1 }
    $magicOk = ($v.magic -eq 0x4C454546)
    $layoutOk = ($v.version -eq 3)

    Write-Host ("magic        0x{0:X8} {1}" -f $v.magic, $(if ($magicOk) { "'FEEL' 正常" } else { "无效" })) `
        -ForegroundColor $(if ($magicOk) { 'Green' } else { 'Red' })
    if ($layoutOk) {
        Write-Host ("version      {0}    build {1}" -f $v.version, $v.serverBuild)
    }
    else {
        Write-Host ("version      {0}（本脚本按 v3/80 字节解析，旧版布局下面的字段可能对不上）" -f $v.version) -ForegroundColor Yellow
    }

    if ($serverAge -ge 0 -and $serverAge -le 15000) {
        Write-Host ("服务端       在线（{0}ms 前心跳，pid {1}）" -f $serverAge, $v.serverPid) -ForegroundColor Green
    }
    elseif ($serverAge -ge 0) {
        Write-Host ("服务端       心跳已过期（{0}ms），客户端会退回 ini" -f $serverAge) -ForegroundColor Yellow
    }
    else {
        Write-Host "服务端       不在线（还没发布过偏好）" -ForegroundColor Yellow
    }

    if ($clientAge -ge 0 -and $clientAge -le 5000) {
        Write-Host ("客户端       在线（{0}ms 前心跳，pid {1}）" -f $clientAge, $v.clientPid) -ForegroundColor Green
    }
    elseif ($clientAge -ge 0) {
        Write-Host ("客户端       心跳过期（{0}ms）—— DLL 可能已卸载" -f $clientAge) -ForegroundColor Yellow
    }
    else {
        Write-Host "客户端       从未上报心跳——DLL 没有注入" -ForegroundColor Red
    }

    $f = $v.clientFlags
    $bits = @()
    if ($f -band 1) { $bits += '滚轮/侧键已接管' }
    if ($f -band 2) { $bits += '按键识别' }
    if ($f -band 4) { $bits += '共享内存' }
    if ($bits.Count -eq 0) { $bits = @('无') }
    Write-Host ("能力位       0x{0:X} -> {1}" -f $f, ($bits -join ' / '))

    Write-Host ""
    Write-Host ("总开关       {0}" -f $(if ($v.enabled) { '开' } else { '关' }))
    Write-Host ("切枪动画     {0}   下沉 {1:0.00}   回弹 {2:0.00}s" -f `
        $(if ($v.qieqiang) { '开' } else { '关' }), $v.amount, $v.time)
    Write-Host ("switchSeq    {0}" -f $v.switchSeq)
    if ($v.reqSeq -ne 0) {
        $cmdName = switch ($v.reqCmd) { 1 { '切换切枪动画' } 2 { '切换总开关' } default { "未知($($v.reqCmd))" } }
        $ack = if ($v.reqAck -eq $v.reqSeq) { '服务端已处理' } else { '等待服务端处理' }
        Write-Host ("按键请求     seq {0} -> {1}（{2}）" -f $v.reqSeq, $cmdName, $ack)
    }
    else {
        Write-Host "按键请求     暂无（客户端没按过 F8，或 DLL 未注入）"
    }
}

if ($Watch) {
    while ($true) {
        Clear-Host
        Write-Host "== csgo-feel 状态 ($(Get-Date -Format 'HH:mm:ss'))  Ctrl+C 退出 ==" -ForegroundColor Cyan
        Write-Host ""
        Show-State
    }
}
else {
    Show-State
}
