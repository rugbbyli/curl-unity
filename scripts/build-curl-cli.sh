#!/usr/bin/env bash
#
# 编译 macOS ARM64 下的 curl 命令行工具用于测试
#
# 产物: output/macOS/curl
#
# 用法:
#   ./scripts/build-curl-cli.sh [--clean]
#
# 测试:
#   ./output/macOS/curl --version
#   ./output/macOS/curl -v --http3 https://cloudflare-quic.com/
#   ./output/macOS/curl -v --http2 https://httpbin.org/get
#
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$SCRIPT_DIR/build.sh" macos-arm64 --curl-cli "$@"
