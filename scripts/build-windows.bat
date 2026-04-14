@echo off
REM ============================================================
REM libcurl Windows 构建脚本 (MSVC)
REM
REM 用法:
REM   从 "x64 Native Tools Command Prompt" 运行编译 64 位
REM   从 "x86 Native Tools Command Prompt" 运行编译 32 位
REM
REM 选项:
REM   --clean    清理该架构的 build 目录后重新编译
REM
REM 前提:
REM   Visual Studio 2019+ (C++ 桌面开发工作负载)
REM   CMake, Ninja, Perl (用于 OpenSSL), Git
REM ============================================================
setlocal enabledelayedexpansion

REM ============================================================
REM 项目路径
REM ============================================================
set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%\..") do set "PROJECT_ROOT=%%~fi"
set "DEPS_SRC=%PROJECT_ROOT%\deps"
set "CURL_SRC=%DEPS_SRC%\curl"
set "BRIDGE_SRC=%PROJECT_ROOT%\bridge\curl_unity_bridge.c"
set "OUTPUT_DIR=%PROJECT_ROOT%\output"

REM ============================================================
REM 检测 MSVC 目标架构
REM ============================================================
set "ARCH="
cl 2>&1 | findstr /C:"for x64" >nul 2>&1 && set "ARCH=x64"
if not defined ARCH (
    cl 2>&1 | findstr /C:"for x86" >nul 2>&1 && set "ARCH=x86"
)
if not defined ARCH (
    cl 2>&1 | findstr /C:"for ARM64" >nul 2>&1 && set "ARCH=arm64"
)
if not defined ARCH (
    echo [ERROR] 无法检测 MSVC 目标架构
    echo 请从 "x64 Native Tools Command Prompt" 或 "x86 Native Tools Command Prompt" 运行此脚本
    exit /b 1
)

if "%ARCH%"=="x64" (
    set "PLATFORM=windows-x64"
    set "OPENSSL_TARGET=VC-WIN64A"
    set "OUTPUT_ARCH=x86_64"
) else if "%ARCH%"=="x86" (
    set "PLATFORM=windows-x86"
    set "OPENSSL_TARGET=VC-WIN32"
    set "OUTPUT_ARCH=x86"
) else (
    echo [ERROR] 不支持的架构: %ARCH%
    exit /b 1
)

set "PREFIX=%PROJECT_ROOT%\build\%PLATFORM%\install"

echo ========================================
echo   libcurl Windows 构建脚本
echo   架构: %ARCH% (%PLATFORM%)
echo   项目: %PROJECT_ROOT%
echo ========================================

REM ============================================================
REM 解析选项
REM ============================================================
set "CLEAN=0"
for %%a in (%*) do (
    if "%%a"=="--clean" set "CLEAN=1"
)
if "%CLEAN%"=="1" (
    echo 清理 build\%PLATFORM% ...
    if exist "%PROJECT_ROOT%\build\%PLATFORM%" rd /s /q "%PROJECT_ROOT%\build\%PLATFORM%"
)

if not exist "%PREFIX%\lib" mkdir "%PREFIX%\lib"
if not exist "%PREFIX%\include" mkdir "%PREFIX%\include"

REM ============================================================
REM 编译 zlib
REM ============================================================
if exist "%PREFIX%\lib\zlibstatic.lib" (
    echo [SKIP] zlib already built
    goto :build_openssl
)

echo.
echo ========================================
echo   [%PLATFORM%] Building zlib
echo ========================================
echo.

set "ZLIB_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\zlib"
if exist "%ZLIB_BUILD%" rd /s /q "%ZLIB_BUILD%"

cmake -B "%ZLIB_BUILD%" -S "%DEPS_SRC%\zlib" -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
    -DBUILD_SHARED_LIBS=OFF
if errorlevel 1 goto :error

cmake --build "%ZLIB_BUILD%"
if errorlevel 1 goto :error

cmake --install "%ZLIB_BUILD%"
if errorlevel 1 goto :error

echo   zlib done.

REM ============================================================
REM 编译 OpenSSL
REM ============================================================
:build_openssl
if exist "%PREFIX%\lib\libssl.lib" if exist "%PREFIX%\lib\libcrypto.lib" (
    echo [SKIP] OpenSSL already built
    goto :build_nghttp2
)

