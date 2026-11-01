@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title 安装 CSGOFeel 服务端插件

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-server.ps1" %*
set "code=%errorlevel%"

echo.
if "%code%"=="0" (
    echo [完成] 安装成功。
) else (
    echo [失败] 安装没有完成，错误码 %code%。请查看上方提示。
)
echo 按任意键退出。
pause >nul
exit /b %code%
