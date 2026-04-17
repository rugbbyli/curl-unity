#!/usr/bin/env bash
#
# curl-unity device test orchestrator
#
# Usage:
#   ./scripts/device-test.sh <platform> [options]
#
# Platforms:
#   macos       Build & run on this Mac
#   android     Deploy to connected Android device
#   ios         Deploy to connected iOS device
#   windows     Cross-build on macOS, deploy & run on Windows via SSH
#
# Options:
#   --build         Run Unity build before deploying (default: use existing build)
#   --skip-sync     Skip sync-plugins.sh step
#   --timeout <s>   Seconds to wait for test completion (default: 120)
#   --unity <ver>   Unity version to use (default: 2022.3.62f3)
#
set -eo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DEMO_DIR="$PROJECT_ROOT/demo/curl-unity"
BUILD_DIR="$DEMO_DIR/Build"

# Defaults
PLATFORM=""
DO_BUILD=false
SKIP_SYNC=false
USE_LOCAL_SYNC=false
TIMEOUT=120
UNITY_VERSION="2022.3.62f3"
UNITY_APP="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"

# ADB: prefer $ADB env var, else $ANDROID_SDK_ROOT/$ANDROID_HOME platform-tools,
# else rely on PATH. Users can override with: ADB=/path/to/adb ./device-test.sh android
if [[ -z "${ADB:-}" ]]; then
    if [[ -n "${ANDROID_SDK_ROOT:-}" && -x "$ANDROID_SDK_ROOT/platform-tools/adb" ]]; then
        ADB="$ANDROID_SDK_ROOT/platform-tools/adb"
    elif [[ -n "${ANDROID_HOME:-}" && -x "$ANDROID_HOME/platform-tools/adb" ]]; then
        ADB="$ANDROID_HOME/platform-tools/adb"
    else
        ADB="adb"
    fi
fi

PACKAGE_NAME="com.basecity.curlunitytest"
WIN_SSH="${WIN_SSH:-developer@192.168.0.170}"
WIN_REMOTE_DIR="curl-unity-test"

# ============================================================
# Parse arguments
# ============================================================
while [[ $# -gt 0 ]]; do
    case "$1" in
        macos|android|ios|windows)
            PLATFORM="$1" ;;
        --build)
            DO_BUILD=true ;;
        --skip-sync)
            SKIP_SYNC=true ;;
        --local)
            USE_LOCAL_SYNC=true ;;
        --timeout)
            TIMEOUT="$2"; shift ;;
        --unity)
            UNITY_VERSION="$2"; shift
            UNITY_APP="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity" ;;
        *)
            echo "Unknown argument: $1"; exit 1 ;;
    esac
    shift
done

if [[ -z "$PLATFORM" ]]; then
    cat <<USAGE
Usage: $0 <macos|android|ios|windows> [options]

Options:
  --build         Run Unity build before deploying (default: use existing build)
  --skip-sync     Skip sync-plugins.sh step
  --local         Use local sync mode (sync Plugins from ../output instead of CI)
  --timeout <s>   Seconds to wait for test completion (default: 120)
  --unity <ver>   Unity version to use (default: 2022.3.62f3)

Environment:
  ADB                 adb binary path (auto-detected from ANDROID_SDK_ROOT / ANDROID_HOME / PATH)
  UNITY_IOS_TEAM_ID   Apple Developer Team ID (required for iOS)
  WIN_SSH             SSH target for Windows remote run (e.g. user@host)
USAGE
    exit 1
fi

# ============================================================
# Utility functions
# ============================================================
log()  { echo "==> $*"; }
err()  { echo "ERROR: $*" >&2; }
step() { echo ""; echo "--- $* ---"; }

# Pipe a streaming command's stdout into wait_for_tests and guarantee the
# streamer exits even if it doesn't honor SIGPIPE when idle (observed with
# `adb logcat` and `idevicedebug`, which buffer/re-pump and can't tell that
# the reader went away once END has been seen).
#
# Usage: stream_to_tests <cmd> [args...]
stream_to_tests() {
    local fifo streamer_pid rc
    fifo=$(mktemp -u -t curl-test.XXXXXX)
    mkfifo "$fifo"
    "$@" > "$fifo" 2>&1 &
    streamer_pid=$!
    wait_for_tests < "$fifo"
    rc=$?
    kill "$streamer_pid" 2>/dev/null || true
    wait "$streamer_pid" 2>/dev/null || true
    rm -f "$fifo"
    return $rc
}

