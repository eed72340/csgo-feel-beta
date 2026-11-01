#requires -Version 5.1
<#
    把编译好的服务端插件复制到 CS2 的 CounterStrikeSharp 插件目录。

    用法：
      .\deploy-server.ps1                          # 自动探测 Steam 库
      .\deploy-server.ps1 -Cs2Path "D:\...\Counter-Strike Global Offensive"
      .\deploy-server.ps1 -Undeploy

    部署后重启服务器，聊天框输入 !csgo 即可。
#>
param(
    [string]$Cs2Path = "",
    [string]$Dll = "",
    [switch]$Undeploy
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not $Dll) { $Dll = Join-Path $root "dist\server\CSGOFeel.dll" }

function Find-Cs2Path {
    # 1) 注册表里的 Steam 安装目录
    $steam = $null
    foreach ($key in @(
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam',
        'HKCU:\SOFTWARE\Valve\Steam'
    )) {
        if (Test-Path $key) {
            $steam = (Get-ItemProperty $key -ErrorAction SilentlyContinue).InstallPath
            if ($steam) { break }
        }
    }

    $candidates = @()
    if ($steam) {
        $candidates += (Join-Path $steam "steamapps\common\Counter-Strike Global Offensive")
        # libraryfolders.vdf 里的额外库
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            Select-String -Path $vdf -Pattern '"path"\s+"(.+)"' -AllMatches | ForEach-Object {
                foreach ($m in $_.Matches) {
                    $lib = $m.Groups[1].Value -replace '\\\\', '\'
                    $candidates += (Join-Path $lib "steamapps\common\Counter-Strike Global Offensive")
                }
            }
        }
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Select-Object -ExpandProperty Root)) {
        $candidates += (Join-Path $drive "SteamLibrary\steamapps\common\Counter-Strike Global Offensive")
        $candidates += (Join-Path $drive "Steam\steamapps\common\Counter-Strike Global Offensive")
    }

    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c "game\csgo"))) { return $c }
    }
    return $null
}

if (-not $Cs2Path) {
    $Cs2Path = Find-Cs2Path
    if (-not $Cs2Path) {
        throw "没有找到 CS2 安装目录，请用 -Cs2Path 指定，例如：.\deploy-server.ps1 -Cs2Path `"D:\Steam\steamapps\common\Counter-Strike Global Offensive`""
    }
}

$pluginRoot = Join-Path $Cs2Path "game\csgo\addons\counterstrikesharp\plugins"
if (-not (Test-Path $pluginRoot)) {
    throw "找不到 CounterStrikeSharp 插件目录：$pluginRoot（先安装 Metamod + CounterStrikeSharp）"
}

$target = Join-Path $pluginRoot "CSGOFeel"

if ($Undeploy) {
    if (Test-Path $target) {
        Remove-Item $target -Recurse -Force
        Write-Host "已移除 $target" -ForegroundColor Yellow
    } else {
        Write-Host "插件未部署，无需移除。" -ForegroundColor Yellow
    }
    return
}

if (-not (Test-Path $Dll)) {
    throw "找不到插件 DLL：$Dll（先运行 build.ps1）"
}

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item $Dll $target -Force

Write-Host "已部署到：$target" -ForegroundColor Green
Get-ChildItem $target | Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } } | Format-Table -AutoSize
Write-Host "重启服务器后在聊天框输入：!csgo  /  !preset csgo  /  !shangyang 1" -ForegroundColor Green
