param(
    [string]$InstallDirectory = "C:\Program Files\sendCMD",
    [Security.SecureString]$ApiKey,
    [string]$ApiKeyFile,
    [switch]$KeepProvisioningFile,
    [switch]$NoServiceRestart
)

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

$settingsPath = Join-Path $InstallDirectory "appsettings.json"
if (-not (Test-Path -LiteralPath $settingsPath)) { throw "appsettings.json was not found at $settingsPath" }

if ($ApiKey -and -not [string]::IsNullOrWhiteSpace($ApiKeyFile)) {
    throw "Specify either ApiKey or ApiKeyFile, not both."
}

$provisioningPath = $null
$secureKey = $ApiKey
if (-not [string]::IsNullOrWhiteSpace($ApiKeyFile)) {
    $provisioningPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ApiKeyFile)
    if (-not (Test-Path -LiteralPath $provisioningPath -PathType Leaf)) {
        throw "The API key provisioning file was not found: $provisioningPath"
    }

    $fileBytes = [IO.File]::ReadAllBytes($provisioningPath)
    try {
        $plainKey = [Text.UTF8Encoding]::new($false, $true).GetString($fileBytes)
        if ($plainKey.StartsWith([char]0xFEFF)) { $plainKey = $plainKey.Substring(1) }
        if ($plainKey.EndsWith("`r`n")) { $plainKey = $plainKey.Substring(0, $plainKey.Length - 2) }
        elseif ($plainKey.EndsWith("`n")) { $plainKey = $plainKey.Substring(0, $plainKey.Length - 1) }
        if ($plainKey.Contains("`r") -or $plainKey.Contains("`n") -or $plainKey -ne $plainKey.Trim()) {
            throw "The provisioning file must contain exactly one API key without leading or trailing whitespace."
        }
        $secureKey = ConvertTo-SecureString -String $plainKey -AsPlainText -Force
        $plainKey = $null
    }
    finally {
        [Array]::Clear($fileBytes, 0, $fileBytes.Length)
    }
}
if (-not $secureKey) { $secureKey = Read-Host "Enter the shared API key (16 or more characters)" -AsSecureString }
$bstr = [IntPtr]::Zero
try {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    if ([string]::IsNullOrWhiteSpace($apiKey) -or $apiKey.Length -lt 16) { throw "The API key must contain at least 16 characters." }
    $clearBytes = [Text.Encoding]::UTF8.GetBytes($apiKey)
    try {
        $encrypted = [Security.Cryptography.ProtectedData]::Protect($clearBytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    }
    finally {
        [Array]::Clear($clearBytes, 0, $clearBytes.Length)
        $apiKey = $null
    }

    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.ApiKey = ""
    if ($settings.PSObject.Properties.Name -contains "ProtectedApiKey") {
        $settings.ProtectedApiKey = [Convert]::ToBase64String($encrypted)
    } else {
        $settings | Add-Member -NotePropertyName ProtectedApiKey -NotePropertyValue ([Convert]::ToBase64String($encrypted))
    }
    $temporaryPath = $settingsPath + ".tmp"
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporaryPath -Encoding UTF8
    Move-Item -LiteralPath $temporaryPath -Destination $settingsPath -Force
    & icacls.exe $settingsPath /inheritance:r /grant:r '*S-1-5-18:(F)' '*S-1-5-32-544:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to restrict appsettings.json permissions." }
    if ($provisioningPath -and -not $KeepProvisioningFile) {
        Remove-Item -LiteralPath $provisioningPath -Force
        if (Test-Path -LiteralPath $provisioningPath) { throw "The provisioning file could not be deleted." }
    }
    if (-not $NoServiceRestart -and (Get-Service -Name sendCMD -ErrorAction SilentlyContinue)) { Restart-Service -Name sendCMD -Force }
    Write-Host "The shared API key was encrypted for this computer and registered successfully." -ForegroundColor Green
    if ($provisioningPath -and $KeepProvisioningFile) {
        Write-Warning "The provisioning file remains on the USB drive. Keep the drive encrypted and under administrator control."
    }
}
finally {
    if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
