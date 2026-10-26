[CmdletBinding()]
param([switch]$Configure, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
try {
    $root = $PSScriptRoot
    $exe = Join-Path $root 'discovery\ClipBridge.Discovery.exe'
    $settingsPath = Join-Path $root 'settings.json'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Missing discovery executable. Extract the entire ZIP first.' }
    if (-not (Test-Path -LiteralPath "$env:WINDIR\System32\dnssd.dll")) {
        throw 'Apple Bonjour is required. Install Bonjour from an official Apple distribution, then run Setup again.'
    }
    $service = Get-Service -Name 'Bonjour Service' -ErrorAction SilentlyContinue
    if ($null -eq $service -or $service.Status -ne 'Running') { throw 'Apple Bonjour Service must be installed and running.' }
    if ($CheckOnly) { Write-Output 'Discovery files and Bonjour prerequisites OK.'; exit 0 }
    if ($Configure -or -not (Test-Path -LiteralPath $settingsPath)) {
        if (Get-Process -Name ClipBridge.Discovery -ErrorAction SilentlyContinue) {
            throw 'A discovery process is already running. Close the existing installation before configuring another one.'
        }
        $adapters = @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up')
        if ($adapters.Count -eq 0) { throw 'Connect a physical Wi-Fi or Ethernet adapter first.' }
        for ($i = 0; $i -lt $adapters.Count; $i++) { Write-Host ('[{0}] {1}' -f ($i + 1), $adapters[$i].Name) }
        $number = 0
        if (-not [int]::TryParse((Read-Host 'Adapter number'), [ref]$number) -or $number -lt 1 -or $number -gt $adapters.Count) { throw 'Invalid adapter number.' }
        $hostName = Read-Host 'Hostname (default clipbridge-test.local; use a different name for each PC)'
        if ([string]::IsNullOrWhiteSpace($hostName)) { $hostName = 'clipbridge-test.local' }
        if ($hostName -notmatch '^[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.local$') { throw 'Use one DNS label followed by .local, for example clipbridge-office.local.' }
        $clientPath = (Read-Host 'Full path to original SyncClipboard.exe').Trim('"')
        if (-not (Test-Path -LiteralPath $clientPath -PathType Leaf) -or [IO.Path]::GetFileName($clientPath) -ne 'SyncClipboard.exe') { throw 'Select an existing original SyncClipboard.exe.' }
        $settings = [ordered]@{ InterfaceId = $adapters[$number - 1].InterfaceGuid.ToString(); HostName = $hostName; Port = 5033; ClientPath = [IO.Path]::GetFullPath($clientPath) }
        $startup = [Environment]::GetFolderPath('Startup')
        $shell = New-Object -ComObject WScript.Shell
        try {
            $links = @(
                @{ Name = 'ClipBridge Bonjour Discovery.lnk'; Target = $exe; Arguments = '--interface-id {0} --hostname {1} --port 5033 --state-dir "{2}"' -f $settings.InterfaceId, $hostName, (Join-Path $root 'state') },
                @{ Name = 'ClipBridge LAN Client.lnk'; Target = $settings.ClientPath; Arguments = '' }
            )
            foreach ($item in $links) {
                $path = Join-Path $startup $item.Name
                $link = $shell.CreateShortcut($path)
                try {
                    if ((Test-Path -LiteralPath $path) -and $link.TargetPath -ne $item.Target) { throw ('Existing startup item belongs to another installation: ' + $item.Name) }
                } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
            }
            $settings | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding UTF8
            foreach ($item in $links) {
                $link = $shell.CreateShortcut((Join-Path $startup $item.Name))
                try {
                    $link.TargetPath = $item.Target
                    $link.Arguments = $item.Arguments
                    $link.WorkingDirectory = Split-Path -Parent $item.Target
                    $link.Save()
                } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
            }
        } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
        Write-Host 'Current-user login startup configured for discovery and the original client.'
    }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $state = Join-Path $root 'state'
    New-Item -ItemType Directory -Path $state -Force | Out-Null
    Start-Process -FilePath $exe -ArgumentList ('--interface-id {0} --hostname {1} --port {2} --state-dir "{3}"' -f $settings.InterfaceId, $settings.HostName, $settings.Port, $state) -WindowStyle Hidden
    $running = Get-Process -Name SyncClipboard -ErrorAction SilentlyContinue
    if ($running -and -not ($running | Where-Object Path -eq $settings.ClientPath)) { throw 'Another SyncClipboard installation is running. Close it before starting this copy.' }
    if (-not $running) { Start-Process -FilePath $settings.ClientPath -WorkingDirectory (Split-Path -Parent $settings.ClientPath) -WindowStyle Hidden }
    Write-Host ('Started. iPhone server address: http://{0}:{1}' -f $settings.HostName, $settings.Port)
    Write-Host 'Configure the built-in server and local sync account as described in README.txt.'
    Write-Host 'After a few seconds, state\status.txt shows registration status.'
} catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
