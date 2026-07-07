#!/usr/bin/env bash
#
# Builds a macOS (Apple Silicon) release of Deusald Localizer and packages it with
# Velopack (https://velopack.io) so existing installs can auto-update in place.
#
# THIS SCRIPT MUST RUN ON A MAC. MacCatalyst compilation needs macOS + Xcode, and
# `vpk pack` for the osx runtime builds a .app bundle that can only be produced on macOS.
# It is the macOS counterpart of scripts/build-release.ps1 (which handles Windows).
#
# Channel strategy (important):
#   The Windows release uses Velopack's DEFAULT channel ("win"), whose artifacts are named
#   without a channel token (e.g. DeusaldLocalizer-1.2.3-full.nupkg, releases.win.json).
#   This build uses an EXPLICIT channel "osx-arm64", which puts that token into EVERY
#   artifact name (DeusaldLocalizer-<ver>-osx-arm64-full.nupkg, releases.osx-arm64.json, ...).
#   The two sets never collide, so Windows + macOS assets can live on the SAME GitHub release,
#   and the Windows channel is left completely untouched. The installed Mac app auto-detects
#   its own channel, so App/Services/UpdateService.cs needs no change.
#
# Signing: this produces an UNSIGNED build. On the target Mac, Gatekeeper will quarantine it;
# clear it once with:  xattr -dr com.apple.quarantine "/Applications/Deusald Localizer.app"
# (or right-click -> Open). Fine for testing auto-update; not for public distribution.
#
# Pipeline:
#   1. dotnet publish  -> MacCatalyst arm64 .app bundle.
#   2. vpk download    -> (only with --upload) pull the previous osx-arm64 release so Velopack
#                         can build a small delta package. First mac release has no delta.
#   3. vpk pack        -> release artifacts under dist/ (full .nupkg, portable .zip, installer
#                         .pkg, releases.osx-arm64.json). Then a SHA256SUMS manifest.
#   4. vpk upload      -> (only with --upload) upload artifacts to a GitHub release for the tag.
#                         Use --merge to ADD to an already-existing release (e.g. v1.2.3).
#
# Usage:
#   ./scripts/build-release-mac.sh                              # pack only, artifacts in dist/
#   ./scripts/build-release-mac.sh --upload --merge             # add mac build to the existing v<Version> release
#   ./scripts/build-release-mac.sh --version 1.2.4 --upload     # new v1.2.4 release (creates it)
#
# Options:
#   --version <x.y.z>     Override version. Default: <Version> from App/App.csproj.
#   --upload              Pack, then upload to GitHub Releases (needs a token).
#   --merge               Add assets to an existing release for the tag instead of failing.
#                         Required when the tag already exists (e.g. adding mac to published v1.2.3).
#   --release-notes <p>   Markdown file to embed / pre-fill the release description.
#   --token <tok>         GitHub PAT (Contents: Read and write). Else uses $GITHUB_TOKEN.
#   --tag <vX.Y.Z>        Release tag. Default: v<version>.
#
set -euo pipefail

# ── Defaults / arg parsing ─────────────────────────────────────────────────────
version=""
upload=false
merge=false
releaseNotes=""
token="${GITHUB_TOKEN:-}"
tag=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)       version="$2"; shift 2 ;;
        --upload)        upload=true; shift ;;
        --merge)         merge=true; shift ;;
        --release-notes) releaseNotes="$2"; shift 2 ;;
        --token)         token="$2"; shift 2 ;;
        --tag)           tag="$2"; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

# ── Constants ──────────────────────────────────────────────────────────────────
packId='DeusaldLocalizer'
packTitle='Deusald Localizer'
authors='Deusald'
repoUrl='https://github.com/Deusald/DeusaldLocalizer'
channel='osx-arm64'
framework='net10.0-maccatalyst'
rid='maccatalyst-arm64'

