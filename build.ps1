#requires -Version 5.1
<#
    一键构建 csgo-feel 的双端 DLL，统一输出到 D:\code\csgo-feel-releases。

    默认只产出「插件本身」——两个 .dll，别的什么都不放：
        D:\code\csgo-feel-releases\server\CSGOFeel.dll          CounterStrikeSharp 服务端插件
        D:\code\csgo-feel-releases\client\CSGOFeelClient.dll    客户端视觉模组（注入 cs2.exe）
        D:\code\csgo-feel-releases\client\csgo_feel.ini         客户端配置
        D:\code\csgo-feel-releases\BUILD.txt                    版本 / 时间 / SHA256 清单

    可选（默认关，都要显式加开关）：
        -WithMovement   独立移动补丁 -> ...\movement\CSGOFeel.Movement.dll
        -WithGui        2D 模拟器     -> ...\gui\CSGORestore.exe
        -WithInjector   从源码编译注入器 -> ...\tools\injector\csgo-feel-injector.exe
                        （**不进 server/client 交付目录、不入库**：仓库只提供源码与编译脚本）

    用法（在仓库根目录执行）：
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -OutDir "D:\somewhere\else"
#>
param(
    [string]$OutDir = "D:\code\csgo-feel-releases",
    [switch]$Test,
    [switch]$WithGui,
    [switch]$WithMovement,
    [switch]$WithInjector
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = $OutDir

function Step($text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}
function Ok($text) { Write-Host "    $text" -ForegroundColor Green }
function Warn($text) { Write-Host "    $text" -ForegroundColor Yellow }

New-Item -ItemType Directory -Force $out | Out-Null

# 交付目录里只允许出现 DLL / ini / 说明。旧版本把注入器和 GUI 一起塞进根目录，
# 这里清掉，免得打包时把「不该分发的可执行文件」带出去。
# 只清「旧的根级 exe」和「注入器」，gui\ 里的模拟器是显式 -WithGui 才有的，不动它。
Get-ChildItem -Path $out -Filter *.exe -File -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force
    Warn "删除交付目录根部的旧可执行文件：$($_.Name)"
}
Get-ChildItem -Path $out -Recurse -Filter "csgo-feel-injector.*" -File -ErrorAction SilentlyContinue | ForEach-Object {
    Remove-Item $_.FullName -Force
    Warn "删除交付目录里的注入器产物：$($_.FullName.Replace($out + '\', ''))"
}
Get-ChildItem -Path $out -Recurse -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force

# ---------------------------------------------------------------- 1. 服务端 DLL
Step "编译服务端插件（CounterStrikeSharp）"
dotnet build (Join-Path $root "server\CSGOFeel.csproj") -c Release -v q
if ($LASTEXITCODE -ne 0) { throw "服务端插件编译失败 (exit $LASTEXITCODE)" }

$serverOut = Join-Path $out "server"
New-Item -ItemType Directory -Force $serverOut | Out-Null
Copy-Item (Join-Path $root "server\bin\Release\net10.0\CSGOFeel.dll") $serverOut -Force
Ok "server\CSGOFeel.dll"

# ---------------------------------------------------------------- 2. 客户端 DLL
Step "编译客户端 DLL（MSVC，C++20，/MT）"

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
    # 这时改用带 tag 的名字编译（同一份源码，只是换个输出名），交付目录里仍然按正式名同步，
    # 否则整个构建在这里中断，产物会停留在上一次的旧版本。
    Warn "CSGOFeelClient.dll 正被占用（游戏里还加载着旧版），改用 -Tag auto 编译"
    if ((Invoke-ClientBuild "auto") -ne 0) { throw "客户端 DLL 编译失败" }
    $clientDll = Join-Path $root "client\build\CSGOFeelClient_auto.dll"
}

$clientOut = Join-Path $out "client"
New-Item -ItemType Directory -Force $clientOut | Out-Null
Copy-Item $clientDll (Join-Path $clientOut "CSGOFeelClient.dll") -Force
Copy-Item (Join-Path $root "client\csgo_feel.ini") $clientOut -Force
Ok "client\CSGOFeelClient.dll + csgo_feel.ini"

