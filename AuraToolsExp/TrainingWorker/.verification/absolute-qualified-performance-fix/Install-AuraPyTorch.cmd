@echo off
setlocal
title Aura PyTorch Environment Installer
pushd "%~dp0"
set "INSTALLER_DIRECTORY=%CD%"

echo ============================================================
echo Aura Transformer Teacher - PyTorch Environment Installer
echo ============================================================
echo The installer will auto-select CUDA when available and fall
echo back to CPU when CUDA validation fails.
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%INSTALLER_DIRECTORY%\Setup-AuraTransformerTeacher.ps1" %*
set "INSTALL_EXIT_CODE=%ERRORLEVEL%"

echo.
if "%INSTALL_EXIT_CODE%"=="0" (
    echo Installation completed successfully.
) else (
    echo Installation failed with exit code %INSTALL_EXIT_CODE%.
)

if not "%AURA_INSTALL_NO_PAUSE%"=="1" pause
popd
exit /b %INSTALL_EXIT_CODE%
