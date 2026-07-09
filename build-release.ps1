# sendCMD Build & Publish Script
# 両方のプロジェクトを「.NETランタイム不要の単一のEXEファイル (Self-Contained / Single File)」としてビルドします。

Write-Host "--- sendCMD のリリースビルドを開始します ---" -ForegroundColor Cyan

$PublishDir = Join-Path (Get-Location) "publish"

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

# 1. サーバー (生徒PC側サービス) のビルド
Write-Host "`n[1/3] サーバー (生徒用 Windows サービス) をビルド中..." -ForegroundColor Yellow
dotnet publish server/server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/server"

if ($LASTEXITCODE -eq 0) {
    Copy-Item "server/install.bat" "$PublishDir/server/" -Force
    Copy-Item "server/uninstall.bat" "$PublishDir/server/" -Force
    Write-Host "✓ サーバービルド成功: $PublishDir\server\server.exe" -ForegroundColor Green
} else {
    Write-Error "サーバーのビルドに失敗しました。"
    exit $LASTEXITCODE
}

# 2. クライアント (教員PC側管理画面) のビルド
Write-Host "`n[2/3] クライアント (教員用デスクトップアプリ) をビルド中..." -ForegroundColor Yellow
dotnet publish client/client.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/client"

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ クライアントビルド成功: $PublishDir\client\client.exe" -ForegroundColor Green
} else {
    Write-Error "クライアントのビルドに失敗しました。"
    exit $LASTEXITCODE
}

# 3. ヘルパー (生徒PC側常駐ヘルパー) のビルド
Write-Host "`n[3/3] ヘルパー (生徒用常駐プロセス) をビルド中..." -ForegroundColor Yellow
dotnet publish helper/helper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/helper"

if ($LASTEXITCODE -eq 0) {
    Copy-Item "$PublishDir/helper/sendCMD_helper.exe" "$PublishDir/server/" -Force
    Remove-Item "$PublishDir/helper" -Recurse -Force
    Write-Host "✓ ヘルパービルド成功・サーバー同梱完了: $PublishDir\server\sendCMD_helper.exe" -ForegroundColor Green
} else {
    Write-Error "ヘルパーのビルドに失敗しました。"
    exit $LASTEXITCODE
}

Write-Host "`n--- ビルド完了 ---" -ForegroundColor Green
Write-Host "配布用ファイル:" -ForegroundColor Cyan
Write-Host "  ・教員PC用アプリ: $PublishDir\client\client.exe" -ForegroundColor White
Write-Host "  ・生徒PC用サービス: $PublishDir\server\server.exe" -ForegroundColor White
Write-Host "  ・生徒PC用常駐ヘルパー (サーバーに同梱済み): $PublishDir\server\sendCMD_helper.exe" -ForegroundColor White
Write-Host "`n[サービス登録用コマンドの例] (管理者用PowerShellで実行):" -ForegroundColor Cyan
Write-Host "  sc.exe create sendCMD binPath= `"$PublishDir\server\server.exe`" start= auto" -ForegroundColor Gray
Write-Host "  sc.exe start sendCMD" -ForegroundColor Gray