wait_for_tests() {
    # Read lines from stdin, output [CURL_TEST] lines, stop at END or timeout.
    #
    # 使用 `read -t` 的短轮询超时，保证当上游测试 app hang 住没有输出时，
    # 我们依然能在 TIMEOUT 秒后主动退出，不会被 `read` 无限阻塞。
    # （之前版本的 deadline 检查在 read 之后，对"静默挂起"场景失效。）
    local deadline=$(( $(date +%s) + TIMEOUT ))
    local end_seen=false
    local result_file="$BUILD_DIR/test-results-$PLATFORM.log"
    : > "$result_file"

    while true; do
        if IFS= read -r -t 5 line; then
            if [[ "$line" == *"[CURL_TEST]"* ]]; then
                local test_line="${line##*\[CURL_TEST\] }"
                echo "[CURL_TEST] $test_line"
                echo "[CURL_TEST] $test_line" >> "$result_file"

                if [[ "$test_line" == END* ]]; then
                    end_seen=true
                    break
                fi
            fi
        fi
        # Whether read succeeded, timed out, or hit EOF — always check the
        # wall-clock deadline so a silent-hang can't keep us blocked forever.
        if (( $(date +%s) > deadline )); then
            echo "[CURL_TEST] TIMEOUT after ${TIMEOUT}s"
            break
        fi
    done

    if $end_seen; then
        # Parse END line
        local end_line
        end_line=$(grep '^\[CURL_TEST\] END' "$result_file" | tail -1)
        local passed failed skipped total_ms
        passed=$(echo "$end_line" | grep -o 'passed=[0-9]*' | cut -d= -f2)
        failed=$(echo "$end_line" | grep -o 'failed=[0-9]*' | cut -d= -f2)
        skipped=$(echo "$end_line" | grep -o 'skipped=[0-9]*' | cut -d= -f2)
        total_ms=$(echo "$end_line" | grep -o 'total_ms=[0-9]*' | cut -d= -f2)

        echo ""
        echo "========================================"
        if [[ "${failed:-0}" == "0" ]]; then
            echo "  ALL TESTS PASSED ($passed passed, $skipped skipped, ${total_ms}ms)"
        else
            echo "  TESTS FAILED: $passed passed, $failed FAILED, $skipped skipped (${total_ms}ms)"
            # Show failure details
            echo ""
            echo "  Failures:"
            grep '^\[CURL_TEST\] FAIL' "$result_file" | while IFS= read -r f; do
                echo "    $f"
            done
        fi
        echo "========================================"
        echo ""
        echo "Full log: $result_file"

        [[ "${failed:-0}" == "0" ]]
        return $?
    else
        err "Tests did not complete within ${TIMEOUT}s"
        echo "Partial log: $result_file"
        return 1
    fi
}

# ============================================================
# Phase 1: Sync plugins
# ============================================================
if ! $SKIP_SYNC; then
    if $USE_LOCAL_SYNC; then
        step "Sync native plugins (local)"
        "$SCRIPT_DIR/sync-plugins.sh"
    else
        step "Sync native plugins (CI)"
        "$SCRIPT_DIR/sync-ci-plugins.sh"
    fi
fi

