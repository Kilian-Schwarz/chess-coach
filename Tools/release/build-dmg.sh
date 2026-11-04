#!/bin/bash
set -euo pipefail
app="${1:?usage: build-dmg.sh <App.app> <out.dmg>}"
out="${2:?usage: build-dmg.sh <App.app> <out.dmg>}"
name="$(basename "$app" .app)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
ditto "$app" "$stage/$name.app"
ln -s /Applications "$stage/Applications"
mkdir -p "$(dirname "$out")"
rm -f "$out"
hdiutil create -volname "$name" -srcfolder "$stage" -fs HFS+ -format UDZO -ov "$out" >/dev/null
echo "$out"
