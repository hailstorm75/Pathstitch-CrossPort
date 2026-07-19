#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
MAMBA="${MICROMAMBA:-micromamba}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Keep one cross-platform implementation so pruning, smoke tests, and manifests
# cannot drift between the Windows and macOS release jobs.
args=(
  -NoProfile
  -File "$SCRIPT_DIR/build-runtime.ps1"
  -RuntimeIdentifier "$RID"
  -Micromamba "$MAMBA"
)
if [[ -n "${OUTPUT_ROOT:-}" ]]; then
  args+=( -OutputRoot "$OUTPUT_ROOT" )
fi
exec pwsh "${args[@]}"
