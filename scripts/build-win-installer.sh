#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_VERSION="${APP_VERSION:-1.0.0}"
DOTNET_BIN="${DOTNET_BIN:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
STAGE_DIR="$ROOT_DIR/dist/win-x64-stage"
OUT_INSTALLER="$ROOT_DIR/dist/HermesDesktop-Setup-${APP_VERSION}-win-x64.exe"
NSI_SCRIPT="$ROOT_DIR/installer/windows/hermes-desktop.nsi"
ICON_PATH="$ROOT_DIR/src/HermesDesktop.App/Assets/hermes-desktop.ico"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/dotnet_cli}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/nuget_packages}"
export LANG="en_US.UTF-8"
export LC_ALL="en_US.UTF-8"

if ! command -v makensis >/dev/null 2>&1; then
  echo "Error: makensis not found. Install via: brew install makensis" >&2
  exit 1
fi

echo "==> Publishing HermesDesktop (win-x64 self-contained multi-file)..."
rm -rf "$STAGE_DIR"
"$DOTNET_BIN" publish "$ROOT_DIR/src/HermesDesktop.App/HermesDesktop.App.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  -p:Version="$APP_VERSION" \
  --source "$NUGET_PACKAGES" \
  --disable-build-servers \
  -m:1 \
  -p:UseSharedCompilation=false \
  -o "$STAGE_DIR"

echo "==> Building NSIS Windows installer..."
makensis \
  -DAPP_VERSION="$APP_VERSION" \
  -DSTAGE_DIR="$STAGE_DIR" \
  -DICON_PATH="$ICON_PATH" \
  -DOUT_FILE="$OUT_INSTALLER" \
  "$NSI_SCRIPT"

echo "==> Done: $OUT_INSTALLER"
ls -lh "$OUT_INSTALLER"
shasum -a 256 "$OUT_INSTALLER"
