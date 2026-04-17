# 真机自动化测试指南

curl-unity 提供了一套完整的真机自动化测试流水线，覆盖 macOS、iOS、Android、Windows 四个平台。一条命令即可完成 **同步 CI 产物 → Unity 构建 → 设备部署 → 运行测试 → 采集结果** 的完整闭环。

## 快速开始

```bash
# macOS — 最快的验证方式，无需外部设备
./scripts/device-test.sh macos --build

# Android — 连接设备或启动模拟器
./scripts/device-test.sh android --build

# iOS — 需要 USB 连接 iOS 设备
UNITY_IOS_TEAM_ID=<your_team_id> ./scripts/device-test.sh ios --build

# Windows — 通过 SSH 远程测试（macOS 交叉编译 + 远程运行）
WIN_SSH=user@host ./scripts/device-test.sh windows --build
```

## 前置条件

### 通用

| 工具 | 用途 | 安装方式 |
|------|------|---------|
| Unity 2022.3.x | 构建测试 app | Unity Hub |
| Unity 平台 Build Support | 对应平台构建模块 (iOS/Android/Windows Mono) | Unity Hub → Installs → Add modules |
| gh (GitHub CLI) | 从 CI 下载构建产物 | `brew install gh` + `gh auth login` |

### Android

| 工具 | 用途 | 安装方式 |
|------|------|---------|
| ADB | 设备通信 | Android SDK Platform Tools |

ADB 路径默认为 `/Users/ly/bin/Android/sdk/platform-tools/adb`，可通过 `ADB` 环境变量覆盖。

### iOS

| 工具 | 用途 | 安装方式 |
|------|------|---------|
| Xcode 15+ | 编译 Xcode 工程 | App Store |
| ios-deploy | 安装 app 到设备 | `brew install ios-deploy` |
| libimobiledevice | 启动 app + 日志捕获 | `brew install libimobiledevice` |
| Apple Development 证书 | 代码签名 | Xcode → Settings → Accounts |

> **关于 libimobiledevice**: 这是实现 iOS 全自动化的关键工具。Xcode 自带的 `devicectl` 仅支持 iOS 17+ 设备，`ios-deploy --debug` 依赖 Xcode DeviceSupport 符号（新版 Xcode 不附带旧 iOS 的符号）。`libimobiledevice` 的 `idevicedebug` 独立于 Xcode 实现设备通信，在所有 iOS 版本上都能启动 app 并捕获 stdout。

### Windows

Windows 采用**远程测试模式**：在 macOS 上交叉编译 Windows Standalone（Mono 后端），通过 SSH 部署到 Windows 机器运行。

| 条件 | 说明 |
|------|------|
| SSH 访问 | Windows 机器需开启 OpenSSH Server，配置好密钥登录 |
| 网络访问 | Windows 机器需能访问外网 (httpbin.org) |
| Windows Defender | 需将测试目录加入排除项（未签名的 Unity exe 会被实时删除） |
| Unity 模块 | macOS 端需安装 "Windows Build Support (Mono)" |

> **为什么用 Mono 而非 IL2CPP？** macOS 无法交叉编译 Windows IL2CPP（需要 MSVC 工具链）。Mono 后端足以验证 native DLL + P/Invoke 的正确性，与 IL2CPP 行为一致。

SSH 连接默认为 `developer@192.168.0.170`，可通过 `WIN_SSH` 环境变量覆盖。

## 脚本说明

### `scripts/device-test.sh` — 主入口

```
用法: ./scripts/device-test.sh <platform> [options]

平台:
  macos       在本机构建运行
  android     部署到 Android 设备/模拟器
  ios         部署到 iOS 真机
  windows     macOS 交叉编译，SSH 部署到 Windows 运行

选项:
  --build         执行 Unity 构建（不传则使用已有构建产物）
  --skip-sync     跳过插件同步步骤
  --local         使用本地编译产物而非 CI 产物
  --timeout <s>   等待测试完成的超时秒数（默认 120）
  --unity <ver>   指定 Unity 版本（默认 2022.3.62f3）
```

**退出码**: 0 = 全部通过, 1 = 有失败或超时, 2 = 构建失败, 3 = 设备未连接

### `scripts/sync-ci-plugins.sh` — CI 产物同步

从 GitHub Actions 下载最新一次成功构建的产物，同步到 Unity Package 的 Plugins 目录。

```bash
# 最新成功构建
./scripts/sync-ci-plugins.sh

# 指定 run ID
./scripts/sync-ci-plugins.sh --run 24397451835
```

## 执行流程

```
device-test.sh <platform> --build
        │
        ├─ 1. sync-ci-plugins.sh       从 CI 下载最新 native 库到 Plugins/
        │
        ├─ 2. Unity CLI                 batchmode 构建测试 app
        │     -executeMethod AutoTestBuilder.Build<Platform>
        │     自动注入 CURL_UNITY_AUTOTEST 编译宏
        │
        ├─ 3. 平台特定部署
        │     macOS:   open app.app --args -autotest
        │     Android: adb install + am start -e unity "-autotest"
        │     iOS:     ios-deploy --bundle + idevicedebug run -autotest
        │     Windows: sftp + SSH Start-Process -autotest
        │
        ├─ 4. 日志采集
        │     macOS:   tail -f ~/Library/Logs/.../Player.log
        │     Android: adb logcat -s Unity:V
        │     iOS:     idevicedebug stdout 直接输出
        │     Windows: SSH 轮询 Player.log
        │
        └─ 5. 结果解析
              grep [CURL_TEST] → 等待 END 行 → 输出汇总
```

