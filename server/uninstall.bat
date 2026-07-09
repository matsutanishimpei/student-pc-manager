@echo off
echo ==================================================
echo   sendCMD Windows サービス アンインストール
echo ==================================================

:: 1. 管理者権限のチェックと昇格の実行
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo 管理者権限が必要です。昇格ダイアログを開きます...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "INSTALL_DIR=C:\Program Files\sendCMD"

echo.
echo [1/3] サービス停止・削除およびプロセス強制終了中...
sc.exe stop sendCMD >nul 2>&1
sc.exe delete sendCMD >nul 2>&1
taskkill /f /im server.exe >nul 2>&1
taskkill /f /im sendCMD_helper.exe >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v sendCMD_helper /f >nul 2>&1
timeout /t 2 /nobreak >nul

echo [2/3] ファイアウォール規則の削除中...
powershell -Command "Remove-NetFirewallRule -Name 'sendCMD' -ErrorAction SilentlyContinue"

echo [3/3] インストール済みファイルを削除中...
if exist "%INSTALL_DIR%" (
    rmdir /s /q "%INSTALL_DIR%"
)

echo.
echo ==================================================
echo   アンインストールが完了しました。
echo ==================================================
echo.
pause