# ---------------------------------------------------------------- 3. 可选：移动补丁
if ($WithMovement) {
    Step "编译独立移动补丁（默认关闭，仅 LAN 实验）"
    dotnet build (Join-Path $root "movement\CSGOFeel.Movement.csproj") -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "移动补丁编译失败 (exit $LASTEXITCODE)" }
    $movementOut = Join-Path $out "movement"
    New-Item -ItemType Directory -Force $movementOut | Out-Null
    Copy-Item (Join-Path $root "movement\bin\Release\net10.0\CSGOFeel.Movement.dll") $movementOut -Force
    Ok "movement\CSGOFeel.Movement.dll"
}

# ---------------------------------------------------------------- 4. 可选：GUI
if ($WithGui) {
    Step "发布 GUI 模拟器（单文件 exe）"

    $running = Get-Process CSGORestore -ErrorAction SilentlyContinue
    if ($running) {
        throw "模拟器正在运行（pid $($running.Id -join ', ')），先关闭再发布；不会强制结束进程。"
    }

    $guiOut = Join-Path $out "gui"
    New-Item -ItemType Directory -Force $guiOut | Out-Null
    dotnet publish (Join-Path $root "gui\CSGORestore.csproj") `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $guiOut
    if ($LASTEXITCODE -ne 0) { throw "GUI 发布失败 (exit $LASTEXITCODE)" }
    Ok "gui\CSGORestore.exe"
}

# ---------------------------------------------------------------- 5. 可选：注入器（只从源码编译）
if ($WithInjector) {
    Step "从源码编译注入器（不入库，也不进交付目录）"
    # 故意放在源码树的 build\injector：交付目录里只留 DLL。
    # 谁要用谁自己编，这样发布包里不会出现内存加载类可执行文件。
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root "build-injector.ps1") -Output (Join-Path $root "build\injector")
    if ($LASTEXITCODE -ne 0) { throw "注入器编译失败 (exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------- 6. 测试
if ($Test) {
    Step "回归测试（C++ 纯逻辑 + 服务端离线冒烟）"
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root "tests\run.ps1")
    if ($LASTEXITCODE -ne 0) { throw "回归测试失败" }
    dotnet run --project (Join-Path $root "tools\plugintest\plugintest.csproj") -c Release -v q
    if ($LASTEXITCODE -ne 0) { throw "冒烟测试失败 (exit $LASTEXITCODE)" }
}

# ---------------------------------------------------------------- 汇总 + 清单
Step "完成，双端 DLL 如下"
$built = Get-ChildItem $out -Recurse -Filter *.dll -File |
    Where-Object { $_.FullName -notmatch '\\tools\\' }

$built | Select-Object @{n = '文件'; e = { $_.FullName.Replace($out + '\', '') } },
                        @{n = '大小(KB)'; e = { [math]::Round($_.Length / 1KB, 1) } },
                        @{n = 'SHA256'; e = { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16) } } |
    Format-Table -AutoSize

# 清单落到交付目录，方便对照「手上这个 DLL 是哪次构建出来的」
$manifest = Join-Path $out "BUILD.txt"
$lines = @(
    "csgo-feel 构建清单",
    "时间       : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "来源仓库   : $root",
    "Git 提交   : $((& git -C $root rev-parse --short HEAD) 2>$null)",
    "产物目录   : $out",
    "",
    "文件                                      大小(KB)   SHA256"
)
foreach ($f in $built) {
    $rel = $f.FullName.Replace($out + '\', '')
    $lines += ("{0,-40} {1,8}   {2}" -f $rel, [math]::Round($f.Length / 1KB, 1),
        (Get-FileHash $f.FullName -Algorithm SHA256).Hash)
}
$lines += ""
$lines += "注入器不随包分发：仓库只提供源码（tools\injector）与编译脚本（build-injector.ps1）。"
[System.IO.File]::WriteAllLines($manifest, $lines, (New-Object System.Text.UTF8Encoding($false)))
Ok "清单：$manifest"

if (-not $WithInjector) {
    Write-Host "注入器不随包分发（仓库只提供源码与编译脚本）。需要时自行执行：" -ForegroundColor Yellow
    Write-Host "    powershell -ExecutionPolicy Bypass -File .\build-injector.ps1" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "服务端部署： powershell -ExecutionPolicy Bypass -File $root\deploy-server.ps1" -ForegroundColor Green
Write-Host "客户端注入： powershell -ExecutionPolicy Bypass -File $root\client\run.ps1" -ForegroundColor Green
