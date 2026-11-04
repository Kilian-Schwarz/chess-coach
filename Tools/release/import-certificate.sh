#!/bin/bash
set -euo pipefail
: "${CERTIFICATE_P12_BASE64:?}" "${CERTIFICATE_PASSWORD:?}" "${KEYCHAIN_PASSWORD:?}"
keychain="$RUNNER_TEMP/release.keychain-db"
p12="$RUNNER_TEMP/certificate.p12"
printf '%s' "$CERTIFICATE_P12_BASE64" | base64 --decode > "$p12"
security create-keychain -p "$KEYCHAIN_PASSWORD" "$keychain"
security set-keychain-settings -lut 3600 "$keychain"
security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$keychain"
security import "$p12" -P "$CERTIFICATE_PASSWORD" -T /usr/bin/codesign -T /usr/bin/security -t cert -f pkcs12 -k "$keychain"
security set-key-partition-list -S apple-tool:,apple: -k "$KEYCHAIN_PASSWORD" "$keychain" >/dev/null
security list-keychains -d user -s "$keychain" login.keychain-db
rm -f "$p12"
security find-identity -v -p codesigning "$keychain"
