#!/usr/bin/env bash
# 快捷脚本: 编译 Android armv7 + arm64 (min API 22)
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "========== Android armeabi-v7a =========="
"$SCRIPT_DIR/build.sh" android-armv7 --android-api 22 "$@"

echo ""
echo "========== Android arm64-v8a =========="
"$SCRIPT_DIR/build.sh" android-arm64 --android-api 22 "$@"

echo ""
echo "===== Android 全部完成 ====="
echo "产物:"
echo "  output/Android/armeabi-v7a/libcurl.so"
echo "  output/Android/arm64-v8a/libcurl.so"
