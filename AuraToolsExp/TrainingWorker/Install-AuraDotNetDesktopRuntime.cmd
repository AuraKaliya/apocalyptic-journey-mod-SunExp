@echo off
setlocal
title Aura .NET Desktop Runtime Installer
pushd "%~dp0"
set "INSTALLER_DIRECTORY=%CD%"
set "RUNTIME_CHECKER=%INSTALLER_DIRECTORY%\Test-AuraDotNetDesktopRuntime.ps1"
set "DOTNET_DOWNLOAD_URL=https://dotnet.microsoft.com/en-us/download/dotnet/8.0"

echo ============================================================
echo Aura Foundation Trainer - .NET 8 Desktop Runtime x64
echo ============================================================
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNTIME_CHECKER%"
if not errorlevel 1 (
    echo The required runtime is already installed.
    goto success
)

where winget.exe >nul 2>nul
if errorlevel 1 goto manual_install

echo Installing Microsoft.DotNet.DesktopRuntime.8 x64 with winget...
winget.exe install --id Microsoft.DotNet.DesktopRuntime.8 --exact --source winget --architecture x64 --accept-package-agreements --accept-source-agreements
if errorlevel 1 goto manual_install

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%RUNTIME_CHECKER%"
if errorlevel 1 goto manual_install

:success
echo.
echo .NET 8 Desktop Runtime x64 is ready.
if not "%AURA_TRAINER_NO_PAUSE%"=="1" pause
popd
exit /b 0

:manual_install
echo.
echo Automatic installation was unavailable or did not complete.
echo The official .NET 8 download page will now open.
echo Select ".NET Desktop Runtime 8" for Windows x64, install it,
echo then start the Aura trainer again.
start "" "%DOTNET_DOWNLOAD_URL%"
if not "%AURA_TRAINER_NO_PAUSE%"=="1" pause
popd
exit /b 10
