# Phase 1 总结：Native 库构建

## 目标

将 libcurl 及其全部依赖（OpenSSL、nghttp2、nghttp3、ngtcp2、zlib）编译为跨平台的单文件原生库，供 Unity3D 通过 P/Invoke 调用，支持 HTTP/2 和 HTTP/3 (QUIC)。

## 最终成果

### 支持平台与产物

| 平台 | 架构 | 产物 | 大小 |
|------|------|------|------|
| macOS | ARM64 | `libcurl_unity.dylib` | 6.6 MB |
| macOS | x86_64 | `libcurl_unity.dylib` | 7.1 MB |
| macOS | Universal (ARM64 + x86_64) | `libcurl_unity.dylib` | 14 MB |
| iOS | ARM64 | `libcurl_unity.a` (静态库) | 13 MB |
| Android | arm64-v8a | `libcurl_unity.so` | 8.9 MB |
| Android | armeabi-v7a | `libcurl_unity.so` | 6.8 MB |
| Android | x86_64 | `libcurl_unity.so` | 9.1 MB |
| Windows | x64 | `libcurl_unity.dll` | 5.9 MB |
| Windows | x86 | `libcurl_unity.dll` | 4.0 MB |

共 **4 平台 9 个架构**，每个平台只需分发一个文件。

### 依赖版本

| 库 | 版本 | 许可证 |
|---|---|---|
| curl | 8.20.0-DEV | curl License (MIT-like) |
| OpenSSL | 3.6.3 | Apache 2.0 |
| nghttp2 | 1.68.1 | MIT |
| nghttp3 | 1.15.0 | MIT |
| ngtcp2 | 1.22.0 | MIT |
| zlib | 1.3.2 | zlib License |

所有依赖均为 git submodule 管理，编译为静态库后合入单个动态库。

## 构建架构

```
deps/ (git submodules)
  │
  ├─ OpenSSL    ─┐
  ├─ nghttp2    ─┤
  ├─ nghttp3    ─┤  全部编译为静态库 (.a / .lib)
  ├─ ngtcp2     ─┤
  ├─ zlib       ─┘
  │                 ↘
  └─ curl       ──→ libcurl.a / libcurl.lib (静态库)
                        ↘
bridge/                  +
  curl_unity_bridge.c ──→ 链接为单个动态库
                          libcurl_unity.dylib / .so / .dll
```

### Bridge 层

ARM64 ABI 对 variadic 和 fixed 参数使用不同的传参方式。libcurl 的 `curl_easy_setopt`、`curl_easy_getinfo`、`curl_multi_setopt` 是 variadic 函数，无法通过 P/Invoke 直接调用。`bridge/curl_unity_bridge.c` 将它们包装为 fixed-argument 函数（14 个 bridge 函数）。非 variadic 的 curl 函数可直接 P/Invoke。

Windows 平台额外需要 `bridge/exports.def` 文件显式声明 DLL 导出符号（Windows DLL 默认不导出非 `__declspec(dllexport)` 的符号）。

## 构建脚本

### 脚本体系

采用"主入口 + 单架构快捷 + 平台聚合"三层结构：

```
scripts/
├── build.sh                    # 主入口 (macOS/iOS/Android 全平台)
│
├── build-macos-arm64.sh        # ┐
├── build-macos-x86_64.sh       # ├ macOS 单架构
├── build-macos.sh              # ┘ 聚合: arm64 + x86_64 → lipo universal
│
├── build-ios-arm64.sh          # ┐
├── build-ios.sh                # ┘ 聚合 (目前仅 arm64)
│
├── build-android-arm64.sh      # ┐
├── build-android-armv7.sh      # ├ Android 单架构
├── build-android-x86_64.sh     # ┘
├── build-android.sh            # 聚合: armv7 + arm64 + x86_64
│
├── build-windows.bat           # Windows 主脚本 (自动检测架构)
├── build-windows-x64.bat       # ┐ Windows 单架构
├── build-windows-x86.bat       # ┘ (自动探测 VS 环境)
│
├── build-curl-cli.sh           # curl CLI 测试工具
├── sync-plugins.sh             # 本地产物 → Unity Plugins 同步
└── sync-ci-plugins.sh          # CI 产物 → Unity Plugins 同步
```

### 关键设计决策

