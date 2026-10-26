[CmdletBinding()]
param([switch]$ConfigureStartup, [switch]$DisableStartup)
$ErrorActionPreference = 'Stop'
try {
    $exe = Join-Path $PSScriptRoot 'client\SyncClipboard.exe'
    $config = Join-Path $PSScriptRoot 'client\SyncClipboard.json'
    $startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'ClipBridge 0.2.lnk'
    if ($DisableStartup) {
        if (Test-Path -LiteralPath $startupLink) {
            $shell = New-Object -ComObject WScript.Shell
            try {
                $link = $shell.CreateShortcut($startupLink)
                try {
                    if ($link.TargetPath -ne $exe) { throw 'Startup belongs to another ClipBridge directory. Run its DisableStartup.cmd.' }
                } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
                Remove-Item -LiteralPath $startupLink
            } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
        }
        Write-Output 'ClipBridge login startup disabled for this directory.'
        exit 0
    }
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $config)) {
        throw 'Extract the complete ZIP and run Setup.cmd first.'
    }
    $processes = @(Get-Process -Name SyncClipboard -ErrorAction SilentlyContinue)
    if ($processes | Where-Object { $_.Path -ne $exe }) {
        throw 'Another SyncClipboard copy is running. Exit it before starting ClipBridge.'
    }
    if ($ConfigureStartup) {
        if ((Read-Host 'Start this ClipBridge automatically at Windows login? [Y/N]') -eq 'Y') {
            $shell = New-Object -ComObject WScript.Shell
            try {
                $link = $shell.CreateShortcut($startupLink)
                try {
                    if ((Test-Path -LiteralPath $startupLink) -and $link.TargetPath -ne $exe) {
                        throw 'An existing ClipBridge startup belongs to another directory. Disable it there first.'
                    }
                    $link.TargetPath = $exe
                    $link.WorkingDirectory = Split-Path -Parent $exe
                    $link.Save()
                } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($link) }
            } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
            Write-Output 'Current-user login startup enabled. Keep this extracted directory in place.'
        }
    }
    if (-not $processes) {
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Hidden
    }
    Write-Output 'ClipBridge is running. Bonjour is managed by its Windows service; do not launch it manually.'
} catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
