#!/usr/bin/env bash
set -euo pipefail

: "${GITHUB_REF_NAME:?GITHUB_REF_NAME is required}"
: "${GITHUB_OUTPUT:?GITHUB_OUTPUT is required}"

tag="$GITHUB_REF_NAME"
if [[ ! "$tag" =~ ^v(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})\.(0|[1-9][0-9]{0,2})$ ]]; then
  echo "::error::Release tags must match vX.Y.Z, with each number from 0 to 999."
  exit 1
fi

echo "tag=$tag" >> "$GITHUB_OUTPUT"
echo "version=${tag#v}" >> "$GITHUB_OUTPUT"
