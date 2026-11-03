#requires -Version 5.1
<#
    csgo-feel 0.2.0 一键安装（无编译依赖版）

    设计前提：目标机器上可能**没有任何编译工具**（没有 Visual Studio、没有 .NET SDK、没有 git）。
    本脚本只用 Windows 自带的 PowerShell 5.1 实现：装文件、探路径、算哈希，不 build 任何东西。

    它做的事：
      1. 在**脚本自己所在的目录**里找双端 DLL（server\CSGOFeel.dll、client\CSGOFeelClient.dll），
         找不到再往上一级看一层；不会去别处乱翻。
      2. 自动定位 CS2 根目录：-Cs2Path 参数 → Steam 注册表 → libraryfolders.vdf 里的每个库。
      3. 部署到 CS2 根目录：
           game\csgo\addons\counterstrikesharp\plugins\CSGOFeel\CSGOFeel.dll
           csgo-feel\CSGOFeelClient.dll + csgo_feel.ini
         覆盖前生成 .bak_<时间戳> 备份，覆盖后逐文件比对 SHA256。
      4. 若拿得到本机 SteamID（-LocalSteamId 或 Steam 的 loginusers.vdf），写入用户级
         CSGO_FEEL_LOCAL_STEAMID，否则客户端拿不到遥测、不会晃。

    用法：
      双击 install.bat（推荐，等价于下面这条）
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Cs2Path "D:\Steam\steamapps\common\Counter-Strike Global Offensive"
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -DryRun
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -SkipServer     # 只装客户端
      powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -SkipClient     # 只装服务端
#>
param(
    [string]$Cs2Path = "",
    [string]$SourceDir = "",
    [string]$LocalSteamId = "",
    [switch]$SkipClient,
    [switch]$SkipServer,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------- 输出
function Step($t) { Write-Host ""; Write-Host "==> $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "    $t" -ForegroundColor Green }
function Note($t) { Write-Host "    $t" -ForegroundColor DarkGray }
function Warn($t) { Write-Host "    $t" -ForegroundColor Yellow }
function Fail($t) { Write-Host "    $t" -ForegroundColor Red }

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$src = $PSScriptRoot
if ($SourceDir) { $src = (Resolve-Path -LiteralPath $SourceDir).Path }

Write-Host ""
Write-Host "  csgo-feel 0.2.0  一键安装" -ForegroundColor White
Write-Host "  脚本目录：$src" -ForegroundColor DarkGray

# ---------------------------------------------------------------- 找产物
# 只认「脚本目录 → 上一级」，不扫全盘；包解开后把脚本放在 dist 同级即可。
function Find-Artifact {
    param(
        [string[]]$Candidates,   # 相对脚本目录的优先路径
        [string]$FileName,
        [string]$Root
    )

    foreach ($rel in $Candidates) {
        $p = Join-Path $Root $rel
        if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
    }

    # 兜底：同目录树里最多找三层（打包方式不同时，DLL 可能躺在子目录里）
    $hit = Get-ChildItem -LiteralPath $Root -Recurse -Depth 3 -File -Filter $FileName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(\.git|node_modules|obj|bin)\\' } |
        Sort-Object { $_.FullName.Length } |
        Select-Object -First 1
    if ($hit) { return $hit.FullName }

    $parent = Split-Path -Parent $Root
    if ($parent -and $parent -ne $Root) {
        foreach ($rel in $Candidates) {
            $p = Join-Path $parent $rel
            if (Test-Path -LiteralPath $p) { return (Resolve-Path -LiteralPath $p).Path }
        }
    }
    return $null
}

Step "查找构建产物"

$serverDll = $null
if (-not $SkipServer) {
    $serverDll = Find-Artifact -Root $src -FileName "CSGOFeel.dll" -Candidates @(
        "server\CSGOFeel.dll",
        "CSGOFeel.dll",
        "dist\server\CSGOFeel.dll",
        "server\bin\Release\net10.0\CSGOFeel.dll"
    )
    if (-not $serverDll) {
        Fail "没有找到 CSGOFeel.dll。把本脚本和 server\、client\ 放在同一目录（发行包解开就是这样）。"
        throw "缺少服务端产物"
    }
    Ok "服务端：$serverDll"
}

$clientDll = $null
$clientIni = $null
if (-not $SkipClient) {
    $clientDll = Find-Artifact -Root $src -FileName "CSGOFeelClient.dll" -Candidates @(
        "client\CSGOFeelClient.dll",
        "CSGOFeelClient.dll",
        "dist\client\CSGOFeelClient.dll",
        "client\build\CSGOFeelClient.dll"
    )
    if (-not $clientDll) {
        Fail "没有找到 CSGOFeelClient.dll。"
        throw "缺少客户端产物"
    }
    Ok "客户端：$clientDll"

    $clientIni = Find-Artifact -Root $src -FileName "csgo_feel.ini" -Candidates @(
        "client\csgo_feel.ini",
        "csgo_feel.ini",
        "dist\client\csgo_feel.ini"
    )
    if ($clientIni) { Ok "配置  ：$clientIni" }
    else {
        Warn "没有找到 csgo_feel.ini，将沿用游戏目录里已有的配置（首次安装则使用客户端内置默认值）。"
    }
}

# ---------------------------------------------------------------- 找 CS2
function Get-SteamRoots {
    $found = New-Object System.Collections.ArrayList
    foreach ($key in @(
            "HKCU:\Software\Valve\Steam",
            "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
            "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $props = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in @("SteamPath", "InstallPath")) {
                $value = $props.$name
                if ($value) {
                    $normalized = $value -replace '/', '\'
                    if (Test-Path -LiteralPath $normalized) {
                        if (-not $found.Contains($normalized)) { [void]$found.Add($normalized) }
                    }
                }
            }
        } catch { }
    }
    return $found
}

function Get-LibraryFolders {
    param([string]$SteamRoot)
    $vdf = Join-Path $SteamRoot "steamapps\libraryfolders.vdf"
    if (-not (Test-Path -LiteralPath $vdf)) { return @() }
    $text = Get-Content -LiteralPath $vdf -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return @() }
    $paths = [regex]::Matches($text, '"path"\s+"([^"]+)"') | ForEach-Object {
        $_.Groups[1].Value -replace '\\\\', '\'
    }
    return @($paths)
}

