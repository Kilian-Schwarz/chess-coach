#!/bin/bash
set -euo pipefail
version="${1:?usage: set-version.sh <x.y.z> [build]}"
build="${2:-${GITHUB_RUN_NUMBER:-1}}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "invalid version: $version" >&2; exit 1; }
root="$(cd "$(dirname "$0")/../.." && pwd)"
perl -pi -e "s/^(\s*MARKETING_VERSION:).*/\$1 \"$version\"/; s/^(\s*CURRENT_PROJECT_VERSION:).*/\$1 \"$build\"/" "$root/project.yml"
for csproj in "$root"/windows/*/*.csproj; do
  perl -pi -e "s|<Version>.*</Version>|<Version>$version</Version>|" "$csproj"
done
echo "version $version (build $build)"
