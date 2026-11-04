#!/bin/bash
set -euo pipefail
usage() { echo "usage: release-local.sh --version X.Y.Z [--identity NAME] [--windows-zip FILE] [--dry-run] [--upload]" >&2; exit 1; }
version=""; windows_zip=""; identity="Developer ID Application"; dry_run=0; upload=0
while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="${2:?}"; shift 2 ;;
    --windows-zip) windows_zip="${2:?}"; shift 2 ;;
    --identity) identity="${2:?}"; shift 2 ;;
    --dry-run) dry_run=1; shift ;;
    --upload) upload=1; shift ;;
    *) usage ;;
  esac
done
[ -n "$version" ] || usage
[ "$dry_run" = 1 ] || : "${NOTARY_PROFILE:?set NOTARY_PROFILE to a notarytool keychain profile}"
[ "$upload" = 0 ] || : "${GITHUB_TOKEN:?}"
repo="${GITHUB_REPOSITORY:-Kilian-Schwarz/chess-coach}"
here="$(cd "$(dirname "$0")" && pwd)"
root="$here/../.."
cd "$root"
dist="$PWD/dist/mac"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
derived="$work/build"
rm -rf "$dist"
mkdir -p "$dist"
finish() { cp "$work"/*.zip "$work"/*.dmg "$work"/*.json "$work/notes.md" "$dist/" 2>/dev/null || true; ditto --norsrc --noextattr --noqtn "$app" "$dist/Chess Coach.app"; }

"$here/set-version.sh" "$version" "$(date +%Y%m%d%H%M)"
xcodegen generate >/dev/null
xcodebuild -project ChessCoach.xcodeproj -scheme ChessCoach -configuration Release \
  -derivedDataPath "$derived" CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO build | tail -n 3
app="$work/Chess Coach.app"
ditto --norsrc --noextattr --noqtn "$derived/Build/Products/Release/Chess Coach.app" "$app"
"$here/fetch-stockfish.sh" macos "$app/Contents/Resources/stockfish-dist"
mv "$app/Contents/Resources/stockfish-dist/stockfish" "$app/Contents/Resources/stockfish"
mkdir -p "$app/Contents/Resources/Licenses"
mv "$app/Contents/Resources"/stockfish-dist/* "$app/Contents/Resources/Licenses/"
rmdir "$app/Contents/Resources/stockfish-dist"

sign() { codesign --force --timestamp --options runtime --sign "$identity" "$@"; }
xattr -cr "$app"
sign "$app/Contents/Resources/stockfish"
sign "$app"
codesign --verify --deep --strict --verbose=2 "$app"

zip="$work/ChessCoach-$version-macos.zip"
dmg="$work/ChessCoach-$version.dmg"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
if [ "$dry_run" = 1 ]; then finish; echo "dry run: stopping before notarization ($dist)"; exit 0; fi

xcrun notarytool submit "$zip" --keychain-profile "$NOTARY_PROFILE" --wait
xcrun stapler staple "$app"
rm -f "$zip"
ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
"$here/build-dmg.sh" "$app" "$dmg"
sign "$dmg"
xcrun notarytool submit "$dmg" --keychain-profile "$NOTARY_PROFILE" --wait
xcrun stapler staple "$dmg"

notes="$work/notes.md"
"$here/changelog-section.sh" "$version" > "$notes"
if [ -n "${UPDATE_SIGNING_KEY:-}" ]; then
  windows_args=()
  [ -z "$windows_zip" ] || windows_args=(--windows "$windows_zip")
  "$here/make-update-json.sh" --version "$version" --notes "$notes" --out "$work/update.json" --macos "$zip" ${windows_args[@]+"${windows_args[@]}"}
fi

finish
zip="$dist/$(basename "$zip")"; dmg="$dist/$(basename "$dmg")"
[ "$upload" = 1 ] || { echo "done: $dist"; exit 0; }
api="https://api.github.com/repos/$repo"
auth=(-H "Authorization: Bearer $GITHUB_TOKEN" -H "Accept: application/vnd.github+json")
body="$(python3 -I -c 'import json,sys; print(json.dumps({"tag_name":"v"+sys.argv[1],"name":"v"+sys.argv[1],"body":open(sys.argv[2]).read(),"draft":True}))' "$version" "$notes")"
release="$(curl -fsS "${auth[@]}" -X POST "$api/releases" -d "$body")"
upload_url="$(python3 -I -c 'import json,sys; print(json.load(sys.stdin)["upload_url"].split("{")[0])' <<<"$release")"
for f in "$zip" "$dmg" "$dist/update.json" ${windows_zip:+"$windows_zip"}; do
  curl -fsS "${auth[@]}" -H "Content-Type: application/octet-stream" --data-binary @"$f" "$upload_url?name=$(basename "$f")" >/dev/null
done
echo "draft release v$version created; publish it on GitHub"
