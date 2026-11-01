#requires -Version 5.1
<#
    只构建注入器 csgo-feel-injector.exe。
    不需要 MSVC，也不需要编译客户端 DLL；适合只想拿注入器的人。

    用法（在仓库根目录执行）：
      .\build-injector.ps1
      .\build-injector.ps1 -Output "D:\csgo-feel"
      .\build-injector.ps1 -SelfContained        # 目标机没装 .NET 10 时用

    默认产物：dist\client\csgo-feel-injector.exe
#>
param(
    [string]$Output = "",
    [switch]$SelfContained,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "tools\injector\injector.csproj"

function Fail($msg) { Write-Host "[fail] $msg" -ForegroundColor Red; exit 1 }
function Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

if (-not (Test-Path $project)) { Fail "找不到注入器工程：$project" }

# .NET SDK 10 是硬要求（服务端与注入器都用 net10.0）
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail "找不到 dotnet，请先安装 .NET SDK 10：https://dotnet.microsoft.com/download" }

$version = (& dotnet --version) 2>$null
Step "dotnet SDK $version"

if (-not $Output) { $Output = Join-Path $root "dist\client" }
New-Item -ItemType Directory -Force $Output | Out-Null

if ($SelfContained) {
    Step "发布注入器（self-contained，免安装运行时）"
    & dotnet publish $project `
        -c $Configuration `
        -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $Output
    if ($LASTEXITCODE -ne 0) { Fail "发布失败 (exit $LASTEXITCODE)" }
}
else {
    Step "编译注入器（framework-dependent）"
    & dotnet build $project -c $Configuration -v q
    if ($LASTEXITCODE -ne 0) { Fail "编译失败 (exit $LASTEXITCODE)" }

    $built = Join-Path $root "tools\injector\bin\$Configuration\net10.0-windows\csgo-feel-injector.exe"
    if (-not (Test-Path $built)) { Fail "编译成功但找不到产物：$built" }
    Copy-Item $built (Join-Path $Output "csgo-feel-injector.exe") -Force
}

$exe = Join-Path $Output "csgo-feel-injector.exe"
if (-not (Test-Path $exe)) { Fail "找不到最终产物：$exe" }

Get-Item $exe |
    Select-Object FullName, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB, 1) } } |
    Format-Table -AutoSize

if (-not $SelfContained) {
    Write-Host "提示：framework-dependent 版本需要目标机安装 .NET 10 运行时；" -ForegroundColor Yellow
    Write-Host "      想做成单文件免运行时，用 -SelfContained 重新执行。" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "用法：把 csgo-feel-injector.exe 和 CSGOFeelClient.dll 放在同一目录，" -ForegroundColor Green
Write-Host "      CS2 启动项加 -insecure，主菜单阶段执行：" -ForegroundColor Green
Write-Host "      .\csgo-feel-injector.exe cs2 CSGOFeelClient.dll" -ForegroundColor Green
