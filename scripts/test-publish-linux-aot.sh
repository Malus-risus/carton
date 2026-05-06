#!/usr/bin/env bash
set -euo pipefail

# Usage: scripts/test-publish-linux-aot.sh [rid] [configuration] [output] [build_macro]
# Examples:
#   scripts/test-publish-linux-aot.sh linux-x64 Release
#   scripts/test-publish-linux-aot.sh linux-x64 Release artifacts/publish/linux-x64-appimage INSTALLER_BUILD

RID="${1:-linux-x64}"
CONFIG="${2:-Release}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/src/carton.GUI/carton.GUI.csproj"
OUTPUT="${3:-${REPO_ROOT}/artifacts/publish/${RID}}"
BUILD_MACRO="${4:-}"
INCLUDE_KERNEL_SCRIPT="${SCRIPT_DIR}/include-singbox-kernel.sh"

echo "Publishing ${PROJECT} as ${RID} (${CONFIG}) with NativeAOT..."
pushd "${REPO_ROOT}" >/dev/null

props=(
  -c "${CONFIG}"
  -r "${RID}"
  -o "${OUTPUT}"
  /p:PublishAot=true
  /p:SelfContained=true
  /p:StripSymbols=true
  /p:DebugSymbols=false
  /p:DebugType=None
  /p:IncludeNativeLibrariesForSelfExtract=true
  /p:EnableCompressionInSingleFile=true
  /p:InvariantGlobalization=true
)

if [[ "$RID" == "linux-arm64" ]]; then
  props+=(/p:ObjCopyName=aarch64-linux-gnu-objcopy)
fi

if [[ -n "$BUILD_MACRO" ]]; then
  props+=(/p:CartonBuildMacro="$BUILD_MACRO")
fi

if ! dotnet publish "${PROJECT}" "${props[@]}"; then
  echo "NativeAOT publish failed."
  popd >/dev/null
  exit 1
fi

if [[ ! -f "$INCLUDE_KERNEL_SCRIPT" ]]; then
  echo "Kernel include script not found: ${INCLUDE_KERNEL_SCRIPT}" >&2
  popd >/dev/null
  exit 1
fi

bash "$INCLUDE_KERNEL_SCRIPT" "$RID" "$OUTPUT"

popd >/dev/null
echo "Output written to ${OUTPUT}"