echo.
echo ========================================
echo   [%PLATFORM%] Building OpenSSL
echo ========================================
echo.

set "OPENSSL_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\openssl"
if exist "%OPENSSL_BUILD%" rd /s /q "%OPENSSL_BUILD%"
mkdir "%OPENSSL_BUILD%"
pushd "%OPENSSL_BUILD%"

perl "%DEPS_SRC%\openssl\Configure" %OPENSSL_TARGET% ^
    --prefix=%PREFIX% ^
    --libdir=lib ^
    no-shared ^
    no-tests ^
    no-apps
if errorlevel 1 ( popd & goto :error )

nmake
if errorlevel 1 ( popd & goto :error )

nmake install_sw
if errorlevel 1 ( popd & goto :error )

popd
echo   OpenSSL done.

REM ============================================================
REM 编译 nghttp2
REM ============================================================
:build_nghttp2
if exist "%PREFIX%\lib\nghttp2.lib" (
    echo [SKIP] nghttp2 already built
    goto :build_nghttp3
)

echo.
echo ========================================
echo   [%PLATFORM%] Building nghttp2
echo ========================================
echo.

call :ensure_submodules "%DEPS_SRC%\nghttp2"

set "NGHTTP2_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\nghttp2"
if exist "%NGHTTP2_BUILD%" rd /s /q "%NGHTTP2_BUILD%"

cmake -B "%NGHTTP2_BUILD%" -S "%DEPS_SRC%\nghttp2" -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON ^
    -DENABLE_LIB_ONLY=ON ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DBUILD_STATIC_LIBS=ON
if errorlevel 1 goto :error

cmake --build "%NGHTTP2_BUILD%"
if errorlevel 1 goto :error

cmake --install "%NGHTTP2_BUILD%"
if errorlevel 1 goto :error

echo   nghttp2 done.

REM ============================================================
REM 编译 nghttp3
REM ============================================================
:build_nghttp3
if exist "%PREFIX%\lib\nghttp3.lib" (
    echo [SKIP] nghttp3 already built
    goto :build_ngtcp2
)

echo.
echo ========================================
echo   [%PLATFORM%] Building nghttp3
echo ========================================
echo.

call :ensure_submodules "%DEPS_SRC%\nghttp3"

set "NGHTTP3_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\nghttp3"
if exist "%NGHTTP3_BUILD%" rd /s /q "%NGHTTP3_BUILD%"

cmake -B "%NGHTTP3_BUILD%" -S "%DEPS_SRC%\nghttp3" -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON ^
    -DENABLE_LIB_ONLY=ON ^
    -DENABLE_SHARED_LIB=OFF ^
    -DENABLE_STATIC_LIB=ON
if errorlevel 1 goto :error

cmake --build "%NGHTTP3_BUILD%"
if errorlevel 1 goto :error

cmake --install "%NGHTTP3_BUILD%"
if errorlevel 1 goto :error

echo   nghttp3 done.

REM ============================================================
REM 编译 ngtcp2
REM ============================================================
:build_ngtcp2
if exist "%PREFIX%\lib\ngtcp2.lib" (
    echo [SKIP] ngtcp2 already built
    goto :build_curl
)

echo.
echo ========================================
echo   [%PLATFORM%] Building ngtcp2
echo ========================================
echo.

call :ensure_submodules "%DEPS_SRC%\ngtcp2"

set "NGTCP2_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\ngtcp2"
if exist "%NGTCP2_BUILD%" rd /s /q "%NGTCP2_BUILD%"

cmake -B "%NGTCP2_BUILD%" -S "%DEPS_SRC%\ngtcp2" -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
    -DCMAKE_PREFIX_PATH="%PREFIX%" ^
    -DOPENSSL_ROOT_DIR="%PREFIX%" ^
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON ^
    -DENABLE_OPENSSL=ON ^
    -DENABLE_LIB_ONLY=ON ^
    -DENABLE_SHARED_LIB=OFF ^
    -DENABLE_STATIC_LIB=ON
if errorlevel 1 goto :error

cmake --build "%NGTCP2_BUILD%"
if errorlevel 1 goto :error

cmake --install "%NGTCP2_BUILD%"
if errorlevel 1 goto :error

echo   ngtcp2 done.

REM ============================================================
REM 编译 libcurl (静态库)
REM ============================================================
:build_curl
echo.
echo ========================================
echo   [%PLATFORM%] Building libcurl
echo ========================================
echo.

