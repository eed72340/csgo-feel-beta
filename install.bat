@echo off
rem ===================================================================
rem  csgo-feel 0.2.0 - one-click installer wrapper
rem  All real work is done by install.ps1 next to this file.
rem  Requires nothing but the PowerShell that ships with Windows.
rem ===================================================================
setlocal
title csgo-feel 0.2.0 installer
cd /d "%~dp0"

if not exist "%~dp0install.ps1" (
    echo [ERROR] install.ps1 not found next to this file.
    echo         Keep install.bat and install.ps1 together.
    echo.
    pause
    exit /b 1
)

where powershell >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Windows PowerShell was not found on this machine.
    echo         Windows 10 / 11 always has it; on a stripped system, run
    echo         install.ps1 manually with PowerShell 5.1 or newer.
    echo.
    pause
    exit /b 1
)

echo.
echo   csgo-feel 0.2.0 installer
echo   ------------------------------------------------------------
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
    echo [FAILED] exit code %RC%
    echo          Read the red lines above. Close CS2 first, or right-click
    echo          this file and choose "Run as administrator".
) else (
    echo [OK] installation finished.
)
echo.
pause
exit /b %RC%
