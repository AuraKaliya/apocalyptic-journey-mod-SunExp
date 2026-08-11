@echo off
setlocal
title Aura Foundation Simulation Viewer
pushd "%~dp0"
set "TRAINER_DIRECTORY=%CD%"
set "RUNTIME_CHECKER=%TRAINER_DIRECTORY%\Test-AuraDotNetDesktopRuntime.ps1"
set "RUNTIME_INSTALLER=%TRAINER_DIRECTORY%\Install-AuraDotNetDesktopRuntime.cmd"
set "VIEWER_EXE=%TRAINER_DIRECTORY%\AuraFoundationTrainer.SimulationViewer.exe"

if not exist "%VIEWER_EXE%" (
    echo Aura Foundation Simulation Viewer is missing:
    echo %VIEWER_EXE%
    goto launch_failed
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNTIME_CHECKER%"
if not errorlevel 1 goto launch_viewer

echo.
choice /C YN /N /M "Install .NET 8 Desktop Runtime x64 now? [Y/N] "
if errorlevel 2 goto runtime_missing
call "%RUNTIME_INSTALLER%"
if errorlevel 1 goto runtime_missing

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNTIME_CHECKER%"
if errorlevel 1 goto runtime_missing

:launch_viewer
start "" "%VIEWER_EXE%" %*
popd
exit /b 0

:runtime_missing
echo.
echo The viewer was not started because .NET 8 Desktop Runtime x64
echo is still missing. Install it and run this launcher again.
goto launch_failed

:launch_failed
if not "%AURA_TRAINER_NO_PAUSE%"=="1" pause
popd
exit /b 10
