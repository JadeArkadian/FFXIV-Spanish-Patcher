#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 5 ]]; then
  echo "Usage: $0 <rid> <external-translations> <version> <tag> <repository>" >&2
  exit 2
fi

rid="$1"
external_translations="$2"
version="$3"
tag="$4"
repository="$5"
project="src/FFXIVSpanishPatcher.App/FFXIVSpanishPatcher.App.csproj"

dotnet restore "$project" --locked-mode
dotnet publish "$project" \
  -c Release \
  -r "$rid" \
  --self-contained \
  --no-restore \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:ExternalTranslations="$external_translations" \
  -p:Version="$version" \
  -p:InformationalVersion="$tag" \
  -p:AssemblyVersion="$version.0" \
  -p:FileVersion="$version.0" \
  -p:RepositorySlug="$repository" \
  -p:LatestReleaseApiUrl="https://api.github.com/repos/$repository/releases/latest" \
  -p:LatestReleasePageUrl="https://github.com/$repository/releases/latest" \
  -o "publish/$rid"
