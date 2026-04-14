@echo off
REM Shortcut: auto-locate VS environment and build Windows x64
REM You can also run build-windows.bat directly from "x64 Native Tools Command Prompt"
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"

REM Locate vcvarsall.bat
set "VCVARSALL="
for %%v in (2022 2019 2017) do (
    for %%e in (Enterprise Professional Community BuildTools) do (
        set "_try=C:\Program Files\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvarsall.bat"
        if exist "!_try!" (
            set "VCVARSALL=!_try!"
            goto :found
        )
        set "_try=C:\Program Files ^(x86^)\Microsoft Visual Studio\%%v\%%e\VC\Auxiliary\Build\vcvarsall.bat"
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

echo [ERROR] Visual Studio not found. Install VS 2017+ or run build-windows.bat from "x64 Native Tools Command Prompt".
exit /b 1

:found
echo Using: %VCVARSALL%
call "%VCVARSALL%" x64
if errorlevel 1 (
    echo [ERROR] vcvarsall.bat failed
    exit /b 1
)

call "%SCRIPT_DIR%build-windows.bat" %*
