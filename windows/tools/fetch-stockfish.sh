#!/bin/bash
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
target="${1:-$here/../../dist/windows/publish}"
tag="sf_19"
asset="stockfish-windows-x86-64-universal.zip"
sha256="3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
echo "Downloading $asset ($tag)"
curl -fsSL "https://github.com/official-stockfish/Stockfish/releases/download/$tag/$asset" -o "$tmp/stockfish.zip"
actual="$(shasum -a 256 "$tmp/stockfish.zip" | cut -d' ' -f1)"
[ "$actual" = "$sha256" ] || { echo "Checksum mismatch for $asset: $actual" >&2; exit 1; }
mkdir "$tmp/x"
unzip -q "$tmp/stockfish.zip" -d "$tmp/x"
exe="$(find "$tmp/x" -iname 'stockfish*.exe' | head -n 1)"
[ -n "$exe" ] || { echo "No stockfish executable in the archive." >&2; exit 1; }
mkdir -p "$target/stockfish"
cp "$exe" "$target/stockfish/stockfish.exe"
find "$tmp/x" \( -iname 'Copying.txt' -o -iname 'AUTHORS' \) -exec cp {} "$target/stockfish/" \;
echo "Done: $target/stockfish/stockfish.exe"