| 决策 | 理由 |
|------|------|
| 全部静态链接到单文件 | 简化分发，避免动态库依赖链问题 |
| iOS 输出静态库 (.a) | Apple 不允许第三方动态库 |
| Windows 用 MSVC 而非 MinGW | Unity IL2CPP 使用 MSVC 的 CRT，混用会导致堆管理崩溃 |
| OpenSSL Windows 加 `no-asm` | 避免 NASM 依赖，HTTP 客户端场景性能影响可忽略 |
| OpenSSL Windows 用 `jom` 替代 `nmake` | nmake 不支持并行构建，jom 可显著缩短编译时间 |
| out-of-source build | 构建产物不污染 submodule 源码目录 |
| bridge 用 `int64_t` 替代 C `long` | Windows LLP64 模型下 C `long` 仅 4 字节，与 C# `long` (8字节) 不匹配 |

## CI/CD

GitHub Actions 全平台自动构建，tag 触发：

```yaml
# .github/workflows/build.yml
on:
  push:
    tags: ['v*']        # 打 tag 时构建
  workflow_dispatch:     # 手动触发

# .github/workflows/test.yml
on:
  push:
    branches: [master]  # push 时跑测试
  pull_request:
    branches: [master]
```

### CI 矩阵

| Runner | 平台 | 架构 |
|--------|------|------|
| macos-latest | macOS | arm64, x86_64 |
| macos-latest | iOS | arm64 |
| ubuntu-latest | Android | arm64, armv7, x86_64 |
| windows-latest | Windows | x64, x86 |

每个构建包含：
1. 依赖缓存（按 submodule commit hash）
2. 完整编译
3. 构建验证（macOS/Windows: smoke test 链接运行；iOS/Android: 符号检查）
4. 产物上传为 GitHub Artifacts

## 解决的关键问题

### Windows 平台（耗时最多）

Windows 构建从零开始搭建，经历了多轮修复：

1. **OpenSSL 并行编译** — `nmake` 不支持并行 → 引入 `jom`
2. **OpenSSL PDB 冲突** — 并行编译时 PDB 文件锁定 → 加 `/FS` 编译标志
3. **zlib 库名不匹配** — CMake 生成 `zs.lib`，curl 期望不同命名 → 统一路径
4. **静态库宏定义** — curl 编译时未定义 `NGHTTP2_STATICLIB` 等 → `__imp_` 符号错误 → 在 CMAKE_C_FLAGS 中添加
5. **系统库缺失** — OpenSSL 需要 `user32.lib`、`iphlpapi.lib` → 逐个排查添加
6. **DLL 导出** — curl 核心函数未从 DLL 导出 → 创建 `exports.def` 文件
7. **Bridge `long` 类型** — C `long` 在 Windows 为 4 字节 → 改用 `int64_t`

### 跨平台一致性

- **NDK 宿主检测** — build.sh 自动检测 `darwin-x86_64` / `linux-x86_64`，支持 macOS 和 Linux CI
- **submodule 精简** — 只初始化构建必需的嵌套 submodule（nghttp3/lib/sfparse），跳过测试框架
- **OpenSSL out-of-source** — 所有平台统一使用 out-of-source build，避免源码目录污染

## 开源合规

已完成许可证分析并创建 `THIRD_PARTY_NOTICES.txt`，包含全部 6 个依赖库的许可证文本。分发包含本库的应用时需附带此文件。

| 库 | 许可证 | 二进制分发要求 |
|---|---|---|
| curl | curl License | 附带版权声明 |
| OpenSSL | Apache 2.0 | 附带许可证全文 |
| nghttp2/nghttp3/ngtcp2 | MIT | 附带版权声明 |
| zlib | zlib License | 无强制要求（建议致谢） |

## 提交历史

共 21 个 commit，按时间顺序：

| 阶段 | Commits | 说明 |
|------|---------|------|
| 初始化 | 1 | 仓库初始化 + submodule 依赖 + 构建脚本 |
| CI 搭建 | 2 | GitHub Actions 全平台构建 |
| Windows 修复 | 11 | 从零到完整的 Windows MSVC 构建链 |
| CI 优化 | 1 | push 跑测试、tag 触发构建 |
| 文档 | 3 | README、BUILD_GUIDE |
| 架构扩展 | 2 | macOS x86_64、Android x86_64、脚本重组 |

## 下一步 (Phase 2)

Phase 1 产出的 native 库是 Phase 2 (Unity Package) 的基础。Unity Package 将提供：
- P/Invoke 绑定层
- async/await HTTP 客户端 API
- 各平台 CA 证书管理
- 连接诊断统计
- UPM 包发布
