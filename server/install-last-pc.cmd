@echo off
setlocal
fltmc >nul 2>&1
if errorlevel 1 (
    set "SENDCMD_LAUNCHER=%~f0"
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:ComSpec -Verb RunAs -ArgumentList @('/d','/c',(([char]34) + $env:SENDCMD_LAUNCHER + ([char]34)))"
    exit /b
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-secure.ps1" -ApiKeyFile "%~dp0..\api-key.provision"
if errorlevel 1 (
    echo.
    echo Installation failed. Check whether api-key.provision still exists.
) else (
    echo.
    echo Installation succeeded. The provisioning key was deleted from the USB drive.
)
pause