# ── Guard: must be macOS ───────────────────────────────────────────────────────
if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: this script must run on macOS (MacCatalyst + vpk osx pack require it)." >&2
    exit 1
fi

if [[ -n "$releaseNotes" && ! -f "$releaseNotes" ]]; then
    echo "ERROR: ReleaseNotes file not found: $releaseNotes" >&2
    exit 1
fi

# ── Resolve paths ──────────────────────────────────────────────────────────────
scriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$scriptDir/.." && pwd)"
csproj="$root/App/App.csproj"
publishDir="$root/App/bin/Release/$framework/$rid"
distDir="$root/dist"

[[ -f "$csproj" ]] || { echo "ERROR: Cannot find App.csproj at $csproj" >&2; exit 1; }

# ── Determine version (from App.csproj <Version> unless overridden) ─────────────
if [[ -z "$version" ]]; then
    version="$(grep -oE '<Version>[^<]+</Version>' "$csproj" | head -1 | sed -E 's/<\/?Version>//g' | tr -d '[:space:]')"
    [[ -n "$version" ]] || { echo "ERROR: No <Version> in App.csproj; pass --version." >&2; exit 1; }
fi
[[ -n "$tag" ]] || tag="v$version"
echo "Packaging $packTitle v$version ($rid, self-contained, Velopack, channel=$channel)"

# ── Ensure the Velopack CLI (vpk) is available ─────────────────────────────────
if ! command -v vpk >/dev/null 2>&1; then
    echo "vpk (Velopack CLI) not found - installing as a global dotnet tool..."
    dotnet tool install -g vpk
    export PATH="$PATH:$HOME/.dotnet/tools"
    command -v vpk >/dev/null 2>&1 || { echo "ERROR: vpk installed but not on PATH. Add ~/.dotnet/tools to PATH and re-run." >&2; exit 1; }
fi

# ── Warn on a dirty working tree (embedded commit hash reflects HEAD) ──────────
if [[ -n "$(git -C "$root" status --porcelain 2>/dev/null || true)" ]]; then
    echo "WARNING: Working tree has uncommitted changes. The embedded commit hash reflects HEAD, not these edits."
fi
shortHash="$(git -C "$root" rev-parse --short HEAD 2>/dev/null || true)"
fullHash="$(git -C "$root" rev-parse HEAD 2>/dev/null || true)"
[[ -n "$shortHash" ]] && echo "Commit: $shortHash"

# ── Publish (MacCatalyst arm64 .app, unsigned) ─────────────────────────────────
# -p:CreatePackage=false makes the build emit a plain .app bundle (not a signed .pkg),
# which is what Velopack consumes via --packDir. Ad-hoc signing keeps it locally runnable
# without an Apple Developer certificate.
rm -rf "$publishDir"
dotnet clean "$csproj" -f "$framework" -c Release
dotnet publish "$csproj" \
    -f "$framework" -c Release \
    -p:RuntimeIdentifier="$rid" \
    -p:CreatePackage=false

# Auto-discover the produced .app bundle and its main executable, rather than hardcoding
# names that MAUI derives from ApplicationTitle/AssemblyName.
app="$(find "$publishDir" -maxdepth 1 -name '*.app' -type d | head -1)"
[[ -n "$app" ]] || { echo "ERROR: No .app bundle found under $publishDir" >&2; exit 1; }
mainExe="$(ls "$app/Contents/MacOS" | head -1)"
[[ -n "$mainExe" ]] || mainExe="$packId"
echo "App bundle: $app"
echo "Main exe:   $mainExe"

mkdir -p "$distDir"

# Optional release notes → embedded in the package and used as the GitHub release description.
notesArgs=()
[[ -n "$releaseNotes" ]] && notesArgs=(--releaseNotes "$(cd "$(dirname "$releaseNotes")" && pwd)/$(basename "$releaseNotes")")

