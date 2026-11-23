#!/usr/bin/env bash
set -euo pipefail

# The plugin projects compile against Dalamud's own assemblies, which Dalamud.NET.Sdk reads from
# DALAMUD_HOME. They are a build-time reference only and are never redistributed. The release
# track floats on purpose: an API-level bump must fail here until the SDK is updated.

: "${RUNNER_TEMP:?RUNNER_TEMP is required}"
: "${GITHUB_ENV:?GITHUB_ENV is required}"

dalamud="$RUNNER_TEMP/dalamud"
mkdir -p "$dalamud"
curl -fsSL --retry 3 -o "$RUNNER_TEMP/dalamud.zip" https://goatcorp.github.io/dalamud-distrib/latest.zip
unzip -q "$RUNNER_TEMP/dalamud.zip" -d "$dalamud"
test -f "$dalamud/Dalamud.dll"
echo "DALAMUD_HOME=$dalamud" >> "$GITHUB_ENV"
