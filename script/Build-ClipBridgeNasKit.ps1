[CmdletBinding()]
param([string]$Version = '0.2.0-preview.1')

$ErrorActionPreference = 'Stop'
if ($Version -ne '0.2.0-preview.1') { throw 'Update the compose image tag and documentation before changing version.' }
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$name = "ClipBridge-Relay-$Version-linux-x64"
$archive = Join-Path $artifacts ($name + '.tar')
$compressed = $archive + '.gz'
$kit = Join-Path $artifacts ("ClipBridge-NAS-Kit-$Version.zip")
if (Test-Path -LiteralPath $kit) { throw 'NAS kit already exists. Move it aside before rebuilding.' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
& dotnet publish (Join-Path $root 'src/SyncClipboard.Server/SyncClipboard.Server.csproj') `
    -c Release -r linux-x64 /t:PublishContainer /p:PublishProfile=LanRelayContainer `
    "/p:ContainerImageTag=$Version" "/p:ContainerArchiveOutputPath=$archive"
if ($LASTEXITCODE -ne 0) { throw 'Relay image build failed.' }
$source = [IO.File]::OpenRead($archive)
$destination = [IO.File]::Create($compressed)
try {
    $gzip = [IO.Compression.GZipStream]::new($destination, [IO.Compression.CompressionLevel]::Optimal, $true)
    try { $source.CopyTo($gzip) } finally { $gzip.Dispose() }
} finally {
    $source.Dispose()
    $destination.Dispose()
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($kit, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @('compose.yaml', '.env.example', 'README.md')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,
            (Join-Path $PSScriptRoot "deployment/clipbridge/$file"), $file) | Out-Null
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,
        (Join-Path $root 'docs/CLIPBRIDGE_DEPLOY_ZH.md'), '部署指南.md') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $root 'LICENSE'), 'LICENSE') | Out-Null
} finally { $zip.Dispose() }
foreach ($file in @($compressed, $kit)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($file))" | Set-Content -LiteralPath ($file + '.sha256') -Encoding ascii
    Write-Output "$([IO.Path]::GetFileName($file)): $hash"
}