function Find-Cs2Root {
    param([string]$Explicit)

    if ($Explicit) {
        $p = $Explicit
        if (Test-Path -LiteralPath $p) {
            $p = (Resolve-Path -LiteralPath $p).Path
            if (Test-Path -LiteralPath (Join-Path $p "game\bin\win64\cs2.exe")) { return $p }
        }
        return $null
    }

    $steamRoots = Get-SteamRoots
    $libraries = New-Object System.Collections.ArrayList
    foreach ($root in $steamRoots) {
        if (-not $libraries.Contains($root)) { [void]$libraries.Add($root) }
        foreach ($lib in (Get-LibraryFolders -SteamRoot $root)) {
            if ($lib -and -not $libraries.Contains($lib)) { [void]$libraries.Add($lib) }
        }
    }

    foreach ($lib in $libraries) {
        $candidate = Join-Path $lib "steamapps\common\Counter-Strike Global Offensive"
        if (Test-Path -LiteralPath (Join-Path $candidate "game\bin\win64\cs2.exe")) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

Step "定位 CS2 根目录"
$cs2 = Find-Cs2Root -Explicit $Cs2Path
if (-not $cs2) {
    Fail "没能自动找到 CS2。请用 -Cs2Path 指定，例如："
    Fail '  powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -Cs2Path "D:\Steam\steamapps\common\Counter-Strike Global Offensive"'
    throw "找不到 CS2 根目录"
}
Ok "CS2：$cs2"

$pluginsRoot = Join-Path $cs2 "game\csgo\addons\counterstrikesharp\plugins"
$feelDir     = Join-Path $pluginsRoot "CSGOFeel"
$clientDir   = Join-Path $cs2 "csgo-feel"

$cs2Running = @(Get-Process -Name "cs2" -ErrorAction SilentlyContinue)
if ($cs2Running.Count -gt 0) {
    Warn "CS2 正在运行（pid $($cs2Running.Id -join ', ')）：客户端 DLL 会被占用，建议先完全退出游戏再装。"
}

# ---------------------------------------------------------------- 备份
function Backup([string]$path) {
    if ($DryRun) { return }
    if (-not (Test-Path -LiteralPath $path)) { return }
    $copy = "$path.bak_$stamp"
    Copy-Item -LiteralPath $path -Destination $copy -Force
    Note "备份 -> $([System.IO.Path]::GetFileName($copy))"
}

function Install-File {
    param([string]$Source, [string]$Target, [string]$Label)

    if ($DryRun) {
        Write-Host ("    [dry-run] {0,-22} -> {1}" -f $Label, $Target) -ForegroundColor DarkGray
        return
    }
    $dir = Split-Path -Parent $Target
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    # 内容一致就不动它，免得反复双击装出几十个 .bak
    if (Test-Path -LiteralPath $Target) {
        $srcHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
        $dstHash = (Get-FileHash -LiteralPath $Target -Algorithm SHA256).Hash
        if ($srcHash -eq $dstHash) {
            Ok ("{0,-22} 已是最新，跳过" -f $Label)
            return
        }
    }
    Backup $Target
    try {
        Copy-Item -LiteralPath $Source -Destination $Target -Force
    } catch {
        Fail "写入失败：$Target"
        Fail "可能没有权限。关掉游戏，或右键 install.bat「以管理员身份运行」。"
        throw
    }
    Ok ("{0,-22} -> {1}" -f $Label, $Target)
}

# ---------------------------------------------------------------- 服务端
if ($SkipServer) {
    Note "按参数跳过服务端插件。"
} elseif (-not (Test-Path -LiteralPath $pluginsRoot)) {
    Warn "没找到 CounterStrikeSharp：$pluginsRoot"
    Warn "说明这台机器还没装服务端插件环境（Metamod + CounterStrikeSharp）。跳过服务端，只装客户端。"
} else {
    Step "部署服务端插件"
    Install-File -Source $serverDll -Target (Join-Path $feelDir "CSGOFeel.dll") -Label "CSGOFeel.dll"

    # players.json 首次安装时不存在，插件自己生成；存在才对齐，不动别的字段。
    $prefs = Join-Path $feelDir "players.json"
    if (-not $DryRun -and (Test-Path -LiteralPath $prefs)) {
        try {
            $data = Get-Content -LiteralPath $prefs -Raw -Encoding UTF8 | ConvertFrom-Json
            $needWrite = $false
            foreach ($prop in $data.PSObject.Properties) {
                $entry = $prop.Value
                if ($null -eq $entry) { continue }
                $names = @($entry.PSObject.Properties.Name)

                if ($names -contains "Lat" -and $null -ne $entry.Lat -and [double]$entry.Lat -ne 0.33) { $entry.Lat = 0.33; $needWrite = $true }
                if ($names -contains "Vert" -and $null -ne $entry.Vert -and [double]$entry.Vert -ne 0.14) { $entry.Vert = 0.14; $needWrite = $true }
                if ($names -contains "Qieqiang" -and $entry.Qieqiang -ne $true) { $entry.Qieqiang = $true; $needWrite = $true }
            }
            if ($needWrite) {
                Backup $prefs
                [System.IO.File]::WriteAllText($prefs, ($data | ConvertTo-Json -Depth 6),
                    (New-Object System.Text.UTF8Encoding($false)))
                Ok "players.json：Lat 0.33 / Vert 0.14 / 切枪动画 on"
            } else {
                Ok "players.json 已对齐，跳过"
            }
        } catch {
            Warn "players.json 解析失败，保持原样：$($_.Exception.Message)"
        }
    }
}

# ---------------------------------------------------------------- 客户端
if ($SkipClient) {
    Note "按参数跳过客户端视觉模组。"
} else {
    Step "部署客户端视觉模组"
    Install-File -Source $clientDll -Target (Join-Path $clientDir "CSGOFeelClient.dll") -Label "CSGOFeelClient.dll"
    if ($clientIni) {
        Install-File -Source $clientIni -Target (Join-Path $clientDir "csgo_feel.ini") -Label "csgo_feel.ini"
    }
    Note "注入器不随包分发（仓库只给源码与编译脚本），这一步只放文件。"
}

# ---------------------------------------------------------------- SteamID
function Get-SteamIdFromLoginUsers {
    param([string]$SteamRoot)
    $vdf = Join-Path $SteamRoot "config\loginusers.vdf"
    if (-not (Test-Path -LiteralPath $vdf)) { return $null }
    $text = Get-Content -LiteralPath $vdf -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return $null }

    # 新版 Steam 不一定写 MostRecent，AutoLogin 更常见；两个都认，MostRecent 优先。
    $autoLogin = $null
    foreach ($m in [regex]::Matches($text, '"(\d{17})"')) {
        $id = $m.Groups[1].Value
        $tail = $text.Substring($m.Index, [Math]::Min(700, $text.Length - $m.Index))
        if ($tail -match '"MostRecent"\s+"1"') { return $id }
        if (-not $autoLogin -and $tail -match '"AutoLogin"\s+"1"') { $autoLogin = $id }
    }
    return $autoLogin
}

Step "本机玩家 SteamID（客户端遥测的开关）"
$steamId = $LocalSteamId
if (-not $steamId) {
    foreach ($root in (Get-SteamRoots)) {
        $steamId = Get-SteamIdFromLoginUsers -SteamRoot $root
        if ($steamId) { Note "从 Steam 最近登录账号读到：$steamId"; break }
    }
}
if ($steamId) {
    if ($DryRun) {
        Note "[dry-run] 将写入用户级环境变量 CSGO_FEEL_LOCAL_STEAMID = $steamId"
    } else {
        $existing = [Environment]::GetEnvironmentVariable("CSGO_FEEL_LOCAL_STEAMID", "User")
        if ($existing -eq $steamId) {
            Ok "CSGO_FEEL_LOCAL_STEAMID 已是 $steamId"
        } else {
            [Environment]::SetEnvironmentVariable("CSGO_FEEL_LOCAL_STEAMID", $steamId, "User")
            Ok "已写入用户级环境变量 CSGO_FEEL_LOCAL_STEAMID = $steamId"
        }
    }
} else {
    Warn "没读到 SteamID。客户端拿不到服务端遥测就不会晃动。"
    Warn '补一次：install.ps1 -LocalSteamId 7656119xxxxxxxxxx'
}

# ---------------------------------------------------------------- 校验
Step "校验"
$expected = @()
if ($serverDll -and -not $SkipServer -and (Test-Path -LiteralPath $pluginsRoot)) {
    $expected += @{ target = (Join-Path $feelDir "CSGOFeel.dll"); source = $serverDll; name = "服务端插件" }
}
if ($clientDll -and -not $SkipClient) {
    $expected += @{ target = (Join-Path $clientDir "CSGOFeelClient.dll"); source = $clientDll; name = "客户端 DLL" }
}
if ($clientIni -and -not $SkipClient) {
    $expected += @{ target = (Join-Path $clientDir "csgo_feel.ini"); source = $clientIni; name = "客户端配置" }
}

$bad = 0
foreach ($item in $expected) {
    if ($DryRun) {
        Write-Host ("    [dry-run] 待写入 {0}" -f $item.target) -ForegroundColor DarkGray
        continue
    }
    if (-not (Test-Path -LiteralPath $item.target)) {
        Fail "缺少 $($item.target)"; $bad++; continue
    }
    $h1 = (Get-FileHash -LiteralPath $item.target -Algorithm SHA256).Hash
    $h2 = (Get-FileHash -LiteralPath $item.source -Algorithm SHA256).Hash
    $kb = [math]::Round((Get-Item -LiteralPath $item.target).Length / 1KB, 1)
    if ($h1 -eq $h2) {
        Ok ("{0,-12} {1,7} KB  sha256 {2}" -f $item.name, $kb, $h1.Substring(0, 16))
    } else {
        Fail ("{0,-12} 哈希不一致（目标 {1} / 来源 {2}）" -f $item.name, $h1.Substring(0, 8), $h2.Substring(0, 8))
        $bad++
    }
}
if ($DryRun) { Ok "dry-run：没有写入任何文件" }
elseif ($bad -eq 0) { Ok "全部写入成功，哈希与来源一致" }
else { throw "有 $bad 个文件校验失败" }

# ---------------------------------------------------------------- 收尾
Write-Host ""
if ($DryRun) {
    Write-Host "预览结束（-DryRun 未改动任何文件）。" -ForegroundColor Yellow
    return
}

Write-Host "装好了。接下来：" -ForegroundColor Green
Write-Host "  1. 完全退出 CS2（若刚才在跑）。" -ForegroundColor Green
Write-Host "  2. Steam → CS2 → 属性 → 启动选项加 -insecure（只连自建服务器）。" -ForegroundColor Green
Write-Host "  3. 启动服务器与游戏；服务端控制台敲 css_csgo 看配置。" -ForegroundColor Green
Write-Host "  4. 客户端还需要注入：注入器不随包分发，用仓库的 build-injector.ps1 自己编一个，" -ForegroundColor Green
Write-Host "     然后 csgo-feel-injector.exe cs2 CSGOFeelClient.dll（或仓库的 client\run.ps1）。" -ForegroundColor Green
Write-Host "  5. 进游戏按 F8 可切换本地视觉；控制台 !csgo 看/改服务端配置。" -ForegroundColor Green
