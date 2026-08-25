#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <rid> <external-translations>" >&2
  exit 2
fi

rid="$1"
external_translations="$2"
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
  -o "publish/$rid"
