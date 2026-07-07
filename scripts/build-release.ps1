<#
.SYNOPSIS
    Builds a Windows release of Deusald Localizer and packages it with Velopack
    (https://velopack.io) so existing installs can auto-update in place.

.DESCRIPTION
    Run from anywhere; paths are resolved relative to the script location.

    Pipeline:
        1. dotnet publish  — self-contained win-x64 build into App\bin\...\publish.
        2. vpk download    — pull the previous release from GitHub so Velopack can
                             build a small delta package (skipped/optional first time).
        3. vpk pack        — produce the release artifacts under dist\:
                                 DeusaldLocalizer-<version>-full.nupkg
                                 DeusaldLocalizer-<version>-delta.nupkg   (if a prior release existed)
                                 DeusaldLocalizer-win-Setup.exe           (the installer users download)
                                 DeusaldLocalizer-win-Portable.zip
                                 releases.win.json                        (the update feed clients read)
                             then compute DeusaldLocalizer-win-SHA256SUMS.txt over the two
                             user-facing downloads so people can verify what they grabbed.
        4. vpk upload      — (only with -Upload) create a GitHub DRAFT release and upload
                             the artifacts to it, then attach the SHA256SUMS.txt to the same
                             draft via the GitHub API. The script never publishes — you review
                             the draft, edit its description, and click Publish yourself.

    The version is read from App/App.csproj (<Version>) unless -Version is passed, and is
    used verbatim as the Velopack package version. Keep <Version> in App.csproj in sync.

    The build still embeds the current short git commit hash into the app
    (shown on the welcome screen as "v<version> · <hash>").

.PARAMETER Version
    Override the version string (e.g. "1.1.3"). Defaults to <Version> in App.csproj.

.PARAMETER Upload
    After packing, run `vpk upload github` to create a GitHub DRAFT release (tag v<version>)
    and upload all artifacts to it. The draft is visible only to you — review it, edit the
    description, and publish it manually. The script never publishes on its own. Needs a token.

.PARAMETER ReleaseNotes
    Optional path to a markdown file whose contents pre-fill the draft release's description.
    If omitted, the description starts empty and you type it into the GitHub draft before publishing.

.PARAMETER Token
    GitHub personal access token (fine-grained, Contents: Read and write) used for -Upload /
    delta download. Resolution order: this parameter, then the GITHUB_TOKEN environment variable,
    then the 1Password item (via the `op` CLI) referenced by $opTokenRef below — pulled only when
    -Upload is set, so a local-only pack never prompts 1Password. Public-repo delta download works
    without a token but is rate-limited.

.EXAMPLE
    ./scripts/build-release.ps1                       # pack only, artifacts in dist\
    ./scripts/build-release.ps1 -Upload               # pack + upload to a GitHub draft you publish
    ./scripts/build-release.ps1 -Version 1.1.3 -Upload -ReleaseNotes notes.md
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Upload,
    [string] $ReleaseNotes,
    [string] $Token
)

$ErrorActionPreference = 'Stop'

# ── Constants ────────────────────────────────────────────────────────────────
$packId     = 'DeusaldLocalizer'
$packTitle  = 'Deusald Localizer'
$authors    = 'Deusald'
$mainExe    = 'DeusaldLocalizer.exe'
$repoUrl    = 'https://github.com/Deusald/DeusaldLocalizer'
$opTokenRef = 'op://Private/GitHub Deusald Localizer Token/credential'  # 1Password secret reference

# Owner/repo parsed from $repoUrl, used for the GitHub REST calls that attach the checksums file.
$null = $repoUrl -match 'github\.com/([^/]+)/([^/]+?)(?:\.git)?/?$'
$repoOwner = $Matches[1]
$repoName  = $Matches[2]

# ── Resolve the GitHub token: -Token > $env:GITHUB_TOKEN > 1Password (only when uploading) ────
function Resolve-GitHubToken {
    param([string] $Explicit, [string] $OpRef, [bool] $NeedFromVault)

    if ($Explicit)         { return $Explicit }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    # Don't reach for 1Password (and trigger its approval prompt) unless a token is actually required.
    if (-not $NeedFromVault) { return $null }

    if (-not (Get-Command op -ErrorAction SilentlyContinue)) {
        Write-Warning "1Password CLI 'op' not found; cannot read $OpRef. Pass -Token or set GITHUB_TOKEN."
        return $null
    }
    Write-Host "Reading GitHub token from 1Password ($OpRef)..." -ForegroundColor Cyan
    # Capture without .Trim() first: a failed `op read` prints to stderr and yields $null on stdout,
    # so guard against null before trimming (otherwise it throws under ErrorActionPreference=Stop).
    $tok = op read $OpRef
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($tok)) { return $tok.Trim() }
    Write-Warning "1Password read failed (signed in? run 'op signin'). Continuing without a token."
    return $null
}
$Token = Resolve-GitHubToken -Explicit $Token -OpRef $opTokenRef -NeedFromVault $Upload

