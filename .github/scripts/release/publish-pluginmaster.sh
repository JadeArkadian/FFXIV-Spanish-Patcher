#!/usr/bin/env bash
set -euo pipefail

# Publishes a release in the custom Dalamud repository by committing dalamud/pluginmaster.json to
# the default branch. Run it from a checkout of that branch, with GH_TOKEN set.
#
# To roll back, revert the commit this script made: the previous file points at the previous
# release asset, which is still there.

if [[ $# -ne 4 ]]; then
  echo "Usage: $0 <version> <tag> <pluginmaster-file> <branch>" >&2
  exit 2
fi

version="$1"
tag="$2"
source="$3"
branch="$4"
asset="FFXIVSpanishTranslationManager-$version.zip"
target="dalamud/pluginmaster.json"

# The repository must never offer a file that is not there. Download the asset back from the
# release and check it against its published checksum before pointing anyone at it.
download="$(mktemp -d)"
gh release download "$tag" --dir "$download" --pattern "$asset" --pattern "$asset.sha256"
(cd "$download" && sha256sum --check "$asset.sha256")

# Re-running the workflow of an older tag must not take the repository back to that version.
if [[ -f "$target" ]]; then
  published="$(jq -r '.[0].AssemblyVersion' "$target")"
  newest="$(printf '%s\n%s\n' "$published" "$version.0" | sort -V | tail -n 1)"
  if [[ "$newest" != "$version.0" ]]; then
    echo "::warning::El repositorio ya publica la versión $published; no se cambia por la $version.0."
    exit 0
  fi
fi

mkdir -p "$(dirname "$target")"
cp "$source" "$target"
git add "$target"
if git diff --cached --quiet; then
  echo "El repositorio ya publica exactamente este archivo; nada que subir."
  exit 0
fi

git -c user.name="github-actions[bot]" \
  -c user.email="41898282+github-actions[bot]@users.noreply.github.com" \
  commit -m "chore(dalamud): publish plugin $tag [skip ci]"

if ! git push origin "HEAD:$branch"; then
  echo "::error::No se pudo subir $target a $branch. Si la rama tiene protección, hay que dar permiso a este workflow o usar un token dedicado; si simplemente avanzó durante la release, basta con relanzar este job."
  exit 1
fi
