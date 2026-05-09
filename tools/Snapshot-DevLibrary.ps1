<#
.SYNOPSIS
    Copies the user's real Playnite library to a separate dev location for safe iteration.

.DESCRIPTION
    Creates (or refreshes) a complete copy of the Playnite user data directory at a
    dev-only location. The dev build can then be pointed at this copy via the
    --userdatadir CLI flag (see Run-Dev.ps1) so all development work touches the
    snapshot instead of the production library.

    Re-run this script any time you want a fresh snapshot of your real library.

.PARAMETER Source
    Source data directory. Defaults to %APPDATA%\Playnite (Playnite's standard location).

.PARAMETER Dest
    Destination data directory. Defaults to %APPDATA%\Playnite-Dev.

.PARAMETER Force
    Skip the confirmation prompt before overwriting an existing destination.

.EXAMPLE
    .\tools\Snapshot-DevLibrary.ps1
    Snapshots %APPDATA%\Playnite into %APPDATA%\Playnite-Dev with confirmation.

.EXAMPLE
    .\tools\Snapshot-DevLibrary.ps1 -Force
    Same as above without prompting.
#>
[CmdletBinding()]
param(
    [string]$Source = "$env:APPDATA\Playnite",
    [string]$Dest   = "$env:APPDATA\Playnite-Dev",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Source)) {
    throw "Source directory not found: $Source"
}

# Refuse to copy while Playnite is running ONLY when sourcing from a real
# Playnite data directory. Custom sources are assumed to be safe (e.g. another
# snapshot on disk). Playnite's LiteDB file is opened with an exclusive lock;
# copying it while it's open produces a corrupt snapshot.
$looksLikeLiveLibrary = (Test-Path (Join-Path $Source 'library')) -and `
                       (Test-Path (Join-Path $Source 'config.json'))
if ($looksLikeLiveLibrary) {
    $running = Get-Process -Name 'Playnite.DesktopApp', 'Playnite.FullscreenApp' -ErrorAction SilentlyContinue
    if ($running) {
        $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
        throw "Playnite is currently running ($names). Close all instances before snapshotting."
    }
}

if (Test-Path $Dest) {
    if (-not $Force) {
        Write-Host "Destination already exists: $Dest" -ForegroundColor Yellow
        $resp = Read-Host "Delete and replace? [y/N]"
        if ($resp -notmatch '^(y|yes)$') {
            Write-Host "Aborted." -ForegroundColor Yellow
            return
        }
    }
    Write-Host "Removing existing snapshot..."
    Remove-Item -Recurse -Force $Dest
}

Write-Host "Snapshotting $Source -> $Dest"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Copy-Item -Recurse -Force $Source $Dest
$sw.Stop()

$size = (Get-ChildItem -Recurse -File $Dest | Measure-Object -Property Length -Sum).Sum
$sizeMb = [Math]::Round($size / 1MB, 1)
Write-Host ("Done in {0:N1}s. Snapshot size: {1} MB" -f $sw.Elapsed.TotalSeconds, $sizeMb) -ForegroundColor Green
Write-Host "Run the dev build with: .\tools\Run-Dev.ps1"
