#!/bin/zsh
set -e
cd "${0:A:h}/.."
app="/Applications/Chess Coach.app"
built="$PWD/build/Build/Products/Release/Chess Coach.app"
identity="${SIGN_IDENTITY:-Apple Development}"
team="${DEVELOPMENT_TEAM:-29GTPRH5R3}"
xcodegen generate >/dev/null
xcodebuild -project ChessCoach.xcodeproj -scheme ChessCoach -configuration Release -derivedDataPath build \
  CODE_SIGN_IDENTITY="$identity" DEVELOPMENT_TEAM="$team" build 2>&1 | grep -E "error:|BUILD"
pkill -x "Chess Coach" || true
rm -rf "$app"
ditto --norsrc --noextattr --noqtn "$built" "$app"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$built" || true
codesign --verify --deep --strict "$app" && echo "Signature ok"
open "$app"
