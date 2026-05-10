<#
.SYNOPSIS
    Builds Release | Any CPU of Playnite.DesktopApp and deploys it to an isolated
    portable install, preserving the bundled library and user data.

.DESCRIPTION
    Replaces the binaries in -DeployDir with a freshly built Release drop while
    leaving user-owned files intact (library/, Extensions/, ExtensionsData/,
    Backup/, cache/, browsercache/, JITProfiles/, plus config.json and the
    other portable-mode configs and logs).

    Use -Backup to zip the deployed library/ folder into -BackupDir before
    touching anything (recommended whenever you've been playing in the deployed
    install since the last deploy).

    The script refuses to run while the deployed Playnite is open -- LiteDB
    holds an exclusive lock and overwriting Playnite.dll under it would either
    fail or yield a half-updated install.

.PARAMETER DeployDir
    Existing portable install root. Defaults to %LOCALAPPDATA%\Playnite-Queue.
    Must already contain a Playnite.DesktopApp.exe and config.json -- this
    script updates a deployment, it does not create one from scratch.

.PARAMETER Backup
    Zip DeployDir\library before overwriting any binaries. Skipped by default
    because deploys after a no-data-change session are safe.

.PARAMETER BackupDir
    Where backup zips land. Defaults to %APPDATA%\Playnite-Queue-Backups.
    Created on demand.

.PARAMETER SkipBuild
    Don't run MSBuild; just deploy whatever's already in
    source\Playnite.DesktopApp\bin\Release. Useful when you've already built
    in Visual Studio.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release; Debug is allowed if you want
    a quick sanity check against the deployed library without a full build.

.EXAMPLE
    .\tools\Deploy-Release.ps1
    Build Release and deploy to %LOCALAPPDATA%\Playnite-Queue. No backup.

.EXAMPLE
    .\tools\Deploy-Release.ps1 -Backup
    Same as above but zip the deployed library first.

.EXAMPLE
    .\tools\Deploy-Release.ps1 -SkipBuild
    Deploy whatever's in bin\Release without rebuilding.
#>
[CmdletBinding()]
param(
    [string]$DeployDir = "$env:LOCALAPPDATA\Playnite-Queue",
    [switch]$Backup,
    [string]$BackupDir = "$env:APPDATA\Playnite-Queue-Backups",
    [switch]$SkipBuild,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$sln      = Join-Path $repoRoot 'source\Playnite.sln'
$buildOut = Join-Path $repoRoot ('source\Playnite.DesktopApp\bin\' + $Configuration)

# --- Sanity checks ----------------------------------------------------------

if (-not (Test-Path $DeployDir)) {
    throw "Deploy directory does not exist: $DeployDir`n" +
          "This script updates an existing portable install -- create it first."
}

$deployedExe    = Join-Path $DeployDir 'Playnite.DesktopApp.exe'
$deployedConfig = Join-Path $DeployDir 'config.json'
if (-not (Test-Path $deployedExe) -or -not (Test-Path $deployedConfig)) {
    throw "Deploy directory looks empty / not a Playnite install: $DeployDir`n" +
          "Expected to find Playnite.DesktopApp.exe and config.json."
}

# Refuse to deploy under a running instance. We resolve by exe path so a
# different Playnite install (e.g. the user's real one) doesn't block us.
$resolvedDeploy = (Resolve-Path $DeployDir).Path
$running = Get-Process -Name 'Playnite.DesktopApp', 'Playnite.FullscreenApp' -ErrorAction SilentlyContinue |
    Where-Object {
        try { $_.Path -and ((Resolve-Path $_.Path -ErrorAction Stop).Path).StartsWith($resolvedDeploy, [StringComparison]::OrdinalIgnoreCase) }
        catch { $false }
    }
if ($running) {
    $names = ($running | Select-Object -ExpandProperty ProcessName -Unique) -join ', '
    throw "Playnite is running from the deploy dir ($names). Close it before deploying."
}

# --- Build ------------------------------------------------------------------

if (-not $SkipBuild) {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install Visual Studio 2022 (or pass -SkipBuild and build manually)."
    }
    $vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
    if (-not $vsRoot) {
        throw "No MSBuild-capable Visual Studio installation found."
    }
    $msbuild = Join-Path $vsRoot 'MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path $msbuild)) {
        throw "MSBuild.exe not found at $msbuild"
    }

    Write-Host "Building Playnite.DesktopApp ($Configuration | Any CPU)..." -ForegroundColor Cyan
    # Solution-target syntax (replaces '.' with '_') so we skip broken upstream
    # test projects that aren't needed for the deploy artifact.
    & $msbuild $sln /t:Playnite_DesktopApp /p:Configuration=$Configuration /p:Platform="Any CPU" /v:minimal /nologo /m
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed (msbuild exit code $LASTEXITCODE)."
    }
}

