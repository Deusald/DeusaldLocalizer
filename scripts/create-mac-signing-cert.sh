#!/usr/bin/env bash
#
# Creates a self-signed CODE SIGNING certificate in your login keychain, so build-release-mac.sh can
# sign the app with a stable identity. That stable identity is what lets MAUI SecureStorage (the macOS
# Keychain) work: an ad-hoc ("-") signature has no identity to bind keychain items to, so SecureStorage
# fails with errSecMissingEntitlement (-34018) and the app forgets the signed-in user between launches.
#
# This is a ONE-TIME, per-machine step. The certificate never leaves your Mac and is only meant for
# local / small-scale direct distribution — it does NOT replace a Developer ID cert for public,
# Gatekeeper-friendly, notarized releases (recipients on other Macs still clear quarantine by hand).
#
# Usage:
#   ./scripts/create-mac-signing-cert.sh                         # identity: "Deusald Localizer Self-Signed"
#   ./scripts/create-mac-signing-cert.sh "My Custom Identity"    # override the certificate common name
#
# The identity name you use here is the same one you pass to build-release-mac.sh via --sign-identity
# (or the MAC_SIGN_IDENTITY env var); its default matches this script's default.
set -euo pipefail

identityName="${1:-Deusald Localizer Self-Signed}"
keychain="$HOME/Library/Keychains/login.keychain-db"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: this script must run on macOS." >&2
    exit 1
fi

# Already present? codesign only needs the identity to exist once.
if security find-identity -v -p codesigning "$keychain" 2>/dev/null | grep -qF "$identityName"; then
    echo "Code-signing identity \"$identityName\" already exists in the login keychain. Nothing to do."
    exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# OpenSSL config so the cert carries the codeSigning extended key usage (works on both OpenSSL and the
# LibreSSL that ships with macOS, which lacks `-addext`).
cat > "$tmp/openssl.cnf" <<EOF
[ req ]
distinguished_name = dn
x509_extensions    = v3
prompt             = no
[ dn ]
CN = $identityName
[ v3 ]
basicConstraints = critical,CA:false
keyUsage         = critical,digitalSignature
extendedKeyUsage = critical,codeSigning
EOF

echo "Generating self-signed certificate \"$identityName\"…"
openssl req -x509 -newkey rsa:2048 -nodes \
    -keyout "$tmp/key.pem" -out "$tmp/cert.pem" \
    -days 3650 -config "$tmp/openssl.cnf" >/dev/null 2>&1

# Import the private key and certificate directly as PEM, pre-authorizing codesign to use the key without
# a UI prompt. This deliberately avoids PKCS#12: OpenSSL 3.x writes a MAC that macOS `security import`
# rejects ("MAC verification failed") and the format flags to work around it are unreliable across
# OpenSSL/LibreSSL. Two PEM imports into the same keychain still form a code-signing identity — macOS pairs
# the key and cert by public key. Order matters: import the key first so the cert links to it.
security import "$tmp/key.pem"  -k "$keychain" -T /usr/bin/codesign -T /usr/bin/security >/dev/null
security import "$tmp/cert.pem" -k "$keychain" -T /usr/bin/codesign -T /usr/bin/security >/dev/null

# Let codesign read the private key non-interactively (partition list).
security set-key-partition-list -S apple-tool:,apple: -k "" "$keychain" >/dev/null 2>&1 || true

# Trust the cert for code signing so `codesign -v` / launching the app locally accepts it. Adds to the
# user's trust settings — macOS will pop an admin prompt the first time. (Optional for signing itself,
# but avoids "not valid" verification errors on this machine.)
security add-trusted-cert -p codeSign -k "$keychain" "$tmp/cert.pem" 2>/dev/null \
    || echo "NOTE: could not set trust automatically — open Keychain Access and trust \"$identityName\" for Code Signing if needed."

echo ""
echo "Done. Verify with:"
echo "  security find-identity -v -p codesigning"
echo ""
echo "Then build a signed app with:"
echo "  ./scripts/build-release-mac.sh --sign-identity \"$identityName\""
