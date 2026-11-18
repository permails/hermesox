#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_VERSION="${APP_VERSION:-1.0.0}"
DOTNET_BIN="${DOTNET_BIN:-/opt/homebrew/opt/dotnet@8/bin/dotnet}"
STAGE_DIR="$ROOT_DIR/dist/win-x64-stage"
PORTABLE_NAME="HermesDesktop-Portable-${APP_VERSION}-win-x64"
PORTABLE_DIR="$ROOT_DIR/dist/$PORTABLE_NAME"
PORTABLE_ZIP="$ROOT_DIR/dist/${PORTABLE_NAME}.zip"
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
rm -rf "$STAGE_DIR" "$PORTABLE_DIR" "$PORTABLE_ZIP" "$ROOT_DIR/dist/win-x64"
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
  -INPUTCHARSET UTF8 \
  -DAPP_VERSION="$APP_VERSION" \
  -DSTAGE_DIR="$STAGE_DIR" \
  -DICON_PATH="$ICON_PATH" \
  -DOUT_FILE="$OUT_INSTALLER" \
  "$NSI_SCRIPT"

echo "==> Packaging Portable green edition (with local ./data directory)..."
cp -R "$STAGE_DIR" "$PORTABLE_DIR"
mkdir -p "$PORTABLE_DIR/data"
cat << 'EOF' > "$PORTABLE_DIR/data/.portable"
Hermes Desktop stores settings, cache, and vault files in this ./data directory when present.
EOF
(cd "$ROOT_DIR/dist" && zip -rq "$(basename "$PORTABLE_ZIP")" "$PORTABLE_NAME")

echo "==> Done:"
ls -lh "$OUT_INSTALLER" "$PORTABLE_ZIP"
shasum -a 256 "$OUT_INSTALLER" "$PORTABLE_ZIP"
