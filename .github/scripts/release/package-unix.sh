#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version> <rid>" >&2
  exit 2
fi

version="$1"
rid="$2"
out="publish/$rid"
asset="FFXIVSpanishPatcher-$version-$rid.zip"

rm -f "$out"/*.pdb

if [[ "$rid" == osx-* ]]; then
  # Build a proper bundle so Finder and the Dock show the icon and application name.
  app="FFXIVSpanishPatcher.app"
  rm -rf "$app"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp "$out/FFXIVSpanishPatcher" "$app/Contents/MacOS/FFXIVSpanishPatcher"
  chmod +x "$app/Contents/MacOS/FFXIVSpanishPatcher"
  cp src/FFXIVSpanishPatcher.App/Assets/icon.icns "$app/Contents/Resources/icon.icns"
  sed "s/__VERSION__/$version/g" build/macos/Info.plist > "$app/Contents/Info.plist"

  # Apple Silicon requires a signature to launch. The ad-hoc signature is intentionally not notarized.
  codesign --force --timestamp=none --sign - "$app/Contents/MacOS/FFXIVSpanishPatcher"
  codesign --force --timestamp=none --sign - "$app"
  codesign --verify --strict --verbose=2 "$app"

  # ditto preserves both the bundle layout and its code signature.
  ditto -c -k --keepParent "$app" "$asset"
else
  cp README.md "$out/README.md"
  (cd "$out" && zip -r "../../$asset" .)
fi

if command -v sha256sum >/dev/null; then
  sha256sum "$asset" > "$asset.sha256"
else
  shasum -a 256 "$asset" > "$asset.sha256"
fi
