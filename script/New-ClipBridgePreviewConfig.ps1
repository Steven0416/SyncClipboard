[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ClientDirectory,
    [Parameter(Mandatory)][Guid]$InterfaceId,
    [Parameter(Mandatory)][Uri]$NasUri,
    [Parameter(Mandatory)][string]$PairingDirectory,
    [switch]$AllowSelectedPublicNetwork
)
$ErrorActionPreference = 'Stop'
if (-not $NasUri.IsAbsoluteUri -or $NasUri.Scheme -ne 'https' -or $NasUri.UserInfo -or $NasUri.Query -or $NasUri.Fragment) {
    throw 'A credential-free HTTPS NAS base address is required.'
}
$client = (Resolve-Path -LiteralPath $ClientDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $client 'SyncClipboard.exe'))) { throw 'Publish the preview client first.' }
foreach ($file in @('StaticConfig.json','SyncClipboard.json')) {
    if (Test-Path -LiteralPath (Join-Path $client $file)) { throw "Existing client configuration was not changed: $file" }
}
if (Test-Path -LiteralPath $PairingDirectory) { throw 'Pairing output already exists; do not regenerate a live identity.' }
$adapter = @(Get-NetAdapter -Physical | Where-Object { [Guid]$_.InterfaceGuid -eq $InterfaceId })
if ($adapter.Count -ne 1) { throw 'Choose exactly one physical adapter.' }
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $bytes = New-Object byte[] 32
    $random.GetBytes($bytes)
    $password = -join ($bytes | ForEach-Object { $_.ToString('x2') })
    $random.GetBytes($bytes)
    $token = -join ($bytes | ForEach-Object { $_.ToString('x2') })
} finally { $random.Dispose() }
$device = [Guid]::NewGuid().ToString('N')
$phone = [Guid]::NewGuid().ToString('N')
$user = 'clipbridge-preview'
$config = [ordered]@{
    ConfigVersion = 3
    Account = @{ AccountId='1'; AccountType='SyncClipboard' }
    SavedAccounts = @{ SyncClipboard = @{ '1' = @{ RemoteURL=$NasUri.AbsoluteUri;UserName=$user;Password=$password;CustomName='ClipBridge preview relay';DeletePreviousFilesOnPush=$true } } }
    SyncService = @{ SyncSwitchOn=$true; PullSwitchOn=$true; PushSwitchOn=$true }
    ServerService = @{ SwitchOn=$false }
    History = @{ EnableHistory=$false; EnableSyncHistory=$false }
    Program = @{ CheckUpdateOnStartUp=$false; AutoDownloadUpdate=$false; DiagnoseMode=$false; Language='zh-CN' }
    LanFirst = @{ Enabled=$true; Port=5034; InterfaceId=$InterfaceId.ToString(); AllowPublicNetworks=[bool]$AllowSelectedPublicNetwork; OnDemandNas=$true; DeviceId=$device; Token=$token }
}
New-Item -ItemType Directory -Path $PairingDirectory | Out-Null
@{ Env=@{PortableUserConfig=$true;PortableAppDataFolder=$true} } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $client 'StaticConfig.json') -Encoding UTF8
$config | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $client 'SyncClipboard.json') -Encoding UTF8
@{ DeviceId=$device; PhoneDeviceId=$phone; LanUrl=('http://syncclip-'+$device+'.local:5034/api/lan-first/v1/clipboard'); LanToken=$token; NasBaseUrl=$NasUri.AbsoluteUri; NasUserName=$user; NasPassword=$password } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PairingDirectory 'private-pairing.json') -Encoding UTF8
@"
services:
  clipbridge-relay-preview:
    image: clipbridge-relay:0.2.0-dev.1
    container_name: clipbridge-relay-preview
    restart: unless-stopped
    ports:
      - "127.0.0.1:8787:5033"
    environment:
      SYNCCLIPBOARD_USERNAME: "$user"
      SYNCCLIPBOARD_PASSWORD: "$password"
    volumes:
      - /volume1/docker/clipbridge-relay-preview/data:/app/data
"@ | Set-Content -LiteralPath (Join-Path $PairingDirectory 'compose.yaml') -Encoding UTF8
Write-Output 'Prepared isolated preview configuration and private pairing files. No production configuration was read or changed; do not upload pairing files to GitHub.'
