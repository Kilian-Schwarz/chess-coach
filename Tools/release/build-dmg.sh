#!/bin/bash
set -euo pipefail
app="${1:?usage: build-dmg.sh <App.app> <out.dmg>}"
out="${2:?usage: build-dmg.sh <App.app> <out.dmg>}"
name="$(basename "$app" .app)"
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
mount="$work/mount"
cleanup() { hdiutil detach -quiet -force "$mount" 2>/dev/null || true; rm -rf "$work"; }
trap cleanup EXIT
stage="$work/stage"
mkdir -p "$stage" "$mount" "$(dirname "$out")"
ditto "$app" "$stage/$name.app"
ln -s /Applications "$stage/Applications"
megabytes=$(( $(du -sk "$stage" | cut -f1) / 1024 + 20 ))
hdiutil create -volname "$name" -srcfolder "$stage" -fs HFS+ -format UDRW -size "${megabytes}m" -ov "$work/rw.dmg" >/dev/null
hdiutil attach "$work/rw.dmg" -readwrite -noverify -nobrowse -noautoopen -mountpoint "$mount" >/dev/null
python3 "$here/dsstore.py" "$mount/.DS_Store" "$name.app"
icon="$app/Contents/Resources/AppIcon.icns"
if [ -f "$icon" ] && command -v SetFile >/dev/null; then
  cp "$icon" "$mount/.VolumeIcon.icns"
  SetFile -a C "$mount"
fi
sync
hdiutil detach -quiet "$mount"
rm -f "$out"
hdiutil convert "$work/rw.dmg" -format UDZO -imagekey zlib-level=9 -o "$out" >/dev/null
echo "$out"