if ($ReleaseNotes -and -not (Test-Path $ReleaseNotes)) {
    throw "ReleaseNotes file not found: $ReleaseNotes"
}

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
Write-Host "Packaging $packTitle v$Version ($rid, self-contained, Velopack)" -ForegroundColor Cyan

# ── Ensure the Velopack CLI (vpk) is available ───────────────────────────────
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Host "vpk (Velopack CLI) not found - installing as a global dotnet tool..." -ForegroundColor Yellow
    dotnet tool install -g vpk
    if ($LASTEXITCODE -ne 0) { throw "Failed to install vpk. Install manually: dotnet tool install -g vpk" }
    if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
        throw "vpk installed but not on PATH. Open a new shell (or add %USERPROFILE%\.dotnet\tools to PATH) and re-run."
    }
}

# ── Warn on a dirty / detached working tree (hash embed reflects HEAD) ────────
$gitStatus = git -C $root status --porcelain 2>$null
if ($LASTEXITCODE -eq 0 -and $gitStatus) {
    Write-Warning "Working tree has uncommitted changes. The embedded commit hash reflects HEAD, not these edits."
}
$shortHash = (git -C $root rev-parse --short HEAD 2>$null)
$fullHash  = (git -C $root rev-parse HEAD 2>$null)
if ($LASTEXITCODE -eq 0) { Write-Host "Commit: $shortHash" -ForegroundColor DarkGray }

# ── Publish ──────────────────────────────────────────────────────────────────
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet clean $csproj -f $framework -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed (exit $LASTEXITCODE)." }
dotnet publish $csproj `
    -f $framework -c Release `
    -p:WindowsPackageType=None -p:SelfContained=true -p:RuntimeIdentifier=$rid
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$exe = Join-Path $publishDir $mainExe
if (-not (Test-Path $exe)) { throw "Expected $exe was not produced." }

# MAUI emits an .ico into the publish folder; use it for the installer/shortcuts if present.
$icon = Join-Path $publishDir 'appicon.ico'
$iconArgs = if (Test-Path $icon) { @('--icon', $icon) } else { @() }

# Optional release notes → embedded in the package and used as the GitHub draft's description.
$notesArgs = if ($ReleaseNotes) { @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) } else { @() }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# ── Fetch the previous release so Velopack can build a delta (best-effort) ────
# Only when uploading: the delta base must be the last *published* release. For local packs we
# skip GitHub entirely and let vpk build the delta against whatever is already in dist\ (e.g. the
# previous version you packed while testing). On the first ever release there is simply no delta.
if ($Upload) {
    Write-Host "Fetching previous release from GitHub (for delta generation)..." -ForegroundColor Cyan
    $dlArgs = @('download', 'github', '--repoUrl', $repoUrl, '--outputDir', $distDir)
    if ($Token) { $dlArgs += @('--token', $Token) }
    vpk @dlArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "No previous release downloaded (first release, or download failed). Building a full-only package."
    }
}

# ── Pack ─────────────────────────────────────────────────────────────────────
Write-Host "Packing with Velopack..." -ForegroundColor Cyan
vpk pack `
    --packId      $packId `
    --packVersion $Version `
    --packDir     $publishDir `
    --mainExe     $mainExe `
    --packTitle   $packTitle `
    --packAuthors $authors `
    --outputDir   $distDir `
    @iconArgs @notesArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)." }

$setup = Join-Path $distDir "$packId-win-Setup.exe"

# ── Checksums ────────────────────────────────────────────────────────────────
# Velopack ships SHA hashes inside releases.win.json for its own auto-updater, but nothing a human
# can use to verify the files they download by hand. Emit a sha256sum-format manifest over the
# user-facing artifacts so anyone can confirm a download with `Get-FileHash` / `sha256sum -c`.
$checksumsFile = Join-Path $distDir "$packId-win-SHA256SUMS.txt"
$hashTargets   = @(
    (Join-Path $distDir "$packId-win-Setup.exe"),
    (Join-Path $distDir "$packId-win-Portable.zip"),
    (Join-Path $distDir "$packId-$Version-full.nupkg")
) | Where-Object { Test-Path $_ }