# ── Fetch the previous osx-arm64 release so Velopack can build a delta (best-effort) ──
if $upload; then
    echo "Fetching previous osx-arm64 release from GitHub (for delta generation)..."
    dlArgs=(download github --repoUrl "$repoUrl" --channel "$channel" --outputDir "$distDir")
    [[ -n "$token" ]] && dlArgs+=(--token "$token")
    vpk "${dlArgs[@]}" || echo "WARNING: No previous osx-arm64 release downloaded (first mac release, or download failed). Building a full-only package."
fi

# ── Pack ───────────────────────────────────────────────────────────────────────
echo "Packing with Velopack..."
vpk pack \
    --packId      "$packId" \
    --packVersion "$version" \
    --packDir     "$app" \
    --mainExe     "$mainExe" \
    --packTitle   "$packTitle" \
    --packAuthors "$authors" \
    --channel     "$channel" \
    --outputDir   "$distDir" \
    "${notesArgs[@]}"

# ── Checksums (sha256sum format over the user-facing osx artifacts) ────────────
# Mirrors build-release.ps1: emit a manifest so anyone can verify a hand-downloaded file
# with `shasum -a 256 -c`. Glob whatever this channel produced (portable .zip, installer
# .pkg, full .nupkg) rather than hardcoding names.
checksumsFile="$distDir/$packId-$channel-SHA256SUMS.txt"
: > "$checksumsFile"
while IFS= read -r f; do
    ( cd "$distDir" && shasum -a 256 "$(basename "$f")" ) >> "$checksumsFile"
done < <(find "$distDir" -maxdepth 1 -type f \
            \( -name "*$channel-Portable.zip" -o -name "*$channel*.pkg" -o -name "$packId-$version-$channel-full.nupkg" \) | sort)
echo "Wrote checksums -> $(basename "$checksumsFile")"

# ── Optional upload to GitHub Releases ─────────────────────────────────────────
if $upload; then
    [[ -n "$token" ]] || { echo "ERROR: --upload requires a token. Pass --token or set GITHUB_TOKEN." >&2; exit 1; }
    echo "Uploading osx-arm64 artifacts to GitHub release $tag..."
    upArgs=(upload github
        --repoUrl     "$repoUrl"
        --token       "$token"
        --outputDir   "$distDir"
        --channel     "$channel"
        --tag         "$tag"
        --releaseName "$packTitle $version")
    # --merge adds to an existing release (required for a published tag like v1.2.3).
    $merge && upArgs+=(--merge)
    # Pin a brand-new tag to this commit. Ignored when the tag already exists (merge).
    [[ -n "$fullHash" ]] && ! $merge && upArgs+=(--targetCommitish "$fullHash")
    vpk "${upArgs[@]}"

    # Attach the checksums manifest to the same release (vpk only uploads its own artifacts).
    if command -v gh >/dev/null 2>&1; then
        echo "Attaching checksums via gh..."
        GH_TOKEN="$token" gh release upload "$tag" "$checksumsFile" --clobber --repo "Deusald/DeusaldLocalizer" \
            || echo "WARNING: gh checksum upload failed; attach $checksumsFile to the $tag release by hand."
    else
        echo "NOTE: 'gh' not found - attach $(basename "$checksumsFile") to the $tag release manually if you want it published."
    fi
fi

# ── Summary ────────────────────────────────────────────────────────────────────
echo ""
echo "Velopack macOS artifacts ready in dist/:"
find "$distDir" -maxdepth 1 -type f -name "*$channel*" -exec basename {} \; | sort | sed 's/^/  /'
echo ""
if $upload; then
    echo "Uploaded to the GitHub release $tag."
    echo "  - If it was a DRAFT created by vpk, open the release, review, and Publish it."
    echo "  - On the test Mac, clear quarantine after install:"
    echo "      xattr -dr com.apple.quarantine \"/Applications/$packTitle.app\""
else
    echo "Local pack only (not uploaded). Re-run with --upload (add --merge for an existing tag)."
fi
