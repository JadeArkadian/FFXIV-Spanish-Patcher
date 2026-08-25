#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <version>" >&2
  exit 2
fi

version="$1"
out="publish/win-x64"
asset="FFXIVSpanishPatcher-$version-win-x64.zip"

rm -f "$out"/*.pdb
cp README.md "$out/README.md"
(cd "$out" && zip -r "../../$asset" .)
sha256sum "$asset" > "$asset.sha256"
