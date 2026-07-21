@echo off
setlocal
fltmc >nul 2>&1
if errorlevel 1 (
    set "SENDCMD_LAUNCHER=%~f0"
    powershell.exe -NoProfile -Command "Start-Process -FilePath $env:ComSpec -Verb RunAs -ArgumentList @('/d','/c',(([char]34) + $env:SENDCMD_LAUNCHER + ([char]34)))"
    exit /b
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-secure.ps1" -ApiKeyFile "%~dp0..\api-key.provision" -KeepProvisioningFile
if errorlevel 1 (
    echo.
    echo Installation failed. The provisioning key was not deleted.
) else (
    echo.
    echo Installation succeeded. Use the USB drive on the next PC.
)
pause
