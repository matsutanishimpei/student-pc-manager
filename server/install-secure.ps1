param(
    [string]$InstallDirectory = "C:\Program Files\sendCMD",
    [Security.SecureString]$ApiKey,
    [string]$ApiKeyFile,
    [switch]$KeepProvisioningFile,
    [switch]$ReconfigureApiKey
)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this script from an elevated PowerShell window." }
if ($ApiKey -and -not [string]::IsNullOrWhiteSpace($ApiKeyFile)) { throw "Specify either ApiKey or ApiKeyFile, not both." }

$sourceDirectory = $PSScriptRoot
$existingService = Get-Service -Name sendCMD -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne 'Stopped') {
    Stop-Service -Name sendCMD -Force
    $existingService.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'server.exe') -Destination $InstallDirectory -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'sendCMD_helper.exe') -Destination $InstallDirectory -Force
Copy-Item -LiteralPath (Join-Path $sourceDirectory 'configure-api-key.ps1') -Destination $InstallDirectory -Force
$settingsPath = Join-Path $InstallDirectory 'appsettings.json'
if (-not (Test-Path -LiteralPath $settingsPath)) { Copy-Item -LiteralPath (Join-Path $sourceDirectory 'appsettings.json') -Destination $settingsPath }

$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($ReconfigureApiKey -or [string]::IsNullOrWhiteSpace($settings.ProtectedApiKey)) {
    $configurationArguments = @{
        InstallDirectory = $InstallDirectory
        NoServiceRestart = $true
    }
    if ($ApiKey) { $configurationArguments.ApiKey = $ApiKey }
    if (-not [string]::IsNullOrWhiteSpace($ApiKeyFile)) { $configurationArguments.ApiKeyFile = $ApiKeyFile }
    if ($KeepProvisioningFile) { $configurationArguments.KeepProvisioningFile = $true }
    & (Join-Path $InstallDirectory 'configure-api-key.ps1') @configurationArguments
} else {
    & icacls.exe $settingsPath /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
}

$binaryPath = '"' + (Join-Path $InstallDirectory 'server.exe') + '"'
if ($existingService) { & sc.exe config sendCMD binPath= $binaryPath start= auto | Out-Null }
else { & sc.exe create sendCMD binPath= $binaryPath start= auto | Out-Null }
Remove-NetFirewallRule -Name 'sendCMD' -ErrorAction SilentlyContinue
New-NetFirewallRule -Name 'sendCMD' -DisplayName 'sendCMD Server' -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow | Out-Null
Start-Service -Name sendCMD
Write-Host "sendCMD was installed successfully. Existing encrypted settings were preserved." -ForegroundColor Green