# ============================================================
# Phase 2: Build (optional)
# ============================================================
if $DO_BUILD; then
    step "Unity build for $PLATFORM"

    if [[ ! -x "$UNITY_APP" ]]; then
        err "Unity not found at: $UNITY_APP"
        exit 2
    fi

    mkdir -p "$BUILD_DIR"
    local_log="$BUILD_DIR/unity-build-$PLATFORM.log"

    # Pass -buildTarget so Unity activates the correct platform BEFORE the
    # project loads. Without it, editor scripts gated on e.g. UNITY_IOS
    # (CurlPostProcessor) are compiled out and their [PostProcessBuild]
    # callbacks never register — the iOS build then fails to link libz.
    case "$PLATFORM" in
        macos)
            METHOD="AutoTestBuilder.BuildMacOS"
            UNITY_TARGET="OSXUniversal" ;;
        windows)
            METHOD="AutoTestBuilder.BuildWindows"
            UNITY_TARGET="Win64" ;;
        android)
            METHOD="AutoTestBuilder.BuildAndroid"
            UNITY_TARGET="Android" ;;
        ios)
            METHOD="AutoTestBuilder.BuildiOS"
            UNITY_TARGET="iOS" ;;
    esac

    log "Running: Unity -buildTarget $UNITY_TARGET -executeMethod $METHOD"
    log "Build log: $local_log"

    "$UNITY_APP" \
        -batchmode -quit -nographics \
        -buildTarget "$UNITY_TARGET" \
        -projectPath "$DEMO_DIR" \
        -executeMethod "$METHOD" \
        -logFile "$local_log" 2>&1 || {
        err "Unity build failed (exit $?)"
        err "Last 30 lines of build log:"
        tail -30 "$local_log" >&2
        exit 2
    }

    log "Unity build complete"
fi

# ============================================================
# Phase 3: Deploy & run
# ============================================================
step "Deploy and run on $PLATFORM"

case "$PLATFORM" in

# --- macOS ---
macos)
    APP="$BUILD_DIR/macOS/curl-unity-test.app"
    if [[ ! -d "$APP" ]]; then
        err "macOS build not found at $APP"
        err "Run with --build to create it"
        exit 2
    fi

    LOG_DIR="$HOME/Library/Logs/BaseCityTest/curl-unity-test"
    LOG_FILE="$LOG_DIR/Player.log"
    mkdir -p "$LOG_DIR"
    : > "$LOG_FILE" 2>/dev/null || true

    log "Launching: $APP"
    open "$APP" --args -autotest &

    # Wait a moment for app to start writing logs
    sleep 2

    # Tail the log file and parse for test results. Using stream_to_tests
    # so the tail process is explicitly killed after END — robust even if
    # the app crashes early and tail has no data to write (no SIGPIPE).
    stream_to_tests tail -n 0 -f "$LOG_FILE"
    exit_code=$?

    # Kill the app if still running
    pkill -f "curl-unity-test" 2>/dev/null || true

    exit $exit_code
    ;;

# --- Windows (via SSH) ---
windows)
    WIN_BUILD="$BUILD_DIR/Windows"
    if [[ ! -f "$WIN_BUILD/curl-unity-test.exe" ]]; then
        err "Windows build not found at $WIN_BUILD/"
        err "Run with --build to create it"
        exit 2
    fi

    # Check SSH connectivity
    if ! ssh -o ConnectTimeout=5 -o BatchMode=yes "$WIN_SSH" "echo ok" &>/dev/null; then
        err "Cannot connect to Windows host: $WIN_SSH"
        err "Set WIN_SSH env var or check SSH config"
        exit 3
    fi

    win_host_name=$(ssh "$WIN_SSH" "hostname" 2>/dev/null | tr -d '\r')
    log "Windows host: $win_host_name ($WIN_SSH)"

    # Upload build to Windows (only when --build, using tar for reliability)
    if $DO_BUILD; then
        log "Uploading build to Windows (tar)..."
        tar czf /tmp/curl-unity-test-win.tar.gz -C "$BUILD_DIR" Windows/
        scp -q /tmp/curl-unity-test-win.tar.gz "$WIN_SSH:curl-unity-test-win.tar.gz"
        ssh "$WIN_SSH" "powershell -Command \"Remove-Item -Recurse -Force $WIN_REMOTE_DIR -ErrorAction SilentlyContinue; tar xzf curl-unity-test-win.tar.gz -C .; Rename-Item Windows $WIN_REMOTE_DIR; Remove-Item curl-unity-test-win.tar.gz\""
        rm -f /tmp/curl-unity-test-win.tar.gz
        log "Upload complete"
    fi

    # Run on Windows
    log "Launching on Windows..."
    # Use Start-Process to detach from SSH session (survives SSH disconnect)
    win_user=$(echo "$WIN_SSH" | cut -d@ -f1)
    # Delete old results, then launch
    WIN_RESULT_DIR="C:/Users/$win_user/AppData/LocalLow/BaseCityTest/curl-unity-test"
    WIN_RESULT_FILE="$WIN_RESULT_DIR/curl_test_results.txt"
    ssh "$WIN_SSH" "powershell Remove-Item '$WIN_RESULT_FILE' -ErrorAction SilentlyContinue" 2>/dev/null || true
    ssh "$WIN_SSH" "powershell Start-Process -FilePath $WIN_REMOTE_DIR/curl-unity-test.exe -ArgumentList '-batchmode','-nographics','-autotest' -WindowStyle Hidden"
    log "App started on Windows"

    # Poll Player.log on remote host (more reliable than results file on Windows)
    WIN_PLAYER_LOG="C:\\Users\\$win_user\\AppData\\LocalLow\\BaseCityTest\\curl-unity-test\\Player.log"
    deadline=$(( $(date +%s) + TIMEOUT ))

    while (( $(date +%s) < deadline )); do
        sleep 5
        result=$(ssh -o ConnectTimeout=5 "$WIN_SSH" \
            "powershell -Command \"Get-Content '$WIN_PLAYER_LOG' -ErrorAction SilentlyContinue\"" 2>/dev/null) || true
        if echo "$result" | grep -q '\[CURL_TEST\] END'; then
            echo "$result" | grep '\[CURL_TEST\]' | wait_for_tests
            exit $?
        fi
        echo -n "."
    done
    echo ""

    err "Tests did not complete within ${TIMEOUT}s"
    exit 1
    ;;

