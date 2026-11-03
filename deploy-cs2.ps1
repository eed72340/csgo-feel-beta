#requires -Version 5.1
<#
    把重建版 csgo-feel 部署进 CS2 根目录，并让补丁真正生效。

    部署内容：
      game\csgo\addons\counterstrikesharp\plugins\CSGOFeel\CSGOFeel.dll   服务端插件（配置 + 本机遥测）
      game\csgo\addons\counterstrikesharp\plugins\CSGOFeel.Movement\...   独立移动补丁
      csgo-feel\CSGOFeelClient.dll / csgo_feel.ini / 注入器               客户端视觉模组

    同时对齐：
      plugins\CSGOFeel\players.json   改成 legacy 实测值 lat 0.33 / vert 0.14
      game\csgo\cfg\autoexec.cfg      旧的 0.40 / 0.25 改成 0.33 / 0.14
      用户环境变量（-SetEnvironment） CSGO_FEEL_LOCAL_STEAMID / CSGO_FEEL_MOVEMENT_LAB=1

    幂等：重复执行只覆盖产物；每次覆盖前生成带时间戳的 .bak_* 备份。

    用法：
      .\deploy-cs2.ps1
      .\deploy-cs2.ps1 -LocalSteamId 76561198745916617 -SetEnvironment
      .\deploy-cs2.ps1 -SkipClient
      .\deploy-cs2.ps1 -Undeploy
