@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title CS:GO Feel beta - build kit

:menu
cls
echo ==================================================
echo    CS:GO Feel beta - build kit
echo ==================================================
echo    [1] Build both DLLs  (server + client)  -^> D:\code\csgo-feel-releases
echo    [2] Build injector from source (not distributed)
echo    [3] Build GUI simulator only
echo    [4] Deploy server plugin to CS2
echo    [5] Inject client DLL into running CS2
echo    [0] Exit
echo.
set "choice="
set /p "choice=Select: "

if "%choice%"=="1" goto build_all
if "%choice%"=="2" goto build_injector
if "%choice%"=="3" goto build_gui
if "%choice%"=="4" goto deploy_server
if "%choice%"=="5" goto inject
if "%choice%"=="0" goto end
goto menu

:build_all
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
goto done

:build_injector
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-injector.ps1"
goto done

:build_gui
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -WithGui -OutDir "D:\code\csgo-feel-releases"
goto done

:deploy_server
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0deploy-server.ps1"
goto done

:inject
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0client\run.ps1"
goto done

:done
echo.
echo Finished. Press any key to return to the menu.
pause >nul
goto menu

:end
endlocal
exit /b 0
