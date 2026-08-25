#!/usr/bin/env bash
set -euo pipefail

if [[ -z "${WINDOWS_SELF_SIGNING_PFX_BASE64:-}" || -z "${WINDOWS_SELF_SIGNING_PFX_PASSWORD:-}" ]]; then
  echo "::warning::Windows self-signing skipped: configure WINDOWS_SELF_SIGNING_PFX_BASE64 and WINDOWS_SELF_SIGNING_PFX_PASSWORD."
  exit 0
fi

cert_dir="$(mktemp -d)"
trap 'rm -rf "$cert_dir"' EXIT
pfx="$cert_dir/FFXIVSpanishPatcher-self-signed.pfx"
cert="$cert_dir/FFXIVSpanishPatcher-self-signed.pem"
exe="publish/win-x64/FFXIVSpanishPatcher.exe"

printf '%s' "$WINDOWS_SELF_SIGNING_PFX_BASE64" | base64 --decode > "$pfx"
openssl pkcs12 -in "$pfx" -clcerts -nokeys \
  -passin "pass:$WINDOWS_SELF_SIGNING_PFX_PASSWORD" -out "$cert"

expected_fingerprint="$(openssl x509 \
  -in docs/security/FFXIVSpanishPatcher-self-signed.pem \
  -noout -fingerprint -sha256)"
actual_fingerprint="$(openssl x509 -in "$cert" -noout -fingerprint -sha256)"
if [[ "$actual_fingerprint" != "$expected_fingerprint" ]]; then
  echo "::error::Windows signing certificate does not match documented public certificate."
  exit 1
fi

sudo apt-get update
sudo apt-get install --yes osslsigncode
osslsigncode sign \
  -pkcs12 "$pfx" \
  -pass "$WINDOWS_SELF_SIGNING_PFX_PASSWORD" \
  -h sha256 \
  -n "FFXIVSpanish Patcher" \
  -i "https://ffxivspanish.carrd.co/" \
  -in "$exe" \
  -out "$exe.signed"
mv "$exe.signed" "$exe"
osslsigncode verify -in "$exe" -CAfile "$cert"