# --- Android ---
android)
    APK="$BUILD_DIR/Android/curl-unity-test.apk"
    if [[ ! -f "$APK" ]]; then
        err "Android APK not found at $APK"
        err "Run with --build to create it"
        exit 2
    fi

    # Check device
    if ! "$ADB" devices 2>/dev/null | grep -q 'device$'; then
        err "No Android device connected"
        err "Connect a device with USB debugging enabled"
        exit 3
    fi

    device_name=$("$ADB" shell getprop ro.product.model 2>/dev/null | tr -d '\r')
    log "Device: $device_name"

    # Install
    log "Installing APK..."
    "$ADB" install -r "$APK" || { err "APK install failed"; exit 3; }

    # Clear logcat
    "$ADB" logcat -c

    # Launch with -autotest arg via Unity's intent extra
    log "Launching app..."
    "$ADB" shell am start -n "$PACKAGE_NAME/com.unity3d.player.UnityPlayerActivity" -e unity '"-autotest"'

    # Capture logcat filtered to Unity tag.
    # adb logcat does NOT exit on SIGPIPE when idle (seen after test END when
    # the app stops writing Unity: lines), so we wrap it in stream_to_tests
    # which kills the process explicitly after wait_for_tests returns.
    stream_to_tests "$ADB" logcat -s Unity:V '*:S'
    exit_code=$?

    # Stop app
    "$ADB" shell am force-stop "$PACKAGE_NAME" 2>/dev/null || true

    exit $exit_code
    ;;

