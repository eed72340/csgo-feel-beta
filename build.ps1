#requires -Version 5.1
<#
    一键构建 csgo-feel 全部产物：
      1. GUI   -> dist\CSGORestore.exe        (self-contained 单文件)
      2. 服务端 -> dist\server\CSGOFeel.dll    (CounterStrikeSharp 插件)
      3. 客户端 -> dist\client\CSGOFeelClient.dll + csgo-feel-injector.exe

    用法（在仓库根目录执行）：
      powershell -ExecutionPolicy Bypass -File .\build.ps1
      powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipGui -Test
#>
param(
    [switch]$SkipGui,
    [switch]$Test
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"

function Step($text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

New-Item -ItemType Directory -Force $dist | Out-Null

# ---------------------------------------------------------------- 1. GUI
if (-not $SkipGui) {
    Step "发布 GUI 单文件 exe"

    # 正在运行的模拟器会锁住 dist\CSGORestore.exe，导致单文件打包失败
    $running = Get-Process CSGORestore -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "检测到模拟器正在运行（pid $($running.Id -join ', ')），先关闭再发布。" -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 800
    }

    dotnet publish (Join-Path $root "gui\CSGORestore.csproj") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $dist
    if ($LASTEXITCODE -ne 0) { throw "GUI 发布失败 (exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------- 2. 服务端
Step "编译服务端插件 (CounterStrikeSharp)"
dotnet build (Join-Path $root "server\CSGOFeel.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "服务端插件编译失败 (exit $LASTEXITCODE)" }

$serverOut = Join-Path $dist "server"
New-Item -ItemType Directory -Force $serverOut | Out-Null
Copy-Item (Join-Path $root "server\bin\Release\net10.0\CSGOFeel.dll") $serverOut -Force

# ---------------------------------------------------------------- 3. 客户端 DLL
Step "编译客户端 DLL (MSVC)"

# client\build.ps1 在 DLL 被占用时会往 stderr 打提示并以非 0 退出。
# 父脚本是 $ErrorActionPreference = "Stop"，原生命令的 stderr 会被当成终止错误，
# 根本走不到下面的降级分支——所以这里必须先临时放宽，拿退出码再判断。
function Invoke-ClientBuild {
    param([string]$Tag)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $arguments = @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $root "client\build.ps1"))
        if ($Tag) { $arguments += @("-Tag", $Tag) }
        & powershell @arguments | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

$clientDll = Join-Path $root "client\build\CSGOFeelClient.dll"
if ((Invoke-ClientBuild "") -ne 0) {
    # 正式名的 DLL 已经注入到正在运行的 cs2 里时，文件被加载器锁住、无法覆盖。
    # 这时改用带 tag 的名字编译（同一份源码，只是换个输出名），dist 里仍然按正式名同步，
    # 否则整个构建在这里中断，dist 会停留在上一次的旧产物。
    Write-Host "CSGOFeelClient.dll 正被占用（游戏里还加载着旧版），改用 -Tag auto 编译" -ForegroundColor Yellow
    if ((Invoke-ClientBuild "auto") -ne 0) { throw "客户端 DLL 编译失败" }
    $clientDll = Join-Path $root "client\build\CSGOFeelClient_auto.dll"
}

$clientOut = Join-Path $dist "client"
New-Item -ItemType Directory -Force $clientOut | Out-Null
Copy-Item $clientDll (Join-Path $clientOut "CSGOFeelClient.dll") -Force
Copy-Item (Join-Path $root "client\csgo_feel.ini") $clientOut -Force

# ---------------------------------------------------------------- 4. 注入器
Step "编译注入器"
dotnet build (Join-Path $root "tools\injector\injector.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "注入器编译失败 (exit $LASTEXITCODE)" }

Copy-Item (Join-Path $root "tools\injector\bin\Release\net10.0-windows\csgo-feel-injector.exe") $clientOut -Force

# ---------------------------------------------------------------- 5. 服务端离线冒烟测试
if ($Test) {
    Step "服务端插件离线冒烟测试（打印各预设下发的指令）"
    dotnet run --project (Join-Path $root "tools\plugintest\plugintest.csproj") -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "冒烟测试失败 (exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------- 汇总
Step "完成，产物如下"
Get-ChildItem $dist -Recurse -File |
    Where-Object { $_.Extension -in @(".exe", ".dll") } |
    Select-Object @{n = '文件'; e = { $_.FullName.Replace($dist + '\', '') } },
                  @{n = '大小(KB)'; e = { [math]::Round($_.Length / 1KB, 1) } } |
    Format-Table -AutoSize

Write-Host "服务端部署： powershell -ExecutionPolicy Bypass -File $root\deploy-server.ps1" -ForegroundColor Green
Write-Host "客户端注入： powershell -ExecutionPolicy Bypass -File $root\client\run.ps1" -ForegroundColor Green
