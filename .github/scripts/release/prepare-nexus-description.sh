#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <version> <rid> <repository>" >&2
  exit 2
fi

: "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required}"

version="$1"
rid="$2"
repository="$3"

case "$rid" in
  win-x64)
    description="Versión $version para Windows 11. Si la descarga aún no está disponible en Nexus (validación manual pendiente), puedes bajarla ya desde GitHub: https://github.com/$repository/releases/latest"
    ;;
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
