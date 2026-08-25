#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <rid>" >&2
  exit 2
fi

rid="$1"
app="$GITHUB_WORKSPACE/publish/$rid/FFXIVSpanishPatcher"
log="$RUNNER_TEMP/FFXIVSpanishPatcher-smoke.log"

chmod +x "$app"

case "$rid" in
  linux-x64)
    xvfb-run -a "$app" >"$log" 2>&1 &
    error_message="FFXIVSpanishPatcher exited during Linux smoke test."
    ;;
  osx-arm64)
    "$app" >"$log" 2>&1 &
    error_message="FFXIVSpanishPatcher exited during macOS smoke test."
    ;;
  *)
    echo "Unsupported Unix smoke RID: $rid" >&2
    exit 2
    ;;
esac

app_pid=$!
sleep 10
if ! kill -0 "$app_pid" 2>/dev/null; then
  cat "$log"
  wait "$app_pid" || true
  echo "$error_message" >&2
  exit 1
fi

kill "$app_pid"
wait "$app_pid" || true
