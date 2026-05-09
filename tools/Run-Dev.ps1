<#
.SYNOPSIS
    Launches the Debug build of Playnite.DesktopApp against an isolated dev data directory.

.DESCRIPTION
    Wraps Playnite.DesktopApp.exe with the --userdatadir flag so the dev build reads
    and writes a separate library, leaving the production library untouched.

    Use Snapshot-DevLibrary.ps1 first (or any time you want a fresh copy) to seed the
    dev data directory from your real library.

.PARAMETER DataDir
    User data directory the dev build will use. Defaults to %APPDATA%\Playnite-Dev,
    matching the default of Snapshot-DevLibrary.ps1.

.PARAMETER ExePath
    Path to the built Playnite.DesktopApp.exe. Defaults to the standard csproj
    bin\Debug\ output (where Visual Studio's F5 build and a plain MSBuild of the
    csproj both put it).

.PARAMETER Build
    Build the Debug|Any CPU configuration of the Playnite solution before launching.
    Builds via the .sln (not the .csproj) so per-project platform mappings work.
    Useful for one-shot rebuild-and-launch from the terminal.

.EXAMPLE
    .\tools\Run-Dev.ps1
    Launches the existing Debug build against %APPDATA%\Playnite-Dev.

.EXAMPLE
    .\tools\Run-Dev.ps1 -Build
    Rebuilds Playnite.DesktopApp first, then launches it.
#>
[CmdletBinding()]
param(
    [string]$DataDir = "$env:APPDATA\Playnite-Dev",
    [string]$ExePath,
    [switch]$Build
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $ExePath) {
    $ExePath = Join-Path $repoRoot 'source\Playnite.DesktopApp\bin\Debug\Playnite.DesktopApp.exe'
}

if (-not (Test-Path $DataDir)) {
    Write-Host "Dev data directory does not exist: $DataDir" -ForegroundColor Yellow
    Write-Host "Run .\tools\Snapshot-DevLibrary.ps1 first to seed it from your real library," -ForegroundColor Yellow
    Write-Host "or pass -DataDir <path> to point at an existing location." -ForegroundColor Yellow
    return
}

if ($Build) {
    Write-Host "Building Playnite solution (Debug|Any CPU)..."
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install Visual Studio 2022 (or set -ExePath to a pre-built binary and skip -Build)."
    }
    $vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $vsRoot) {
        throw "No MSBuild-capable Visual Studio installation found."
    }
    $msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) {
        throw "MSBuild.exe not found at $msbuild"
    }
    $sln = Join-Path $repoRoot 'source\Playnite.sln'
    # Build only Playnite.DesktopApp and its transitive dependencies (Playnite, Playnite.SDK).
    # MSBuild's solution-target syntax replaces '.' with '_' in project names.
    # This skips broken upstream test projects (e.g. Playnite.Toolbox.Tests) that aren't
    # needed to launch the app.
    & $msbuild $sln /t:Playnite_DesktopApp /p:Configuration=Debug /p:Platform="Any CPU" /v:minimal /nologo /m
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed (msbuild exit code $LASTEXITCODE)."
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Host "Playnite.DesktopApp.exe not found at: $ExePath" -ForegroundColor Yellow
    Write-Host "Build it first (e.g. open source\Playnite.sln in Visual Studio and build the" -ForegroundColor Yellow
    Write-Host "Playnite.DesktopApp project under Debug|x86), or rerun this script with -Build." -ForegroundColor Yellow
    return
}

$running = Get-Process -Name 'Playnite.DesktopApp' -ErrorAction SilentlyContinue
if ($running) {
    throw "Playnite.DesktopApp is already running. Close it before relaunching."
}

# Safety check: refuse to launch if the dev config.json points DatabasePath at
# anything outside DataDir. Playnite's --userdatadir only redirects where
# config.json is read from; the DatabasePath value inside it still wins. A stale
# DatabasePath would silently send writes to the real production library.
$cfgPath = Join-Path $DataDir 'config.json'
if (Test-Path $cfgPath) {
    try {
        $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
        $rawDb = [string]$cfg.DatabasePath
        if (-not [string]::IsNullOrWhiteSpace($rawDb)) {
            $expanded = [Environment]::ExpandEnvironmentVariables($rawDb)
            $resolvedDb = try { [IO.Path]::GetFullPath($expanded) } catch { $expanded }
            $resolvedData = [IO.Path]::GetFullPath($DataDir)
            if (-not $resolvedDb.StartsWith($resolvedData, [StringComparison]::OrdinalIgnoreCase)) {
                Write-Host ""
                Write-Host "REFUSING TO LAUNCH: dev config.json DatabasePath escapes the dev dir." -ForegroundColor Red
                Write-Host "  config.json: $cfgPath"
                Write-Host "  DatabasePath: $rawDb"
                Write-Host "  resolves to: $resolvedDb"
                Write-Host "  expected under: $resolvedData"
                Write-Host ""
                Write-Host "Re-run .\tools\Snapshot-DevLibrary.ps1 to refresh the snapshot" -ForegroundColor Yellow
                Write-Host "(it now rewrites DatabasePath to the dev library), or fix the path manually." -ForegroundColor Yellow
                return
            }
        }
    } catch {
        Write-Warning "Could not validate dev config.json DatabasePath: $_"
    }
}

Write-Host "Launching $ExePath" -ForegroundColor Green
Write-Host "  --userdatadir $DataDir"
& $ExePath --userdatadir $DataDir