set "CURL_BUILD=%PROJECT_ROOT%\build\%PLATFORM%\curl"
if exist "%CURL_BUILD%" rd /s /q "%CURL_BUILD%"

cmake -B "%CURL_BUILD%" -S "%CURL_SRC%" -G Ninja ^
    -DCMAKE_BUILD_TYPE=Release ^
    -DCMAKE_INSTALL_PREFIX="%PREFIX%" ^
    -DCMAKE_PREFIX_PATH="%PREFIX%" ^
    -DOPENSSL_ROOT_DIR="%PREFIX%" ^
    -DBUILD_CURL_EXE=OFF ^
    -DBUILD_SHARED_LIBS=OFF ^
    -DBUILD_STATIC_LIBS=ON ^
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON ^
    -DCURL_ENABLE_SSL=ON ^
    -DCURL_USE_OPENSSL=ON ^
    -DUSE_NGHTTP2=ON ^
    -DUSE_NGTCP2=ON ^
    -DHTTP_ONLY=ON ^
    -DCURL_USE_LIBPSL=OFF ^
    -DCURL_USE_LIBSSH2=OFF ^
    -DCURL_USE_LIBSSH=OFF ^
    -DCURL_BROTLI=OFF ^
    -DCURL_ZSTD=OFF ^
    -DUSE_LIBIDN2=OFF ^
    -DCURL_DISABLE_LDAP=ON ^
    -DCURL_DISABLE_LDAPS=ON ^
    -DCURL_DISABLE_AWS=ON ^
    -DCURL_DISABLE_KERBEROS_AUTH=ON ^
    -DCURL_DISABLE_NEGOTIATE_AUTH=ON ^
    -DCURL_DISABLE_VERBOSE_STRINGS=OFF ^
    -DCURL_LTO=ON
if errorlevel 1 goto :error

cmake --build "%CURL_BUILD%"
if errorlevel 1 goto :error

cmake --install "%CURL_BUILD%"
if errorlevel 1 goto :error

echo   libcurl done.

REM ============================================================
REM 编译 bridge + 收集产物 → libcurl_unity.dll
REM ============================================================
echo.
echo ========================================
echo   [%PLATFORM%] Building bridge + DLL
echo ========================================
echo.

set "DLL_OUT=%OUTPUT_DIR%\Windows\%OUTPUT_ARCH%"
if not exist "%DLL_OUT%" mkdir "%DLL_OUT%"

cl /O2 /LD /MD ^
    /I"%PREFIX%\include" ^
    "%BRIDGE_SRC%" ^
    /Fe:"%DLL_OUT%\libcurl_unity.dll" ^
    /link ^
    /WHOLEARCHIVE:"%PREFIX%\lib\libcurl.lib" ^
    "%PREFIX%\lib\libssl.lib" ^
    "%PREFIX%\lib\libcrypto.lib" ^
    "%PREFIX%\lib\nghttp2.lib" ^
    "%PREFIX%\lib\ngtcp2.lib" ^
    "%PREFIX%\lib\ngtcp2_crypto_ossl.lib" ^
    "%PREFIX%\lib\nghttp3.lib" ^
    "%PREFIX%\lib\zlibstatic.lib" ^
    ws2_32.lib crypt32.lib advapi32.lib bcrypt.lib
if errorlevel 1 goto :error

REM 清理编译中间文件
del /q "%DLL_OUT%\curl_unity_bridge.obj" 2>nul
del /q "%DLL_OUT%\libcurl_unity.exp" 2>nul

echo   -^> %DLL_OUT%\libcurl_unity.dll
echo.
echo ========================================
echo   Build complete! (%PLATFORM%)
echo ========================================
echo   Install:  %PREFIX%
echo   Output:   %DLL_OUT%\libcurl_unity.dll
exit /b 0

REM ============================================================
REM 工具函数
REM ============================================================

:ensure_submodules
set "_repo=%~1"
if not exist "%_repo%\.gitmodules" goto :eof
pushd "%_repo%"
git submodule status | findstr /B /C:"-" >nul 2>&1 && (
    echo   Initializing submodules: %~nx1
    git submodule update --init --depth 1
)
popd
goto :eof

:error
echo.
echo [ERROR] Build failed!
exit /b 1
