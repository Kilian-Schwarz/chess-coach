#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
openssl ecparam -name prime256v1 -genkey -noout -out "$work/key.pem"
openssl pkey -in "$work/key.pem" -pubout -outform DER | base64 | tr -d '\n' > "$work/pub.b64"
head -c 4096 /dev/urandom > "$work/payload.zip"
openssl dgst -sha256 -sign "$work/key.pem" "$work/payload.zip" | base64 | tr -d '\n' > "$work/sig.b64"
swiftc -O -o "$work/test" "$root/ChessCoach/App/Updater.swift" "$root/ChessCoach/Core/Log.swift" "$root/Tools/release/updater-test/main.swift"
"$work/test" "$work/payload.zip" "$(cat "$work/pub.b64")" "$(cat "$work/sig.b64")"
