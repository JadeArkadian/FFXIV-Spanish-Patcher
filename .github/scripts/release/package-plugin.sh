#!/usr/bin/env bash
set -euo pipefail

# Builds the Dalamud plugin with the release version, runs its tests against that exact build and
# leaves the ZIP and its checksum in the working directory. CI runs it too, with version 0.0.0.

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version> <tag>" >&2
  exit 2
fi

version="$1"
tag="$2"
tests="tests/FFXIVSpanishPatcher.Plugin.Tests/FFXIVSpanishPatcher.Plugin.Tests.csproj"
package="src/FFXIVSpanishPatcher.Plugin/bin/Release/FFXIVSpanishTranslationManager"
asset="FFXIVSpanishTranslationManager-$version.zip"

# Building the test project builds the plugin with the same properties. Its package tests then
# check the ZIP contents and that assembly, manifest and generated mod metadata share one version.
dotnet restore "$tests" --locked-mode
dotnet build "$tests" \
  -c Release \
  --no-restore \
  -p:Version="$version" \
  -p:InformationalVersion="$tag" \
  -p:AssemblyVersion="$version.0" \
  -p:FileVersion="$version.0"
dotnet test "$tests" -c Release --no-build --verbosity minimal

# The tests prove the build agrees with itself; this proves it agrees with the tag.
manifest_version="$(jq -r '.AssemblyVersion' "$package/FFXIVSpanishTranslationManager.json")"
if [[ "$manifest_version" != "$version.0" ]]; then
  echo "::error::El manifiesto del plugin declara la versión $manifest_version; se esperaba $version.0."
  exit 1
fi

cp "$package/latest.zip" "$asset"
sha256sum "$asset" > "$asset.sha256"
