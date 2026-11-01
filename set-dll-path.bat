@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title 设置 CSGOFeel.dll 路径

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0set-dll-path.ps1" %*
set "code=%errorlevel%"

echo.
if "%code%"=="0" (
    echo [完成] 已记录 DLL 路径。
) else (
    echo [失败] 设置没有完成，错误码 %code%。请查看上方提示。
)
echo 按任意键退出。
pause >nul
exit /b %code%
