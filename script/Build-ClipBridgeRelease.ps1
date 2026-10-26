[CmdletBinding()]
param(
    [string]$Version = '0.2.0-preview.1',
    [Parameter(Mandatory)][string]$ShortcutPath
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root ('artifacts\ClipBridge-' + $Version + '-win-x64')
if (Test-Path -LiteralPath $output) { throw 'Use a new empty release directory/version.' }
$shortcut = (Resolve-Path -LiteralPath $ShortcutPath).Path
$signedBytes = [IO.File]::ReadAllBytes($shortcut)
if ([Text.Encoding]::ASCII.GetString($signedBytes, 0, 4) -ne 'AEA1') { throw 'A signed iPhone shortcut is required.' }
New-Item -ItemType Directory -Path $output | Out-Null
& dotnet publish (Join-Path $root 'src\SyncClipboard.WinUI3') -c Release -r win-x64 -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --self-contained true -o (Join-Path $output 'client') -m:2 -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
& dotnet publish (Join-Path $root 'src\ClipBridge.Setup') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $output 'setup')
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'distribution-v02\*') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $output
$packageRoot = $env:NUGET_PACKAGES
if (-not $packageRoot) { $packageRoot = Join-Path $env:USERPROFILE '.nuget\packages' }
$centralPackages = [xml](Get-Content -LiteralPath (Join-Path $root 'src\Directory.Packages.props') -Raw)
$qrVersion = ($centralPackages.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'QRCoder').Version
New-Item -ItemType Directory -Path (Join-Path $output 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $packageRoot "qrcoder\$qrVersion\LICENSE.txt") -Destination (Join-Path $output 'licenses\QRCoder-LICENSE.txt')
Copy-Item -LiteralPath $shortcut -Destination (Join-Path $output 'ClipBridge.shortcut')
'{"Env":{"PortableUserConfig":true,"PortableAppDataFolder":true}}' | Set-Content -LiteralPath (Join-Path $output 'client\StaticConfig.json') -Encoding UTF8
foreach ($forbidden in @('client\SyncClipboard.json','client\appdata','client\pairing-private')) {
    if (Test-Path -LiteralPath (Join-Path $output $forbidden)) { throw "Private/runtime content in release: $forbidden" }
}
$manifest = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($output.Length + 1)
})
$manifest | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding UTF8
$zip = $output + '.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
('{0}  {1}' -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Output $zip