#>
param(
    [string]$Cs2Path = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive",
    [string]$ReleaseDir = "D:\code\csgo-feel-releases",
    [string]$LocalSteamId = "",
    [switch]$SetEnvironment,
    [switch]$SkipClient,
    [switch]$Undeploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$rel = $ReleaseDir
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"

function Step($text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }
function Ok($text) { Write-Host "    $text" -ForegroundColor Green }
function Note($text) { Write-Host "    $text" -ForegroundColor DarkGray }

function Backup([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $target = "$path.bak_$stamp"
    Copy-Item -LiteralPath $path -Destination $target -Force
    Note "备份 $([System.IO.Path]::GetFileName($path)) -> $([System.IO.Path]::GetFileName($target))"
}

$cs2 = (Resolve-Path -LiteralPath $Cs2Path).Path
if (-not (Test-Path (Join-Path $cs2 "game\bin\win64\cs2.exe"))) {
    throw "不像是 CS2 根目录（缺少 game\bin\win64\cs2.exe）：$cs2"
}

$cssRoot = Join-Path $cs2 "game\csgo\addons\counterstrikesharp"
$plugins = Join-Path $cssRoot "plugins"
$clientDir = Join-Path $cs2 "csgo-feel"
$autoexec = Join-Path $cs2 "game\csgo\cfg\autoexec.cfg"

if ($Undeploy) {
    Step "卸载移动补丁"
    $mvDir = Join-Path $plugins "CSGOFeel.Movement"
    if (Test-Path $mvDir) {
        $gone = "$mvDir.removed_$stamp"
        Move-Item -LiteralPath $mvDir -Destination $gone
        Ok "已移出：$gone"
    } else {
        Ok "移动补丁未部署。"
    }
    Note "服务端插件与客户端文件保持原样；需要时用同目录的 .bak_$stamp 备份还原。"
    return
}

# ---------------------------------------------------------------- 前置检查
Step "检查产物与目标目录"
$serverDll = Join-Path $rel "server\CSGOFeel.dll"
$moveDll = Join-Path $rel "movement\CSGOFeel.Movement.dll"
$clientDll = Join-Path $rel "client\CSGOFeelClient.dll"
$clientIni = Join-Path $rel "client\csgo_feel.ini"
# 注入器不随包分发：只有自己 build-injector.ps1 编过才有，缺了也不影响部署。
$injectorDirs = @(
    (Join-Path $rel "tools\injector"),
    (Join-Path $root "build\injector")
) | Where-Object { Test-Path -LiteralPath $_ }

$required = @($serverDll)
if (-not $SkipClient) { $required += @($clientDll, $clientIni) }
foreach ($f in $required) {
    if (-not (Test-Path -LiteralPath $f)) { throw "找不到构建产物：$f（先运行 build.ps1）" }
}
if (-not (Test-Path $plugins)) { throw "找不到 CounterStrikeSharp 插件目录：$plugins" }
Ok "CS2: $cs2"
Ok "产物目录: $rel"
Ok "产物齐全"

# ---------------------------------------------------------------- 服务端插件
Step "部署服务端插件（配置 + 本机遥测）"
$feelDir = Join-Path $plugins "CSGOFeel"
New-Item -ItemType Directory -Force $feelDir | Out-Null
$feelTarget = Join-Path $feelDir "CSGOFeel.dll"
Backup $feelTarget
Copy-Item -LiteralPath $serverDll -Destination $feelTarget -Force
Ok "CSGOFeel.dll -> $feelDir"

if (Test-Path -LiteralPath $moveDll) {
    Step "部署独立移动补丁"
    $mvDir = Join-Path $plugins "CSGOFeel.Movement"
    New-Item -ItemType Directory -Force $mvDir | Out-Null
    Backup (Join-Path $mvDir "CSGOFeel.Movement.dll")
    Copy-Item -LiteralPath $moveDll -Destination (Join-Path $mvDir "CSGOFeel.Movement.dll") -Force
    Ok "CSGOFeel.Movement.dll -> $mvDir"
}
else {
    Note "没有移动补丁产物（build.ps1 默认不构建），跳过。"
}

# ---------------------------------------------------------------- 客户端
if (-not $SkipClient) {
    Step "部署客户端视觉模组"
    New-Item -ItemType Directory -Force $clientDir | Out-Null
    Backup (Join-Path $clientDir "CSGOFeelClient.dll")
    Backup (Join-Path $clientDir "csgo_feel.ini")
    Copy-Item -LiteralPath $clientDll -Destination (Join-Path $clientDir "CSGOFeelClient.dll") -Force
    Copy-Item -LiteralPath $clientIni -Destination (Join-Path $clientDir "csgo_feel.ini") -Force
    $injectorDir = @($injectorDirs) | Select-Object -First 1
    if ($injectorDir) {
        foreach ($extra in Get-ChildItem -LiteralPath $injectorDir -File -Filter "csgo-feel-injector.*") {
            Copy-Item -LiteralPath $extra.FullName -Destination (Join-Path $clientDir $extra.Name) -Force
        }
        Note "注入器来自 $injectorDir（自己编译的，不分发）"
    }
    else {
        Note "没有注入器（不在分发包内）。需要时执行 build-injector.ps1 自行编译。"
    }
    Ok "客户端文件 -> $clientDir"
}

# ---------------------------------------------------------------- players.json
Step "对齐 players.json 的 legacy 实测值"
$prefsPath = Join-Path $feelDir "players.json"
Backup $prefsPath

$prefs = [ordered]@{}
if (Test-Path $prefsPath) {
    try {
        $raw = Get-Content -LiteralPath $prefsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($p in $raw.PSObject.Properties) { $prefs[$p.Name] = $p.Value }
    } catch {
        Note "旧 players.json 无法解析，改为按默认值重建：$($_.Exception.Message)"
    }
}
if ($LocalSteamId -and -not $prefs.Contains($LocalSteamId)) { $prefs[$LocalSteamId] = [ordered]@{} }

foreach ($id in @($prefs.Keys)) {
    $entry = $prefs[$id]
    # 数值来自 csgo legacy\csgo\cfg\default.cfg，不再使用旧项目的 0.40 / 0.25。
    $entry.Lat = 0.33
    $entry.Vert = 0.14
    if (-not $entry.PSObject.Properties.Name.Contains("Preset")) { $entry | Add-Member -NotePropertyName Preset -NotePropertyValue "csgo" }
    if (-not $entry.PSObject.Properties.Name.Contains("Enabled")) { $entry | Add-Member -NotePropertyName Enabled -NotePropertyValue $true }
    if (-not $entry.PSObject.Properties.Name.Contains("Bob")) { $entry | Add-Member -NotePropertyName Bob -NotePropertyValue $true }
    if (-not $entry.PSObject.Properties.Name.Contains("Lower")) { $entry | Add-Member -NotePropertyName Lower -NotePropertyValue 21 }
    if (-not $entry.PSObject.Properties.Name.Contains("Cycle")) { $entry | Add-Member -NotePropertyName Cycle -NotePropertyValue 0.98 }
    if (-not $entry.PSObject.Properties.Name.Contains("ShiftLeft")) { $entry | Add-Member -NotePropertyName ShiftLeft -NotePropertyValue 1.5 }
    if (-not $entry.PSObject.Properties.Name.Contains("ShiftRight")) { $entry | Add-Member -NotePropertyName ShiftRight -NotePropertyValue 0.75 }
    # 切枪动画重新接上：触发源是真实换枪事件（+ 窗口消息兜底），不再改写开火时间。
    $entry.Qieqiang = $true
    if (-not $entry.PSObject.Properties.Name.Contains("QieqiangAmount")) { $entry | Add-Member -NotePropertyName QieqiangAmount -NotePropertyValue 0.55 }
    if (-not $entry.PSObject.Properties.Name.Contains("QieqiangTime")) { $entry | Add-Member -NotePropertyName QieqiangTime -NotePropertyValue 0.35 }
    if (-not $entry.PSObject.Properties.Name.Contains("Shangyang")) { $entry | Add-Member -NotePropertyName Shangyang -NotePropertyValue $true }
    if (-not $entry.PSObject.Properties.Name.Contains("ViewmodelRecoil")) { $entry | Add-Member -NotePropertyName ViewmodelRecoil -NotePropertyValue $true }
    if (-not $entry.PSObject.Properties.Name.Contains("PunchDecay")) { $entry | Add-Member -NotePropertyName PunchDecay -NotePropertyValue 18 }
    if (-not $entry.PSObject.Properties.Name.Contains("Scale")) { $entry | Add-Member -NotePropertyName Scale -NotePropertyValue 1.0 }
}

$json = ($prefs | ConvertTo-Json -Depth 5)
[System.IO.File]::WriteAllText($prefsPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Ok "players.json 已写入 $($prefs.Count) 名玩家（Lat 0.33 / Vert 0.14 / 切枪动画 on）"

# ---------------------------------------------------------------- autoexec.cfg
Step "对齐 autoexec.cfg 中的晃动数值"
if (Test-Path -LiteralPath $autoexec) {
    Backup $autoexec
    $text = Get-Content -LiteralPath $autoexec -Raw -Encoding UTF8
    $before = $text
    $text = [regex]::Replace($text, '(?m)^(\s*cl_bobamt_lat\s+)"[^"]*"', '$1"0.33"')
    $text = [regex]::Replace($text, '(?m)^(\s*cl_bobamt_vert\s+)"[^"]*"', '$1"0.14"')
    if ($text -ne $before) {
        [System.IO.File]::WriteAllText($autoexec, $text, (New-Object System.Text.UTF8Encoding($false)))
        Ok "已更新 cl_bobamt_lat / cl_bobamt_vert"
    } else {
        Ok "数值已是 0.33 / 0.14，无需改动"
    }
    Note "注意：CS2 已删除这些 cl_bob* 指令，它们是无效行；真正让晃动生效的是客户端模组。"
} else {
    Note "没有 autoexec.cfg，跳过。"
}

# ---------------------------------------------------------------- 环境变量
Step "环境变量（补丁生效的开关）"
if ($SetEnvironment) {
    $steamId = $LocalSteamId
    if (-not $steamId) { throw "需要 -LocalSteamId（例如 76561198745916617）才能绑定本机玩家" }

    [Environment]::SetEnvironmentVariable("CSGO_FEEL_LOCAL_STEAMID", $steamId, "User")
    [Environment]::SetEnvironmentVariable("CSGO_FEEL_MOVEMENT_LAB", "1", "User")
    $env:CSGO_FEEL_LOCAL_STEAMID = $steamId
    $env:CSGO_FEEL_MOVEMENT_LAB = "1"
    Ok "CSGO_FEEL_LOCAL_STEAMID = $steamId（用户级）"
    Ok "CSGO_FEEL_MOVEMENT_LAB = 1（用户级）"
    Note "用户级变量对新启动的进程生效：先完全退出 CS2，再从 Steam 启动。"
} else {
    Note "未传 -SetEnvironment：本次不写环境变量。"
    Note "补丁在缺少这两个变量时保持关闭，不会改动任何速度。"
}

# ---------------------------------------------------------------- 校验
Step "校验部署结果"
$expected = @(
    @{ path = $feelTarget; name = "服务端插件"; src = $serverDll }
)
if (Test-Path -LiteralPath $moveDll) {
    $expected += @{ path = (Join-Path $plugins "CSGOFeel.Movement\CSGOFeel.Movement.dll"); name = "移动补丁"; src = $moveDll }
}
if (-not $SkipClient) {
    $expected += @{ path = (Join-Path $clientDir "CSGOFeelClient.dll"); name = "客户端 DLL"; src = $clientDll }
}

$fail = 0
foreach ($item in $expected) {
    if (-not (Test-Path -LiteralPath $item.path)) { Write-Host "    [fail] 缺少 $($item.path)" -ForegroundColor Red; $fail++; continue }
    $deployed = (Get-FileHash -LiteralPath $item.path -Algorithm SHA256).Hash
    $size = [math]::Round((Get-Item -LiteralPath $item.path).Length / 1KB, 1)
    Write-Host ("    {0,-12} {1,7} KB  sha256 {2}" -f $item.name, $size, $deployed.Substring(0, 16))

    if ($item.src) {
        $origin = (Get-FileHash -LiteralPath $item.src -Algorithm SHA256).Hash
        if ($origin -ne $deployed) {
            Write-Host ("    [fail] {0} 与产物目录不一致" -f $item.name) -ForegroundColor Red
            $fail++
        }
    }
}
if ($fail -eq 0) { Ok "已部署文件与 $rel 的产出一致" }

try {
    Get-Content -LiteralPath $prefsPath -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null
    Ok "players.json 是合法 JSON"
} catch {
    Write-Host "    [fail] players.json 不是合法 JSON：$($_.Exception.Message)" -ForegroundColor Red; $fail++
}

Write-Host ""
if ($fail -gt 0) { throw "校验失败 $fail 项" }
Write-Host "部署完成。下一步：" -ForegroundColor Green
Write-Host "  1. 完全退出 CS2（若在运行）。" -ForegroundColor Green
Write-Host "  2. Steam 启动项加 -insecure，不要连公网服务器。" -ForegroundColor Green
Write-Host "  3. 启动后进入本机地图，控制台 css_feelmove 查看补丁状态。" -ForegroundColor Green
Write-Host "  4. 客户端 DLL 需要注入：build-injector.ps1 编好注入器后执行 client\run.ps1。" -ForegroundColor Green
