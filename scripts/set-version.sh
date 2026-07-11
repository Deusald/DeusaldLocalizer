#!/usr/bin/env bash
#
# Bumps the product version everywhere it lives, in one shot, so no project is ever missed.
#
# The version "1.4.5" is duplicated across three .csproj files (App, WebApp, Common) as <Version>,
# and App also mirrors it in <ApplicationDisplayVersion> and in <ApplicationVersion> (MAUI's integer
# build number). <ApplicationVersion> IS the patch component: bumping it and the version go together.
#
# You pass just the MAJOR.MINOR prefix. The script increments <ApplicationVersion> by one, and sets
# the full version everywhere to MAJOR.MINOR.<newApplicationVersion>. So with ApplicationVersion 5,
# `set-version.sh 1.4` -> ApplicationVersion 6 and version 1.4.6 in every project.
#
# Usage:
#   ./scripts/set-version.sh            # print the current version, change nothing
#   ./scripts/set-version.sh 1.4        # ++ApplicationVersion, set version to 1.4.<that> everywhere
#
set -euo pipefail

# ── Args ───────────────────────────────────────────────────────────────────────
prefix=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        -*) echo "Unknown option: $1" >&2; exit 2 ;;
        *)
            [[ -z "$prefix" ]] || { echo "ERROR: version prefix already given ('$prefix'); unexpected '$1'." >&2; exit 2; }
            prefix="$1"; shift ;;
    esac
done

# ── Resolve paths ──────────────────────────────────────────────────────────────
scriptDir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$scriptDir/.." && pwd)"

appCsproj="$root/App/App.csproj"
webAppCsproj="$root/WebApp/WebApp.csproj"
commonCsproj="$root/Common/Common.csproj"

# Every file that carries the shared <Version> tag. Add new projects here as they gain a version.
versionFiles=("$appCsproj" "$webAppCsproj" "$commonCsproj")

for f in "${versionFiles[@]}" "$appCsproj"; do
    [[ -f "$f" ]] || { echo "ERROR: missing csproj: $f" >&2; exit 1; }
done

# ── Small helpers ──────────────────────────────────────────────────────────────
# Read the (first) inner text of a tag from a file.
readTag() { grep -oE "<$1>[^<]+</$1>" "$2" | head -1 | sed -E "s/<\/?$1>//g" | tr -d '[:space:]'; }

# Replace every <tag>...</tag> in a file (preserving the file's indentation) with a new value.
setTag() {
    local tag="$1" value="$2" file="$3"
    grep -q "<$tag>" "$file" || { echo "ERROR: no <$tag> in $file" >&2; exit 1; }
    sed -i -E "s#<$tag>[^<]*</$tag>#<$tag>$value</$tag>#g" "$file"
}

# ── Report current state ───────────────────────────────────────────────────────
currentVersion="$(readTag Version "$appCsproj")"
currentBuild="$(readTag ApplicationVersion "$appCsproj")"
echo "Current version:  $currentVersion  (ApplicationVersion $currentBuild)"

# No prefix argument → just report and exit.
if [[ -z "$prefix" ]]; then
    echo ""
    echo "Version is declared in:"
    for f in "${versionFiles[@]}"; do echo "  <Version>            ${f#$root/}"; done
    echo "  <ApplicationDisplayVersion>  ${appCsproj#$root/}"
    echo "  <ApplicationVersion> (=patch) ${appCsproj#$root/}"
    echo ""
    echo "Pass a MAJOR.MINOR prefix to bump, e.g.  ./scripts/set-version.sh 1.4"
    exit 0
fi

# ── Validate the prefix + compute the new version ──────────────────────────────
if [[ ! "$prefix" =~ ^[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: '$prefix' is not a valid prefix (expected MAJOR.MINOR, e.g. 1.4)." >&2
    exit 1
fi
[[ "$currentBuild" =~ ^[0-9]+$ ]] || { echo "ERROR: <ApplicationVersion> ('$currentBuild') is not an integer." >&2; exit 1; }

newBuild=$((currentBuild + 1))
newVersion="$prefix.$newBuild"

# ── Apply ──────────────────────────────────────────────────────────────────────
echo ""
echo "Bumping ApplicationVersion $currentBuild -> $newBuild, setting version -> $newVersion"
for f in "${versionFiles[@]}"; do
    setTag Version "$newVersion" "$f"
    echo "  <Version> updated in ${f#$root/}"
done

setTag ApplicationDisplayVersion "$newVersion" "$appCsproj"
echo "  <ApplicationDisplayVersion> updated in ${appCsproj#$root/}"

setTag ApplicationVersion "$newBuild" "$appCsproj"
echo "  <ApplicationVersion> $currentBuild -> $newBuild in ${appCsproj#$root/}"

echo ""
echo "Done. Review the changes:"
echo "  git diff -- ${appCsproj#$root/} ${webAppCsproj#$root/} ${commonCsproj#$root/}"