$checksumLines = $hashTargets | ForEach-Object {
    $hash = (Get-FileHash -Path $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    # Two spaces + bare filename = the canonical `sha256sum` format users can feed straight to `-c`.
    "$hash  $(Split-Path $_ -Leaf)"
}
Set-Content -Path $checksumsFile -Value $checksumLines -Encoding ascii
Write-Host "Wrote checksums -> $(Split-Path $checksumsFile -Leaf)" -ForegroundColor DarkGray

# ── Attach the checksums file to the (draft) GitHub release ───────────────────
# vpk only uploads its own artifacts, so the manifest has to be pushed to the same draft by hand
# via the REST API. Drafts have no real git tag yet, so we match the draft by its tag_name field.
function Publish-ChecksumsAsset {
    param([string] $Owner, [string] $Repo, [string] $Tag, [string] $Path, [string] $Tok)

    $headers = @{
        Authorization = "Bearer $Tok"
        Accept        = 'application/vnd.github+json'
        'User-Agent'  = 'build-release.ps1'
    }
    $releases = Invoke-RestMethod -Method Get -Headers $headers `
        -Uri "https://api.github.com/repos/$Owner/$Repo/releases?per_page=100"
    $release = $releases | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
    if (-not $release) { throw "Could not find a release for tag $Tag to attach checksums to." }

    $name = Split-Path $Path -Leaf
    # Replace any asset of the same name left over from a re-run so uploads stay idempotent.
    $existing = $release.assets | Where-Object { $_.name -eq $name }
    foreach ($asset in $existing) {
        Invoke-RestMethod -Method Delete -Headers $headers `
            -Uri "https://api.github.com/repos/$Owner/$Repo/releases/assets/$($asset.id)" | Out-Null
    }

    $uploadUrl = ($release.upload_url -replace '\{\?[^}]*\}', '') + "?name=$name"
    $uploadHeaders = $headers.Clone()
    $uploadHeaders['Content-Type'] = 'text/plain'
    Invoke-RestMethod -Method Post -Headers $uploadHeaders -Uri $uploadUrl -InFile $Path | Out-Null
}

# ── Optional upload to GitHub Releases ───────────────────────────────────────
if ($Upload) {
    if (-not $Token) { throw "-Upload requires a token. Pass -Token, set GITHUB_TOKEN, or sign in to 1Password (op signin)." }
    Write-Host "Creating GitHub DRAFT release (tag v$Version) and uploading artifacts..." -ForegroundColor Cyan
    # No --publish: vpk always leaves the release as a draft, which we never convert here.
    $upArgs = @(
        'upload', 'github',
        '--repoUrl',     $repoUrl,
        '--token',       $Token,
        '--outputDir',   $distDir,
        '--tag',         "v$Version",
        '--releaseName', "$packTitle v$Version"
    )
    if ($fullHash) { $upArgs += @('--targetCommitish', $fullHash) }  # pin the tag to this exact commit (created on publish)
    vpk @upArgs
    if ($LASTEXITCODE -ne 0) { throw "vpk upload failed (exit $LASTEXITCODE)." }

    Write-Host "Attaching checksums to the draft release..." -ForegroundColor Cyan
    Publish-ChecksumsAsset -Owner $repoOwner -Repo $repoName -Tag "v$Version" -Path $checksumsFile -Tok $Token
}

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Velopack artifacts ready in dist\:" -ForegroundColor Green
Get-ChildItem $distDir -File | Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-30) } |
    Sort-Object Name | ForEach-Object { Write-Host ("  {0,-45} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB)) }
Write-Host ""

if ($Upload) {
    Write-Host "Uploaded to a GitHub DRAFT release (visible only to you)." -ForegroundColor Yellow
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  1. Open https://github.com/Deusald/DeusaldLocalizer/releases  and open the 'v$Version' draft."
    Write-Host "  2. Edit the description, then click Publish (keep it a non-prerelease)."
    Write-Host "     Publishing creates the git tag v$Version at the built commit and lets clients auto-update."
}
else {
    Write-Host "Local pack only (not uploaded)." -ForegroundColor Yellow
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "  - Test the installer:  $setup"
    Write-Host "  - Re-run with -Upload to create a GitHub draft you can review and publish."
}
