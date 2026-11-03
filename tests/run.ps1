$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet run --project "$PSScriptRoot\FeelTests.csproj" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Movement policy tests failed' }
# Reuse the project's MSVC/SDK environment discovery (also rebuilds client).
. "$root\client\build.ps1"
# The dot-sourced build sets $root to its own client directory.
$root = Split-Path $PSScriptRoot -Parent
$out = "$root\client\build"

& $cl /nologo /std:c++20 /utf-8 /EHsc /MT /W4 "$PSScriptRoot\bob_test.cpp" "$root\client\src\bob.cpp" "$root\client\src\telemetry.cpp" "/Fo:$out\" "/Fe:$out\bob-test.exe"
if ($LASTEXITCODE -ne 0) { throw 'Native tests failed to compile' }
& "$out\bob-test.exe"
if ($LASTEXITCODE -ne 0) { throw "Native tests failed: $LASTEXITCODE" }

# 手感动画层（切枪下沉回弹 / 上扬弹簧 / 触发去重）是纯逻辑，单独编译单独跑。
& $cl /nologo /std:c++20 /utf-8 /EHsc /MT /W4 "$PSScriptRoot\anim_test.cpp" "$root\client\src\anim.cpp" "/Fo:$out\" "/Fe:$out\anim-test.exe"
if ($LASTEXITCODE -ne 0) { throw 'Animation tests failed to compile' }
& "$out\anim-test.exe"
if ($LASTEXITCODE -ne 0) { throw "Animation tests failed: $LASTEXITCODE" }
