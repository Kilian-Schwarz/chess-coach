#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
dest="${DEST:-/Applications}"
app="$dest/Chess Coach.app"
built="$root/build/local/Build/Products/Release/Chess Coach.app"

need() { command -v "$1" >/dev/null 2>&1 || { echo "missing: $1 ($2)" >&2; exit 1; }; }
need xcodebuild "install Xcode from the App Store"
need xcodegen "brew install xcodegen"

xcodegen generate >/dev/null
xcodebuild -project ChessCoach.xcodeproj -scheme ChessCoach -configuration Release \
  -derivedDataPath build/local CODE_SIGN_IDENTITY=- CODE_SIGNING_ALLOWED=YES build | tail -n 3
[ -d "$built" ] || { echo "build failed" >&2; exit 1; }

stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
ditto --norsrc --noextattr --noqtn "$built" "$stage/Chess Coach.app"

if stockfish_path="$(command -v stockfish)"; then
  cp "$stockfish_path" "$stage/Chess Coach.app/Contents/Resources/stockfish"
else
  echo "stockfish not found, downloading the official release"
  Tools/release/fetch-stockfish.sh macos "$stage/stockfish-dist"
  mv "$stage/stockfish-dist/stockfish" "$stage/Chess Coach.app/Contents/Resources/stockfish"
  mkdir -p "$stage/Chess Coach.app/Contents/Resources/Licenses"
  mv "$stage"/stockfish-dist/* "$stage/Chess Coach.app/Contents/Resources/Licenses/"
fi
chmod +x "$stage/Chess Coach.app/Contents/Resources/stockfish"
codesign --force --deep --sign - "$stage/Chess Coach.app"
codesign --verify --deep --strict "$stage/Chess Coach.app"

pkill -x "Chess Coach" || true
rm -rf "$app"
ditto --norsrc --noextattr --noqtn "$stage/Chess Coach.app" "$app"
open "$app"

cat <<MESSAGE

Chess Coach is installed in $dest and running (menu bar icon).

Screen recording permission
  The app needs it to see the chessboard. Switch the app on in its menu, then allow it in
  System Settings > Privacy & Security > Screen & System Audio Recording, and restart the app.
  This build is signed ad hoc, so macOS treats every rebuild as a new app: after rebuilding,
  remove the old "Chess Coach" entry in that list with the minus button and allow the app again.
MESSAGE
