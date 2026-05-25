#Requires -Version 5.1
<#
.SYNOPSIS
    MONARCH Intelligence Report System - Build Script

.DESCRIPTION
    Compiles src/monarch.py into a single standalone exe using PyInstaller,
    then deploys it to NinjaTrader 8\MONARCH\.

.EXAMPLE
    .\build.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SrcEntry  = Join-Path $PSScriptRoot 'src\monarch.py'
$SrcPaths  = Join-Path $PSScriptRoot 'src'
$DeployDir = Join-Path $env:USERPROFILE 'Documents\NinjaTrader 8\MONARCH'

Write-Host ""
Write-Host " ============================================================" -ForegroundColor Cyan
Write-Host "  MONARCH Intelligence Report System - Build" -ForegroundColor Cyan
Write-Host " ============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Source  : $SrcEntry"
Write-Host "  Deploy  : $DeployDir\MONARCH.exe"
Write-Host ""

# ── Find the Python that owns PyInstaller ─────────────────────────────────────
Write-Host " [CHECK] Locating PyInstaller..." -ForegroundColor Yellow

$pipInfo = pip show pyinstaller 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host " [INFO] PyInstaller not found. Installing..." -ForegroundColor Yellow
    pip install pyinstaller
    if ($LASTEXITCODE -ne 0) {
        Write-Error "pip install pyinstaller failed. Is pip in your PATH?"
    }
    $pipInfo = pip show pyinstaller 2>&1
}

# Parse the Location line to find the site-packages folder
$locationLine = $pipInfo | Where-Object { $_ -match '^Location:' }
if (-not $locationLine) {
    Write-Error "Could not determine PyInstaller location from: $pipInfo"
}
$sitePkgs = ($locationLine -replace '^Location:\s*', '').Trim()
# python.exe is two levels up from site-packages (Lib/site-packages -> Lib -> root)
$pyRoot   = Split-Path (Split-Path $sitePkgs -Parent) -Parent
$pyExe    = Join-Path $pyRoot 'python.exe'

if (-not (Test-Path $pyExe)) {
    Write-Host " [WARN] Could not derive python.exe from pip location ($sitePkgs)." -ForegroundColor Yellow
    Write-Host "        Falling back to 'python' in PATH." -ForegroundColor Yellow
    $pyExe = 'python'
}

$pyVer   = & $pyExe --version 2>&1
$piVer   = ($pipInfo | Where-Object { $_ -match '^Version:' }) -replace '^Version:\s*',''
Write-Host "  Python     : $pyVer  ($pyExe)" -ForegroundColor Green
Write-Host "  PyInstaller: $piVer" -ForegroundColor Green
Write-Host ""

# ── Remove packages known to break PyInstaller ───────────────────────────────
$blocklist = @('enum34')
foreach ($pkg in $blocklist) {
    $ErrorActionPreference = 'Continue'
    $null = & $pyExe -m pip show $pkg 2>&1
    $pkgFound = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = 'Stop'
    if ($pkgFound) {
        Write-Host " [FIX] Removing incompatible package '$pkg'..." -ForegroundColor Yellow
        $ErrorActionPreference = 'Continue'
        & $pyExe -m pip uninstall $pkg -y 2>&1 | Out-Null
        $ErrorActionPreference = 'Stop'
        Write-Host "       Done." -ForegroundColor Green
    }
}

# ── Clean previous build artefacts ────────────────────────────────────────────
Write-Host " [CLEAN] Removing previous build..." -ForegroundColor Yellow
@('build', 'dist', 'MONARCH.spec') | ForEach-Object {
    $p = Join-Path $PSScriptRoot $_
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

# ── Optional icon ─────────────────────────────────────────────────────────────
$IconArg  = @()
$IconFile = Join-Path $PSScriptRoot 'monarch.ico'
if (Test-Path $IconFile) {
    Write-Host "  Icon: $IconFile" -ForegroundColor Green
    $IconArg = @('--icon', $IconFile)
} else {
    Write-Host "  Icon: none  (run: python make_icon.py  to generate monarch.ico)" -ForegroundColor Yellow
}

# ── Generate Windows exe version info ─────────────────────────────────────────
$VerArg      = @()
$VerScript   = Join-Path $PSScriptRoot 'make_version_file.py'
$VerInfoFile = Join-Path $PSScriptRoot 'version_info.txt'
if (Test-Path $VerScript) {
    Write-Host "  Generating version_info.txt..." -ForegroundColor Yellow
    & $pyExe $VerScript
    if ((Test-Path $VerInfoFile) -and ($LASTEXITCODE -eq 0)) {
        Write-Host "  Version info: $VerInfoFile" -ForegroundColor Green
        $VerArg = @('--version-file', $VerInfoFile)
    } else {
        Write-Host "  [warn] make_version_file.py failed - skipping version info" -ForegroundColor Yellow
    }
} else {
    Write-Host "  Version info: none (make_version_file.py not found)" -ForegroundColor Yellow
}
Write-Host ""

# ── Compile ───────────────────────────────────────────────────────────────────
Write-Host " [BUILD] Compiling MONARCH.exe (30-60 seconds)..." -ForegroundColor Yellow
Write-Host ""

& $pyExe -m PyInstaller `
    --onefile `
    --console `
    --name MONARCH `
    --paths $SrcPaths `
    @IconArg `
    @VerArg `
    $SrcEntry

if ($LASTEXITCODE -ne 0) {
    Write-Error "PyInstaller compilation failed (exit code $LASTEXITCODE)."
}

# ── Deploy ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host " [DEPLOY] Copying to $DeployDir\MONARCH.exe..." -ForegroundColor Yellow

if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir | Out-Null
    Write-Host "          Created $DeployDir"
}

Copy-Item (Join-Path $PSScriptRoot 'dist\MONARCH.exe') `
          (Join-Path $DeployDir 'MONARCH.exe') -Force

Write-Host ""
Write-Host " ============================================================" -ForegroundColor Green
Write-Host "  Build complete!" -ForegroundColor Green
Write-Host ""
Write-Host "  Deployed to:"
Write-Host "  $DeployDir\MONARCH.exe" -ForegroundColor White
Write-Host ""
Write-Host "  First run creates:"
Write-Host "    $DeployDir\CastleBravo.html"
Write-Host "    $DeployDir\logs\"
Write-Host "    $DeployDir\reports\"
Write-Host " ============================================================" -ForegroundColor Green
Write-Host ""

$run = Read-Host " Run MONARCH.exe now? [Y/N]"
if ($run -ieq 'Y') {
    Write-Host ""
    & (Join-Path $DeployDir 'MONARCH.exe')
}