if (-not (Test-Path (Join-Path $buildOut 'Playnite.DesktopApp.exe'))) {
    throw "Build output not found at $buildOut`n" +
          "Drop -SkipBuild or check that Configuration=$Configuration produced an exe."
}

# --- Optional backup --------------------------------------------------------

if ($Backup) {
    $libDir = Join-Path $DeployDir 'library'
    if (-not (Test-Path $libDir)) {
        Write-Warning "No library/ folder under $DeployDir -- skipping backup."
    } else {
        if (-not (Test-Path $BackupDir)) {
            New-Item -ItemType Directory -Path $BackupDir | Out-Null
        }
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $zip   = Join-Path $BackupDir ("playnite-library-$stamp.zip")
        Write-Host "Backing up library -> $zip" -ForegroundColor Cyan
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        # CompressionLevel=Fastest: library/ is mostly an already-compressed
        # LiteDB file plus image cache; 'Optimal' yields ~the same size for
        # ~5x the wall time.
        Compress-Archive -Path $libDir -DestinationPath $zip -CompressionLevel Fastest -Force
        $sw.Stop()
        $sizeMb = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
        Write-Host ("  done in {0:N1}s ({1} MB)" -f $sw.Elapsed.TotalSeconds, $sizeMb) -ForegroundColor Green
    }
}

# --- Deploy -----------------------------------------------------------------

# robocopy without /MIR is purely additive: it copies new/changed files and
# leaves anything in the destination that isn't in the source untouched. We
# still explicitly exclude user-data dirs / portable-config files so a future
# build that happens to ship a file with the same name can't clobber them.
$excludeDirs = @(
    'library',          # LiteDB + image cache
    'Extensions',       # user-installed plugins/themes
    'ExtensionsData',   # plugin state
    'Backup',           # Playnite's own auto-backups
    'cache',            # generic cache
    'browsercache',     # CefSharp profile
    'JITProfiles'       # ngen-style cache
)
$excludeFiles = @(
    'config.json',
    'fullscreenConfig.json',
    'windowPositions.json',
    'Run-PlayniteQueue.ps1',
    'Safe Mode.bat',
    'crash_reporter.cfg',
    'Common.config',
    'gamecontrollerdb.txt',
    '*.log'
)

Write-Host "Deploying $buildOut -> $DeployDir" -ForegroundColor Cyan
$rcArgs = @(
    $buildOut, $DeployDir,
    '/E', '/R:2', '/W:2', '/NFL', '/NDL', '/NP',
    '/XD'
) + $excludeDirs + @('/XF') + $excludeFiles

& robocopy @rcArgs | Out-Host
$rc = $LASTEXITCODE
# robocopy success codes: 0 (nothing to do), 1 (files copied), 2 (extra files
# in dest), 3 (1+2). Anything >=8 is an error.
if ($rc -ge 8) {
    throw "robocopy failed with exit code $rc."
}

# --- Sanity check the deployed binary --------------------------------------

$deployedDll = Join-Path $DeployDir 'Playnite.dll'
if (Test-Path $deployedDll) {
    $stamp = (Get-Item $deployedDll).LastWriteTime
    Write-Host ""
    Write-Host ("Deployed Playnite.dll : {0:yyyy-MM-dd HH:mm:ss}" -f $stamp) -ForegroundColor Green
    Write-Host ("                  exe : {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Item $deployedExe).LastWriteTime) -ForegroundColor Green
}

Write-Host ""
Write-Host "Launch with the desktop shortcut, or:" -ForegroundColor Yellow
Write-Host "  & '$deployedExe'"
