#!/bin/bash
set -euo pipefail
usage() { echo "usage: make-update-json.sh --version X.Y.Z --notes FILE --out FILE [--macos ZIP] [--windows ZIP]" >&2; exit 1; }
version=""; notes=""; out=""; macos=""; windows=""
while [ $# -gt 0 ]; do
  case "$1" in
    --version) version="${2:?}"; shift 2 ;;
    --notes) notes="${2:?}"; shift 2 ;;
    --out) out="${2:?}"; shift 2 ;;
    --macos) macos="${2:?}"; shift 2 ;;
    --windows) windows="${2:?}"; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$version" ] && [ -n "$notes" ] && [ -n "$out" ] || usage
: "${UPDATE_SIGNING_KEY:?set UPDATE_SIGNING_KEY to the path of the PEM private key}"
[ -f "$UPDATE_SIGNING_KEY" ] || { echo "key file not found" >&2; exit 1; }

entry() {
  local zip="$1"
  printf '%s\t%s\t%s\n' "$(basename "$zip")" \
    "$(shasum -a 256 "$zip" | cut -d' ' -f1)" \
    "$(openssl dgst -sha256 -sign "$UPDATE_SIGNING_KEY" "$zip" | base64 | tr -d '\n')"
}

mac_entry=""; win_entry=""
[ -z "$macos" ] || mac_entry="$(entry "$macos")"
[ -z "$windows" ] || win_entry="$(entry "$windows")"

python3 -I - "$version" "$notes" "$out" "$mac_entry" "$win_entry" <<'PY'
import json, sys
version, notes_path, out, mac, win = sys.argv[1:6]
def asset(line):
    file, sha, sig = line.split("\t")
    return {"file": file, "sha256": sha, "signature": sig}
doc = {"version": version, "notes": open(notes_path).read().strip()}
if mac: doc["macos"] = asset(mac)
if win: doc["windows"] = asset(win)
with open(out, "w") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY
echo "$out"
