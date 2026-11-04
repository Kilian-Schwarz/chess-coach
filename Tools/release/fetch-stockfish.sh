#!/bin/bash
set -euo pipefail
platform="${1:?usage: fetch-stockfish.sh <macos|windows> <dest-dir>}"
dest="${2:?usage: fetch-stockfish.sh <macos|windows> <dest-dir>}"
tag="sf_19"
base="https://github.com/official-stockfish/Stockfish/releases/download/$tag"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
case "$platform" in
  macos) asset="stockfish-macos-universal.tar.gz"; exe="stockfish"; sha256="a1f0e3bcc5a6927a11fe6fc8e54a779754645f3c2bae2cf13420fd1957adaa77" ;;
  windows) asset="stockfish-windows-x86-64-universal.zip"; exe="stockfish.exe"; sha256="3c8bf1f9ea66a09350a40df4f632288285ac206d99f33ab5842c408fc30b48a7" ;;
  *) echo "unknown platform: $platform" >&2; exit 1 ;;
esac
curl -fsSL "$base/$asset" -o "$work/$asset"
actual="$(shasum -a 256 "$work/$asset" | cut -d' ' -f1)"
[ "$actual" = "$sha256" ] || { echo "checksum mismatch for $asset: $actual" >&2; exit 1; }
mkdir "$work/x"
case "$asset" in
  *.zip) unzip -q "$work/$asset" -d "$work/x" ;;
  *) tar -xzf "$work/$asset" -C "$work/x" ;;
esac
bin="$(find "$work/x" -type f \( -name 'stockfish' -o -name 'stockfish-*' -o -name 'stockfish*.exe' \) ! -name '*.txt' ! -name '*.md' | head -n 1)"
[ -n "$bin" ] || { echo "no stockfish binary in $asset" >&2; exit 1; }
mkdir -p "$dest"
cp "$bin" "$dest/$exe"
chmod +x "$dest/$exe"
find "$work/x" -type f \( -iname 'Copying*' -o -iname 'AUTHORS*' \) -exec cp {} "$dest/" \;
[ -f "$dest/Copying.txt" ] || curl -fsSL "https://raw.githubusercontent.com/official-stockfish/Stockfish/$tag/Copying.txt" -o "$dest/Copying.txt"
cat > "$dest/STOCKFISH-NOTICE.txt" <<NOTICE
This folder contains the unmodified Stockfish chess engine ($tag), licensed under GPL-3.0.
Source code: https://github.com/official-stockfish/Stockfish/tree/$tag
Binary release: $base/$asset
NOTICE
echo "stockfish $tag -> $dest"
