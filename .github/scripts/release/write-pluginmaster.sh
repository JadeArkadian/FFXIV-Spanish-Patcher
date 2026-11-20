#!/usr/bin/env bash
set -euo pipefail

# Writes the custom Dalamud repository file for one release: the manifest the build generated plus
# where to download that exact version. Run it after package-plugin.sh.

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <tag> <repository> <output>" >&2
  exit 2
fi

version="$1"
tag="$2"
repository="$3"
output="$4"
manifest="src/FFXIVSpanishPatcher.Plugin/bin/Release/FFXIVSpanishTranslationManager/FFXIVSpanishTranslationManager.json"

# The link names the versioned asset, never "latest": a repository file restored from history
# must keep installing the version it describes.
url="https://github.com/$repository/releases/download/$tag/FFXIVSpanishTranslationManager-$version.zip"

jq --arg url "$url" --argjson updated "$(date +%s)" '[. + {
  DownloadLinkInstall: $url,
  DownloadLinkUpdate: $url,
  DownloadLinkTesting: $url,
  IsHide: false,
  IsTestingExclusive: false,
  LastUpdate: $updated
}]' "$manifest" > "$output"

# Dalamud silently ignores an entry it cannot use, so refuse to write one here.
if ! jq -e --arg version "$version.0" '
  length == 1
  and .[0].InternalName == "FFXIVSpanishTranslationManager"
  and .[0].AssemblyVersion == $version
  and (.[0].DalamudApiLevel | type == "number")
' "$output" > /dev/null; then
  echo "::error::El pluginmaster generado no describe la versión $version del plugin."
  exit 1
fi
