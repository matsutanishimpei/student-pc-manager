@echo off
echo ==================================================
echo   sendCMD Windows サービス 自動セットアップ
echo ==================================================

:: 1. 管理者権限のチェックと昇格の実行
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo 管理者権限が必要です。昇格ダイアログを開きます...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

:: 2. インストール設定
set "INSTALL_DIR=C:\Program Files\sendCMD"
set "PORT=5000"

echo.
echo [1/4] 既存のサービスとプロセスを停止・クリーンアップ中...
:: 既存サービスが登録されている場合は停止・削除
sc.exe stop sendCMD >nul 2>&1
sc.exe delete sendCMD >nul 2>&1
:: 実行中のプロセスがあれば強制終了してロック解除
taskkill /f /im server.exe >nul 2>&1
:: サービスの削除完了を待つために2秒待機
timeout /t 2 /nobreak >nul

echo [2/4] インストール先フォルダを作成・ファイルコピー中...
if not exist "%INSTALL_DIR%" (
    mkdir "%INSTALL_DIR%"
)
copy /y "%~dp0server.exe" "%INSTALL_DIR%\"
copy /y "%~dp0appsettings.json" "%INSTALL_DIR%\"

echo [3/4] ファイアウォール規則を追加中 (ポート: %PORT%)...
powershell -Command "Remove-NetFirewallRule -Name 'sendCMD' -ErrorAction SilentlyContinue; New-NetFirewallRule -Name 'sendCMD' -DisplayName 'sendCMD Server' -Direction Inbound -Protocol TCP -LocalPort %PORT% -Action Allow"

echo [4/4] Windows サービスに登録し、起動中...
:: 新規登録と起動
sc.exe create sendCMD binPath= "\"%INSTALL_DIR%\server.exe\"" start= auto
sc.exe start sendCMD

echo.
echo ==================================================
echo   セットアップが正常に完了しました！
echo   サービスはバックグラウンドで自動起動に設定されました。
echo ==================================================
echo.
pause
