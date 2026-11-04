#!/bin/bash
set -euo pipefail
version="${1:?usage: changelog-section.sh <x.y.z> [file]}"
file="${2:-$(dirname "$0")/../../CHANGELOG.md}"
section="$(awk -v v="$version" '
  /^## \[/ { if (found) exit; if (index($0, "[" v "]")) { found = 1; next } }
  found && !/^\[[^]]+\]: / { print }
' "$file")"
[ -n "$(printf '%s' "$section" | tr -d '[:space:]')" ] || { echo "no changelog section for $version" >&2; exit 1; }
printf '%s\n' "$section"
