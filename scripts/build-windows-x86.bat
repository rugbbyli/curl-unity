@echo off
REM 快捷脚本: 自动定位 VS 环境并编译 Windows x86
REM 也可以直接从 "x86 Native Tools Command Prompt" 运行 build-windows.bat
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"

REM 尝试定位 vcvarsall.bat
set "VCVARSALL="
for %%v in (2022 2019) do (
    for %%e in (Enterprise Professional Community BuildTools) do (
        set "_try=C:\Program Files\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvarsall.bat"
        if exist "!_try!" (
            set "VCVARSALL=!_try!"
            goto :found
        )
    )
)

REM Fallback: vswhere
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath 2^>nul`) do (
    set "_try=%%i\VC\Auxiliary\Build\vcvarsall.bat"
    if exist "!_try!" (
        set "VCVARSALL=!_try!"
        goto :found
    )
)

echo [ERROR] 未找到 Visual Studio。请安装 VS 2019+ 或从 "x86 Native Tools Command Prompt" 手动运行 build-windows.bat
exit /b 1

:found
echo 使用: %VCVARSALL%
call "%VCVARSALL%" x86
if errorlevel 1 (
    echo [ERROR] vcvarsall.bat 初始化失败
    exit /b 1
)

call "%SCRIPT_DIR%build-windows.bat" %*
