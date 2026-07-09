#!/usr/bin/env bash
#
# Builds a macOS (Apple Silicon) release of Deusald Localizer as a distributable .app, zips it, and
# writes a SHA256 checksum next to it. Upload both files to a GitHub release by hand.
#
# macOS does NOT auto-update in place (Velopack's in-place update is unsupported under Mac Catalyst).
# Instead the app checks GitHub for a newer release and, when one exists, offers a "Download" button
# that opens the releases page so the user can grab the new build manually. So all this script needs
# to produce is the .app archive + its checksum. It is the macOS counterpart of build-release.ps1
# (which still does full Velopack auto-update packaging for Windows).
#
# THIS SCRIPT MUST RUN ON A MAC (MacCatalyst compilation needs macOS + Xcode).
#
# Entitlements: the app is published with scripts/mac-entitlements.plist (hardened-runtime allowances,
# NO App Sandbox) instead of App/Platforms/MacCatalyst/Entitlements.plist (which enables the sandbox
# for the Mac App Store). Direct-download builds should not be sandboxed.
#
# Signing: this produces an UNSIGNED (ad-hoc) build. Gatekeeper will quarantine it on other Macs; the
# recipient clears it once with:
#     xattr -dr com.apple.quarantine "/Applications/Deusald Localizer.app"   (or right-click -> Open).
# For public distribution, sign + notarize the .app before zipping (Developer ID + `xcrun notarytool`
# + `xcrun stapler staple`); that is intentionally left out of this script.
#
# Usage:
#   ./scripts/build-release-mac.sh                 # build + zip + checksum into dist/
#   ./scripts/build-release-mac.sh --version 1.2.4 # override the version (default: <Version> in csproj)
#
set -euo pipefail

# ── Args ───────────────────────────────────────────────────────────────────────
version=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

# ── Constants ──────────────────────────────────────────────────────────────────
packId='DeusaldLocalizer'
framework='net10.0-maccatalyst'
rid='maccatalyst-arm64'

# ── Guard: must be macOS ───────────────────────────────────────────────────────
if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: this script must run on macOS (MacCatalyst compilation requires it)." >&2
    exit 1
fi

# ── Resolve paths ──────────────────────────────────────────────────────────────
scriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$scriptDir/.." && pwd)"
csproj="$root/App/App.csproj"
publishDir="$root/App/bin/Release/$framework/$rid"
distDir="$root/dist"
entitlements="$scriptDir/mac-entitlements.plist"

[[ -f "$csproj" ]]       || { echo "ERROR: Cannot find App.csproj at $csproj" >&2; exit 1; }
[[ -f "$entitlements" ]] || { echo "ERROR: entitlements file missing: $entitlements" >&2; exit 1; }

# ── Determine version (from App.csproj <Version> unless overridden) ─────────────
if [[ -z "$version" ]]; then
    version="$(grep -oE '<Version>[^<]+</Version>' "$csproj" | head -1 | sed -E 's/<\/?Version>//g' | tr -d '[:space:]')"
    [[ -n "$version" ]] || { echo "ERROR: No <Version> in App.csproj; pass --version." >&2; exit 1; }
fi
echo "Building $packId v$version ($rid)"

# ── Warn on a dirty working tree (embedded commit hash reflects HEAD) ──────────
if [[ -n "$(git -C "$root" status --porcelain 2>/dev/null || true)" ]]; then
    echo "WARNING: Working tree has uncommitted changes. The embedded commit hash reflects HEAD, not these edits."
fi
shortHash="$(git -C "$root" rev-parse --short HEAD 2>/dev/null || true)"
[[ -n "$shortHash" ]] && echo "Commit: $shortHash"

# ── Publish (MacCatalyst arm64 .app, ad-hoc signed, non-sandbox entitlements) ──
rm -rf "$publishDir"
dotnet clean "$csproj" -f "$framework" -c Release
dotnet publish "$csproj" \
    -f "$framework" -c Release \
    -p:RuntimeIdentifier="$rid" \
    -p:CreatePackage=false \
    -p:CodesignEntitlements="$entitlements"

# Auto-discover the produced .app bundle (its name comes from ApplicationTitle).
app="$(find "$publishDir" -maxdepth 1 -name '*.app' -type d | head -1)"
[[ -n "$app" ]] || { echo "ERROR: No .app bundle found under $publishDir" >&2; exit 1; }
echo "App bundle: $app"

# ── Archive + checksum ─────────────────────────────────────────────────────────
mkdir -p "$distDir"
zipName="$packId-$rid-$version.zip"
zipPath="$distDir/$zipName"
rm -f "$zipPath"

# `ditto` preserves the bundle's symlinks, resource forks and code signature (plain `zip` can corrupt
# a .app), producing an archive that unzips back to a runnable Deusald Localizer.app.
echo "Zipping -> $zipName"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zipPath"

# SHA256 in `shasum -a 256 -c`-compatible format so anyone can verify the download.
( cd "$distDir" && shasum -a 256 "$zipName" > "$zipName.sha256" )
echo "Wrote checksum -> $zipName.sha256"

# ── Summary ────────────────────────────────────────────────────────────────────
echo ""
echo "Done. Upload these two files to the GitHub release by hand:"
echo "  $distDir/$zipName"
echo "  $distDir/$zipName.sha256"
echo ""
echo "NOTE: this build is UNSIGNED. On the target Mac, clear quarantine after unzipping:"
echo "  xattr -dr com.apple.quarantine \"/path/to/Deusald Localizer.app\"   (or right-click -> Open)"
