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
# Signing: by default this produces an UNSIGNED (ad-hoc) build. Gatekeeper will quarantine it on other
# Macs; the recipient clears it once with:
#     xattr -dr com.apple.quarantine "/Applications/Deusald Localizer.app"   (or right-click -> Open).
# For public distribution, sign + notarize the .app before zipping (Developer ID + `xcrun notarytool`
# + `xcrun stapler staple`); that is intentionally left out of this script.
#
# Self-signed signing (--sign-identity / MAC_SIGN_IDENTITY): signs the .app with a stable, self-signed
# code-signing identity and the keychain-access-group entitlements (scripts/mac-entitlements-signed.plist).
# This does NOT make Gatekeeper happy on other Macs (recipients still clear quarantine), but it gives the
# app a stable code signature, which is what MAUI SecureStorage (the macOS Keychain) needs to persist the
# signed-in user — an ad-hoc build fails with errSecMissingEntitlement (-34018). Create the identity once
# with scripts/create-mac-signing-cert.sh.
#
# Usage:
#   ./scripts/build-release-mac.sh                                  # build + zip + checksum into dist/ (ad-hoc)
#   ./scripts/build-release-mac.sh --version 1.2.4                  # override the version (default: <Version> in csproj)
#   ./scripts/build-release-mac.sh --sign-identity "Deusald Localizer Self-Signed"   # self-signed build
#   MAC_SIGN_IDENTITY="Deusald Localizer Self-Signed" ./scripts/build-release-mac.sh # same, via env var
#
set -euo pipefail

# ── Args ───────────────────────────────────────────────────────────────────────
version=""
signIdentity="${MAC_SIGN_IDENTITY:-}"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        --sign-identity) signIdentity="$2"; shift 2 ;;
        --sign) signIdentity="Deusald Localizer Self-Signed"; shift ;;
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

# Signed builds carry a keychain access group (so SecureStorage works); ad-hoc builds do not.
if [[ -n "$signIdentity" ]]; then
    entitlements="$scriptDir/mac-entitlements-signed.plist"
else
    entitlements="$scriptDir/mac-entitlements.plist"
fi

[[ -f "$csproj" ]]       || { echo "ERROR: Cannot find App.csproj at $csproj" >&2; exit 1; }
[[ -f "$entitlements" ]] || { echo "ERROR: entitlements file missing: $entitlements" >&2; exit 1; }

# ── Guard: signing identity must exist before we spend time building ────────────
if [[ -n "$signIdentity" ]]; then
    if ! security find-identity -v -p codesigning 2>/dev/null | grep -qF "$signIdentity"; then
        echo "ERROR: code-signing identity \"$signIdentity\" not found in any keychain." >&2
        echo "       Create it once with: ./scripts/create-mac-signing-cert.sh \"$signIdentity\"" >&2
        exit 1
    fi
    echo "Signing identity: $signIdentity"
fi

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

# ── Re-sign with the self-signed identity (replaces the ad-hoc signature) ──────
# The publish above ad-hoc signs the bundle; re-sign it with the real identity so it carries a stable
# code signature plus the keychain-access-group entitlements (SecureStorage). --deep re-signs the nested
# dylibs/frameworks with the same identity; entitlements apply only to the main executable. No secure
# timestamp (Apple TSA) — self-signed builds don't notarize and the script runs offline.
if [[ -n "$signIdentity" ]]; then
    echo "Signing bundle with \"$signIdentity\"…"
    codesign --force --deep --options runtime --timestamp=none \
        --entitlements "$entitlements" \
        --sign "$signIdentity" "$app"
    echo "Verifying signature…"
    codesign --verify --deep --strict --verbose=2 "$app"
    codesign --display --entitlements - "$app" | grep -q 'keychain-access-groups' \
        && echo "keychain-access-group present — SecureStorage should work." \
        || echo "WARNING: keychain-access-group not found in the signed entitlements."
fi

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
if [[ -n "$signIdentity" ]]; then
    echo "NOTE: this build is SELF-SIGNED (\"$signIdentity\"), NOT notarized. SecureStorage/Keychain works,"
    echo "      but Gatekeeper still quarantines it on other Macs. On the target Mac, clear quarantine:"
else
    echo "NOTE: this build is UNSIGNED (ad-hoc). SecureStorage/Keychain will NOT persist the signed-in user"
    echo "      (the app falls back to plaintext Preferences). Sign with --sign-identity to fix that."
    echo "      On the target Mac, clear quarantine after unzipping:"
fi
echo "  xattr -dr com.apple.quarantine \"/path/to/Deusald Localizer.app\"   (or right-click -> Open)"
