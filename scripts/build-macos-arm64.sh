#!/usr/bin/env bash
# 快捷脚本: 编译 macOS ARM64 (Apple Silicon)
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$SCRIPT_DIR/build.sh" macos-arm64 "$@"