# --- iOS ---
ios)
    XCODE_PROJ="$BUILD_DIR/iOS/Unity-iPhone.xcodeproj"
    DERIVED_DATA="$BUILD_DIR/iOS/DerivedData"

    # If we just did a Unity build, also need xcodebuild
    if $DO_BUILD || [[ ! -d "$DERIVED_DATA" ]]; then
        if [[ ! -d "$XCODE_PROJ" ]]; then
            err "iOS Xcode project not found at $XCODE_PROJ"
            err "Run with --build to create it"
            exit 2
        fi

        step "Compiling Xcode project"

        if [[ -z "${UNITY_IOS_TEAM_ID:-}" ]]; then
            err "UNITY_IOS_TEAM_ID is not set"
            err "Set your Apple Developer Team ID: UNITY_IOS_TEAM_ID=XXXXXXXXXX $0 ios --build"
            exit 2
        fi
        TEAM_ID="$UNITY_IOS_TEAM_ID"
        log "Team ID: $TEAM_ID"

        xcodebuild \
            -project "$XCODE_PROJ" \
            -scheme "Unity-iPhone" \
            -configuration Debug \
            -destination 'generic/platform=iOS' \
            -derivedDataPath "$DERIVED_DATA" \
            DEVELOPMENT_TEAM="$TEAM_ID" \
            CODE_SIGN_IDENTITY="Apple Development" \
            CODE_SIGNING_ALLOWED=YES \
            -allowProvisioningUpdates \
            -quiet \
            build || { err "xcodebuild failed"; exit 2; }

        log "Xcode build complete"
    fi

    # Find the .app bundle
    IOS_APP=$(find "$DERIVED_DATA" -name "*.app" -path "*/Debug-iphoneos/*" 2>/dev/null | head -1)
    if [[ -z "$IOS_APP" ]]; then
        err "Cannot find .app bundle in $DERIVED_DATA"
        exit 2
    fi

    log "App bundle: $IOS_APP"

    # Check for ios-deploy
    if ! command -v ios-deploy &>/dev/null; then
        err "ios-deploy not found. Install: brew install ios-deploy"
        exit 3
    fi

    # Check device
    if ! ios-deploy --detect --timeout 5 2>/dev/null | grep -q 'Found'; then
        err "No iOS device connected"
        err "Connect a device via USB and trust this computer"
        exit 3
    fi

    # Detect device UDID
    IOS_UDID=$(idevice_id -l 2>/dev/null | head -1)
    if [[ -z "$IOS_UDID" ]]; then
        # Fallback: try ios-deploy detection
        IOS_UDID=$(ios-deploy --detect --timeout 5 2>/dev/null | grep -o '[0-9a-f]\{40\}' | head -1)
    fi
    log "Device UDID: $IOS_UDID"

    # Install app
    log "Installing app..."
    ios-deploy --bundle "$IOS_APP" --no-wifi --uninstall 2>&1 | grep -E "^\[|Installed" || true

    # Launch via idevicedebug (streams stdout — works on all iOS versions, no DeviceSupport needed)
    if command -v idevicedebug &>/dev/null; then
        log "Launching via idevicedebug (stdout capture)..."
        # idevicedebug, like adb logcat, doesn't always exit on SIGPIPE once
        # the app stops producing output — wrap in stream_to_tests for reliable cleanup.
        if [[ -n "$IOS_UDID" ]]; then
            stream_to_tests idevicedebug -u "$IOS_UDID" -e CURL_UNITY_AUTOTEST=1 run "$PACKAGE_NAME"
        else
            stream_to_tests idevicedebug -e CURL_UNITY_AUTOTEST=1 run "$PACKAGE_NAME"
        fi
        exit_code=$?

        # iOS 上 Application.Quit() 无效，需要主动停止 app
        idevicedebug kill "$PACKAGE_NAME" 2>/dev/null || true

        exit $exit_code
    fi

    # Fallback: no idevicedebug, use file-based result collection
    log "idevicedebug not found, falling back to file-based results..."
    log "Please TAP the app icon 'curl-unity-test' on the device."

    pull_dir="$BUILD_DIR/ios-pull"
    deadline=$(( $(date +%s) + TIMEOUT ))

    while (( $(date +%s) < deadline )); do
        sleep 5
        rm -rf "$pull_dir"
        ios-deploy --bundle_id "$PACKAGE_NAME" --no-wifi --download=/Documents \
            --to "$pull_dir" 2>/dev/null || true
        result_file=$(find "$pull_dir" -name "curl_test_results.txt" 2>/dev/null | head -1)
        if [[ -n "$result_file" ]] && grep -q '\[CURL_TEST\] END' "$result_file" 2>/dev/null; then
            log "Results file retrieved from device"
            break
        fi
        echo -n "."
    done
    echo ""

    if [[ -z "$result_file" ]] || ! grep -q '\[CURL_TEST\] END' "$result_file" 2>/dev/null; then
        err "Tests did not complete within ${TIMEOUT}s"
        [[ -n "$result_file" ]] && cat "$result_file" >&2
        exit 1
    fi

    wait_for_tests < "$result_file"
    exit $?
    ;;

*)
    err "Unknown platform: $PLATFORM"
    exit 1
    ;;
esac