## 测试用例

AutoTestRunner 包含 12 个测试，覆盖 HTTP 客户端的核心功能：

| 测试 | 验证目标 | 端点 | 平台差异 |
|------|---------|------|---------|
| GET_Basic | 基础 GET 请求，200 响应，body 解析 | httpbin.org/get | — |
| POST_Json | JSON POST + 回显验证 | httpbin.org/post | — |
| **HTTPS_Verify** | **CA 证书链验证** | www.example.com | macOS/iOS: SecTrust; Android: JNI 系统证书; Windows: CryptoAPI (CURLSSLOPT_NATIVE_CA) |
| HTTP2 | HTTP/2 ALPN 协商 | httpbin.org/get | — |
| ResponseHeaders | 响应头解析，自定义 header 验证 | httpbin.org/response-headers | — |
| Redirect | 重定向跟随，RedirectCount 验证 | httpbin.org/redirect/3 | — |
| Timeout | 超时回调 (CURLE_OPERATION_TIMEDOUT) | httpbin.org/delay/30 | — |
| Cancel | CancellationToken 取消 | httpbin.org/delay/30 | — |
| LargeResponse | 100KB 下载完整性 | httpbin.org/bytes/102400 | — |
| Concurrent | 5 并发请求 | httpbin.org/get ×5 | — |
| ConnectionReuse | 连接复用率 > 0 | httpbin.org/get ×5 | — |
| DnsFailure | 无效域名错误处理 | *.invalid | DNS 劫持环境下错误码可能不同 |

> **HTTPS_Verify 是最重要的平台差异测试** — 各平台的证书验证实现完全不同：macOS/iOS 使用 Apple SecTrust（编译时启用 `USE_APPLE_SECTRUST`），Android 通过 JNI 调用 `TrustManagerFactory` 提取系统证书并缓存为 PEM 文件，Windows 通过 `CURLSSLOPT_NATIVE_CA` 让 curl 经由 CryptoAPI 访问系统证书库。这个测试在真机上才能有效验证。

## 日志协议

测试输出通过 `Debug.Log` 以 `[CURL_TEST]` 前缀标记，方便从设备日志中 grep：

```
[CURL_TEST] BEGIN version=1 platform=Android count=12
[CURL_TEST] RUN GET_Basic
[CURL_TEST] PASS GET_Basic 342ms
[CURL_TEST] FAIL HTTPS_Verify 1503ms unable to get local issuer certificate
[CURL_TEST] SKIP HTTP3 network does not support QUIC
[CURL_TEST] DIAG total_requests=6 success=5 reuse_rate=0.67 avg_ttfb_ms=339.4
[CURL_TEST] END passed=11 failed=1 skipped=0 total_ms=8234
```

iOS 上还会同时将结果写入 `Application.persistentDataPath/curl_test_results.txt`，作为日志捕获失败时的备用回传通道（通过 `ios-deploy --download` 拉取）。

## 平台特定说明

### macOS

最简单的验证方式，无需外部设备。构建产物为 `.app`，直接运行。日志从 Unity Player.log 文件 tail。

```bash
./scripts/device-test.sh macos --build
```

### Android

支持真机和模拟器。通过 ADB 安装 APK 并用 logcat 采集日志。

```bash
# 真机（USB 连接，开启 USB 调试）
./scripts/device-test.sh android --build

# 模拟器（无线 ADB）
adb connect 127.0.0.1:5555
./scripts/device-test.sh android --build

# 指定 ADB 路径
ADB=/path/to/adb ./scripts/device-test.sh android --build
```

### iOS

流程分为三步：Unity 生成 Xcode 工程 → xcodebuild 编译签名 → 设备部署运行。

```bash
# 首次需要指定 Team ID（后续可设为环境变量）
export UNITY_IOS_TEAM_ID=6387VP2XPL
./scripts/device-test.sh ios --build
```

**查找你的 Team ID**:
```bash
# 方法 1: 从 Xcode 偏好读取
defaults read com.apple.dt.Xcode IDEProvisioningTeams

# 方法 2: 从签名证书读取
security find-identity -v -p codesigning
```

**iOS 工具链对比**:

| 工具 | 安装 app | 启动 app | 日志捕获 | 旧 iOS 支持 |
|------|---------|---------|---------|------------|
| Xcode (GUI) | ✅ | ✅ | ✅ | ✅ |
| devicectl | ❌ | ❌ | ❌ | ❌ (仅 iOS 17+) |
| ios-deploy | ✅ | ⚠️ 需要 DeviceSupport | ⚠️ 同上 | ❌ |
| **idevicedebug** | ❌ | **✅** | **✅ (stdout)** | **✅** |

