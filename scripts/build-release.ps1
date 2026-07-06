<#
.SYNOPSIS
    Builds a self-contained Windows release of Deusald Localizer, zips it, and
    generates a SHA-256 checksum ready to upload to a GitHub Release.

.DESCRIPTION
    Run from anywhere; paths are resolved relative to the script location.
    Produces, under dist/:
        DeusaldLocalizer-v<version>-win-x64.zip
        DeusaldLocalizer-v<version>-win-x64.zip.sha256

    The version is read from App/App.csproj (<Version>) unless -Version is passed.
    The build embeds the current short git commit hash into the app
    (shown on the welcome screen as "v<version> · <hash>").

.PARAMETER Version
    Override the version string (e.g. "1.0.1"). Defaults to <Version> in App.csproj.

.PARAMETER Tag
    Also create and push an annotated git tag "v<version>" for the current commit.

.EXAMPLE
    ./scripts/build-release.ps1
    ./scripts/build-release.ps1 -Version 1.0.1 -Tag
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Tag
)

$ErrorActionPreference = 'Stop'

# ── Resolve paths ────────────────────────────────────────────────────────────
$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root       = Split-Path -Parent $scriptDir
$csproj     = Join-Path $root 'App\App.csproj'
$framework  = 'net10.0-windows10.0.19041.0'
$rid        = 'win-x64'
$publishDir = Join-Path $root "App\bin\Release\$framework\$rid\publish"
$distDir    = Join-Path $root 'dist'

if (-not (Test-Path $csproj)) { throw "Cannot find App.csproj at $csproj" }

# ── Determine version ────────────────────────────────────────────────────────
if (-not $Version) {
    $csprojText = Get-Content $csproj -Raw
    if ($csprojText -match '<Version>(.*?)</Version>') { $Version = $Matches[1].Trim() }
    else { throw "No <Version> in App.csproj; pass -Version explicitly." }
}
Write-Host "Building Deusald Localizer v$Version ($rid, self-contained)" -ForegroundColor Cyan

# ── Warn on a dirty / detached working tree (hash embed reflects HEAD) ────────
$gitStatus = git -C $root status --porcelain 2>$null
if ($LASTEXITCODE -eq 0 -and $gitStatus) {
    Write-Warning "Working tree has uncommitted changes. The embedded commit hash reflects HEAD, not these edits."
}
$shortHash = (git -C $root rev-parse --short HEAD 2>$null)
if ($LASTEXITCODE -eq 0) { Write-Host "Commit: $shortHash" -ForegroundColor DarkGray }

# ── Publish ──────────────────────────────────────────────────────────────────
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj `
    -f $framework -c Release `
    -p:WindowsPackageType=None -p:SelfContained=true -p:RuntimeIdentifier=$rid
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $publishDir 'DeusaldLocalizer.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe was not produced." }

# ── Zip ──────────────────────────────────────────────────────────────────────
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipName = "DeusaldLocalizer-v$Version-win-x64.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipMb = "{0:N1}" -f ((Get-Item $zipPath).Length / 1MB)

# ── Checksum ─────────────────────────────────────────────────────────────────
$hash = (Get-FileHash -Algorithm SHA256 $zipPath).Hash.ToLower()
$shaPath = "$zipPath.sha256"
"$hash  $zipName" | Out-File -FilePath $shaPath -Encoding ascii -NoNewline

# ── Optional tag ─────────────────────────────────────────────────────────────
if ($Tag) {
    Write-Host "Tagging v$Version..." -ForegroundColor Cyan
    git -C $root tag -a "v$Version" -m "DeusaldLocalizer v$Version"
    if ($LASTEXITCODE -ne 0) { throw "git tag failed (does v$Version already exist?)." }
    git -C $root push origin "v$Version"
    if ($LASTEXITCODE -ne 0) { throw "git push tag failed." }
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Release artifacts ready ($zipMb MB):" -ForegroundColor Green
Write-Host "  $zipPath"
Write-Host "  $shaPath"
Write-Host ""
Write-Host "SHA-256: $hash" -ForegroundColor Yellow
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
if (-not $Tag) {
    Write-Host "  1. Tag the release commit:  git tag -a v$Version -m `"DeusaldLocalizer v$Version`"  &&  git push origin v$Version"
    Write-Host "     (or re-run this script with -Tag)"
}
Write-Host "  2. Open https://github.com/Deusald/DeusaldLocalizer/releases/new?tag=v$Version"
Write-Host "  3. Attach both files above, add notes, and Publish."
