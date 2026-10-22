[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid]$InterfaceId,
    [ValidatePattern('^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.local$')]
    [string]$HostName = 'clipbridge-test.local',
    [ValidateRange(1, 65535)]
    [int]$Port = 5033
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts\clipbridge-discovery-win-x64\ClipBridge.Discovery.exe'
$stateDirectory = Join-Path $projectRoot '.tools\bonjour-live'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'Publish src/ClipBridge.Discovery for win-x64 to artifacts/clipbridge-discovery-win-x64 first.'
}
if (-not (Test-Path -LiteralPath "$env:WINDIR\System32\dnssd.dll")) {
    throw 'Apple Bonjour must be installed before enabling discovery.'
}
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$arguments = '--interface-id {0} --hostname {1} --port {2} --state-dir "{3}"' -f $InterfaceId, $HostName, $Port, $stateDirectory
$startupDirectory = [Environment]::GetFolderPath('Startup')
if ([string]::IsNullOrWhiteSpace($startupDirectory)) {
    throw 'The current user has no Startup folder.'
}
$shortcutPath = Join-Path $startupDirectory 'ClipBridge Bonjour Discovery.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $executable) {
        throw 'The existing startup shortcut belongs to another installation; it was not changed.'
    }
    $shortcut.TargetPath = $executable
    $shortcut.Arguments = $arguments
    $shortcut.WorkingDirectory = $projectRoot
    $shortcut.Description = 'Publishes a Bonjour hostname for the existing SyncClipboard HTTP service.'
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)
    }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
}
Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $projectRoot -WindowStyle Hidden
Write-Output "Current-user startup configured for ${HostName}:$Port. No HTTP server, NAS settings or firewall rules were changed."