脚本组合使用 `ios-deploy`（安装）+ `idevicedebug`（启动+日志），实现全 iOS 版本覆盖。

### Windows

通过 SSH 远程测试。在 macOS 上交叉编译 Windows Standalone（Mono），传输到 Windows 机器运行。

```bash
# 指定 SSH 目标
WIN_SSH=developer@192.168.0.170 ./scripts/device-test.sh windows --build

# 默认 SSH 目标（脚本内配置）
./scripts/device-test.sh windows --build
```

**Windows 执行流程**：
1. macOS 上 Unity 交叉编译 → `Build/Windows/curl-unity-test.exe`
2. `tar` 压缩 + `sftp` 传输到 Windows（`scp` 在 Windows OpenSSH 上不可靠）
3. SSH 执行 `Start-Process`（脱离 SSH 会话，app 不会随 SSH 断连而终止）
4. 轮询 Windows 端的 `Player.log`，检测 `[CURL_TEST] END`

**注意事项**：
- Windows Defender 会删除未签名的 Unity exe。首次使用需添加排除项：
  ```powershell
  # 在 Windows 上以管理员身份运行
  Add-MpPreference -ExclusionPath 'C:\Users\<user>\curl-unity-test'
  ```
- SSH 传输使用 `sftp`（`scp` 在部分 Windows OpenSSH 版本上大文件传输不稳定）

## 文件结构

```
demo/curl-unity/Assets/
├── AutoTest/
│   └── AutoTestRunner.cs       # 测试运行器 (#if CURL_UNITY_AUTOTEST)
└── Editor/
    └── AutoTestBuilder.cs      # Unity CLI 构建脚本

scripts/
├── device-test.sh              # 主入口：构建 → 部署 → 采集 → 报告
├── sync-ci-plugins.sh          # 从 GitHub Actions 下载 CI 产物
└── sync-plugins.sh             # 从本地 output/ 同步产物
```

### AutoTestRunner.cs

通过 `[RuntimeInitializeOnLoadMethod]` 自动注入，无需修改场景。双重保护机制：
1. **编译期**：`#if CURL_UNITY_AUTOTEST` 宏保护，正常构建不编译此代码
2. **运行期**：需要 `-autotest` 命令行参数，双击/点击启动不会触发测试。各平台传参方式：macOS/Windows 直接传参，Android 通过 intent extra `unity`，iOS 通过 idevicedebug args

### AutoTestBuilder.cs

提供四个静态方法供 Unity CLI 调用：
- `AutoTestBuilder.BuildMacOS`
- `AutoTestBuilder.BuildWindows` — Mono 后端，支持 macOS 交叉编译
- `AutoTestBuilder.BuildAndroid`
- `AutoTestBuilder.BuildiOS`

## 验证结果示例

以下为实际测试结果（2026-04-15）：

```
┌──────────┬──────────────────────┬──────────────┬────────┬───────┐
│ Platform │ Device               │ OS           │ Result │ Time  │
├──────────┼──────────────────────┼──────────────┼────────┼───────┤
│ macOS    │ MacBook Pro          │ Sequoia      │ 12/12  │ 61.0s │
│ iOS      │ iPad mini 2 (A7)     │ iOS 12.5.7   │ 12/12  │ 12.4s │
│ Android  │ SM-G998B (emulator)  │ Android 13   │ 12/12  │ 11.8s │
│ Windows  │ hjdclinet-win (SSH)  │ Windows 11   │ 12/12  │  9.9s │
└──────────┴──────────────────────┴──────────────┴────────┴───────┘
```

## 故障排查

### Unity 构建失败
```bash
# 确认 Unity 编辑器未打开同一项目（会冲突）
# 查看详细构建日志
cat demo/curl-unity/Build/unity-build-<platform>.log | grep -i error
```

### Android 设备未检测到
```bash
adb devices                    # 确认设备在列
adb shell getprop ro.build.version.release  # 确认 Android 版本
```

### iOS 签名错误
```bash
# 确认证书有效
security find-identity -v -p codesigning

# 确认 Team ID 正确
UNITY_IOS_TEAM_ID=<id> ./scripts/device-test.sh ios --build
```

### iOS app 无法自动启动
```bash
# 确认 libimobiledevice 已安装
which idevicedebug

# 手动测试启动
idevicedebug run com.basecity.curlunitytest
```

### Windows exe 被删除
```powershell
# Windows Defender 实时扫描会删除未签名 exe。添加排除路径：
Add-MpPreference -ExclusionPath 'C:\Users\developer\curl-unity-test'
```

### Windows SSH 传输失败
```bash
# scp 在部分 Windows OpenSSH 上不稳定，脚本已使用 sftp 替代
# 如果 sftp 也失败，检查 Windows OpenSSH 服务状态：
ssh user@host "powershell Get-Service sshd"
```

### 测试超时（120s 内未完成）
- 检查设备网络连接（测试依赖 httpbin.org）
- 增加超时时间：`--timeout 300`
- 查看部分结果：`cat demo/curl-unity/Build/test-results-<platform>.log`
