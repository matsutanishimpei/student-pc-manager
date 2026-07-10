# sendCMD Build & Publish Script
# Builds both projects as single EXE files (Self-Contained / Single File)

Write-Host "--- Starting sendCMD Release Build ---" -ForegroundColor Cyan

$PublishDir = Join-Path (Get-Location) "publish"

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

# 1. Server Build (Student PC Service)
Write-Host "`n[1/3] Building Server (Student Service)..." -ForegroundColor Yellow
dotnet publish server/server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/server"

if ($LASTEXITCODE -eq 0) {
    Copy-Item "server/install.bat" "$PublishDir/server/" -Force
    Copy-Item "server/uninstall.bat" "$PublishDir/server/" -Force
    
    # Inject default API key into release appsettings.json
    $AppsettingsPath = Join-Path "$PublishDir/server" "appsettings.json"
    if (Test-Path $AppsettingsPath) {
        $json = Get-Content $AppsettingsPath -Raw | ConvertFrom-Json
        $json.ApiKey = "5c3e7f41-0f73-455b-b9d9-482470724653"
        $json | ConvertTo-Json -Depth 10 | Out-File $AppsettingsPath -Encoding utf8
    }
    
    Write-Host "✓ Server build successful (API key injected): $PublishDir\server\server.exe" -ForegroundColor Green
} else {
    Write-Error "Failed to build Server."
    exit $LASTEXITCODE
}

# 2. Client Build (Teacher PC App)
Write-Host "`n[2/3] Building Client (Teacher App)..." -ForegroundColor Yellow
dotnet publish client/client.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/client"

if ($LASTEXITCODE -eq 0) {
    # Generate initial config.json with default API key
    $ConfigJson = @{
        ApiKey = "5c3e7f41-0f73-455b-b9d9-482470724653"
    } | ConvertTo-Json
    $ConfigPath = Join-Path "$PublishDir/client" "config.json"
    $ConfigJson | Out-File $ConfigPath -Encoding utf8

    Write-Host "✓ Client build successful (Initial config generated): $PublishDir\client\client.exe" -ForegroundColor Green
} else {
    Write-Error "Failed to build Client."
    exit $LASTEXITCODE
}

# 3. Helper Build (Student PC Helper Process)
Write-Host "`n[3/3] Building Helper (Student Helper)..." -ForegroundColor Yellow
dotnet publish helper/helper.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PublishDir/helper"

if ($LASTEXITCODE -eq 0) {
    Copy-Item "$PublishDir/helper/sendCMD_helper.exe" "$PublishDir/server/" -Force
    Remove-Item "$PublishDir/helper" -Recurse -Force
    Write-Host "✓ Helper build successful (Moved to server folder): $PublishDir\server\sendCMD_helper.exe" -ForegroundColor Green
} else {
    Write-Error "Failed to build Helper."
    exit $LASTEXITCODE
}

Write-Host "`n--- Build Completed ---" -ForegroundColor Green
Write-Host "Published Files:" -ForegroundColor Cyan
Write-Host "  * Teacher App: $PublishDir\client\client.exe" -ForegroundColor White
Write-Host "  * Student Service: $PublishDir\server\server.exe" -ForegroundColor White
Write-Host "  * Student Helper: $PublishDir\server\sendCMD_helper.exe" -ForegroundColor White
Write-Host "`n[Service Registration Example] (Run as Administrator in PowerShell):" -ForegroundColor Cyan
Write-Host "  sc.exe create sendCMD binPath= `"$PublishDir\server\server.exe`" start= auto" -ForegroundColor Gray
Write-Host "  sc.exe start sendCMD" -ForegroundColor Gray
