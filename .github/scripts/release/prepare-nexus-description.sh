#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: $0 <version> <rid>" >&2
  exit 2
fi

: "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required}"

version="$1"
rid="$2"

case "$rid" in
  linux-x64)
    description="Versión $version para Linux (x64)"
    ;;
  osx-arm64)
    description="Versión $version para macOS Apple Silicon"
    ;;
  *)
    echo "::error::RID no soportado: $rid"
    exit 1
    ;;
esac

echo "description=$description" >> "$GITHUB_OUTPUT"
