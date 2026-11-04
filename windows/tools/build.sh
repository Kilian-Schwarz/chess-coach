#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$here/../.."
dist="$root/dist/windows"
project="$root/windows/ChessCoach.App/ChessCoach.App.csproj"
version="${1:-$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$project" | head -n 1)}"
args=(-c Release -r win-x64 --self-contained -p:PublishSingleFile=true "-p:Version=$version" -o "$dist/publish")
[ -z "${OFFICIAL_BUILD:-}" ] || args+=(-p:OfficialBuild=true)
rm -rf "$dist/publish"
dotnet publish "$project" "${args[@]}"
"$here/fetch-stockfish.sh" "$dist/publish"
zip="$dist/ChessCoach-$version-windows-x64.zip"
rm -f "$zip"
(cd "$dist/publish" && zip -qr "$zip" .)
setup_project="$root/windows/ChessCoach.Setup/ChessCoach.Setup.csproj"
rm -rf "$dist/setup"
dotnet publish "$setup_project" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true "-p:Version=$version" -o "$dist/setup"
setup="$dist/ChessCoach-$version-setup.exe"
size="$(wc -c < "$zip" | tr -d ' ')"
{ cat "$dist/setup/ChessCoach-Setup.exe" "$zip"; printf '%016xCCSETUP1' "$size"; } > "$setup"
echo "Built: $zip"
echo "Built: $setup"
