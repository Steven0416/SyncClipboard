[CmdletBinding()]
param([string]$Version = '0.1.0-preview.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root ('artifacts\ClipBridge-LAN-Kit-' + $Version + '-win-x64')
if (Test-Path -LiteralPath $output) { throw 'Output already exists; use a new empty output/version.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& dotnet publish (Join-Path $root 'src\ClipBridge.Discovery') -c Release -r win-x64 --self-contained true -o (Join-Path $output 'discovery')
if ($LASTEXITCODE -ne 0) { throw 'Discovery publish failed.' }
Copy-Item -Path (Join-Path $PSScriptRoot 'distribution\*') -Destination $output
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $output
$zip = $output + '.zip'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath ($zip + '.sha256') -Encoding ASCII
Write-Output $zip
