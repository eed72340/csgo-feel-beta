#requires -Version 5.1
<#
    编译客户端 DLL（MSVC /MT x64）。
    用法：
      .\build.ps1                    -> build\CSGOFeelClient.dll
      .\build.ps1 -Tag diag2         -> build\CSGOFeelClient_diag2.dll（DLL 已注入游戏时用这个，避免文件被锁）
      .\build.ps1 -VcVars "C:\...\VC\Auxiliary\Build\vcvars64.bat"

    默认不调用 vcvars64.bat：它内部会跑 reg.exe / vswhere，在受限环境里容易被拦，
    拦掉之后 INCLUDE / LIB 是空的，编译会以「找不到 windows.h」失败。
    这里改成直接定位 MSVC 与 Windows SDK，自己拼 INCLUDE / LIB / PATH。
#>
param(
    [string]$Tag = "",
    [string]$VcVars = ""
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot

function Find-MsvcTools {
    # 返回 ...\VC\Tools\MSVC\<版本> 目录
    $globs = @(
        "D:\VSBuildTools\VC\Tools\MSVC",
        "C:\VSBuildTools\VC\Tools\MSVC",
        "$env:ProgramFiles\Microsoft Visual Studio\*\*\VC\Tools\MSVC",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\VC\Tools\MSVC"
    )
    foreach ($g in $globs) {
        $hit = Get-ChildItem $g -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName "bin\Hostx64\x64\cl.exe") } |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    return $null
}

function Find-Sdk {
    # 返回 @{ Include = ...; Lib = ... }，挑装了 ucrt x64 的最高版本
    foreach ($base in @("${env:ProgramFiles(x86)}\Windows Kits\10", "$env:ProgramFiles\Windows Kits\10")) {
        $incRoot = Join-Path $base "Include"
        if (-not (Test-Path $incRoot)) { continue }
        $ver = Get-ChildItem $incRoot -Directory |
            Where-Object { Test-Path (Join-Path $base "Lib\$($_.Name)\ucrt\x64") } |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($ver) {
            return @{
                Include = Join-Path $incRoot $ver.Name
                Lib     = Join-Path $base "Lib\$($ver.Name)"
            }
        }
    }
    return $null
}

$out = Join-Path $root "build"
New-Item -ItemType Directory -Force $out | Out-Null

$name = if ($Tag) { "CSGOFeelClient_$Tag.dll" } else { "CSGOFeelClient.dll" }
$dllPath = Join-Path $out $name

if (Test-Path $dllPath) {
    # 刚编译出来的 DLL 常被杀软/搜索索引短暂占用，重试几次再判定「已注入」
    $removed = $false
    for ($i = 0; $i -lt 5; $i++) {
        try {
            Remove-Item $dllPath -Force -ErrorAction Stop
            $removed = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    }
    if (-not $removed) {
        throw "$name 正在被占用（已经注入到游戏里了）。换一个 -Tag 重新编译，例如：build.ps1 -Tag diag2"
    }
}

$sources = Get-ChildItem (Join-Path $root "src") -Filter *.cpp | ForEach-Object { $_.FullName }
$clArgs = @(
    "/nologo", "/std:c++20", "/utf-8", "/EHsc", "/O2", "/MT", "/LD"
) + $sources + @(
    "/Fe:$dllPath",
    "/Fo:$out\",
    "/link", "/SUBSYSTEM:WINDOWS", "/DEBUG:NONE", "winmm.lib", "user32.lib"
)

Write-Host "==> 编译客户端 DLL ($name)" -ForegroundColor Cyan

# 默认自己拼环境；只有显式传 -VcVars 才走 vcvars（比如换 ARM 工具链）
if ($VcVars) {
    if (-not (Test-Path $VcVars)) { throw "找不到 $VcVars" }
    Write-Host "    使用 vcvars: $VcVars" -ForegroundColor DarkGray
    $quoted = $clArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $cmdLine = 'call "' + $VcVars + '" >nul && cl ' + ($quoted -join ' ')
    cmd /c $cmdLine
    if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }
}
else {
    $msvc = Find-MsvcTools
    $sdk = Find-Sdk
    if (-not $msvc -or -not $sdk) {
        throw "找不到 MSVC / Windows SDK。装 Visual Studio 2022 生成工具（含 C++ 桌面开发）后重试，或用 -VcVars 指定 vcvars64.bat。"
    }

    Write-Host "    直接使用 MSVC: $msvc" -ForegroundColor DarkGray
    $env:INCLUDE = @(
        (Join-Path $msvc "include"),
        (Join-Path $sdk.Include "ucrt"),
        (Join-Path $sdk.Include "shared"),
        (Join-Path $sdk.Include "um"),
        (Join-Path $sdk.Include "winrt"),
        (Join-Path $sdk.Include "cppwinrt")
    ) -join ';'
    $env:LIB = @(
        (Join-Path $msvc "lib\x64"),
        (Join-Path $sdk.Lib "ucrt\x64"),
        (Join-Path $sdk.Lib "um\x64")
    ) -join ';'

    $cl = Join-Path $msvc "bin\Hostx64\x64\cl.exe"
    & $cl @clArgs
    if ($LASTEXITCODE -ne 0) { throw "编译失败 (exit $LASTEXITCODE)" }
}

Get-Item $dllPath | Select-Object FullName, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } }

# bob 参数文件跟着 DLL 走，DLL 会读同目录的 csgo_feel.ini
$ini = Join-Path $root "csgo_feel.ini"
if (Test-Path $ini) { Copy-Item $ini (Join-Path $out "csgo_feel.ini") -Force }
