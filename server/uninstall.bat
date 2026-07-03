@echo off
echo ==================================================
echo   sendCMD Windows サービス アンインストール
echo ==================================================

:: 1. 管理者権限のチェックと昇格の試行
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo 管理者権限が必要です。昇格用ダイアログを表示します...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

set "INSTALL_DIR=C:\Program Files\sendCMD"

echo.
echo [1/3] サービスを停止し、削除中...
sc.exe stop sendCMD >nul 2>&1
sc.exe delete sendCMD >nul 2>&1

echo [2/3] ファイアウォール規則を削除中...
powershell -Command "Remove-NetFirewallRule -Name 'sendCMD' -ErrorAction SilentlyContinue"

echo [3/3] インストール済みファイルを削除中...
if exist "%INSTALL_DIR%" (
    rmdir /s /q "%INSTALL_DIR%"
)

echo.
echo ==================================================
echo   アンインストールが正常に完了しました。
echo ==================================================
echo.
pause
