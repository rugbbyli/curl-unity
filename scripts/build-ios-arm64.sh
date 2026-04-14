#!/usr/bin/env bash
# 快捷脚本: 编译 iOS ARM64 (真机)
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$SCRIPT_DIR/build.sh" ios-arm64 "$@"
