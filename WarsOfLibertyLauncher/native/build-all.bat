@echo off
REM ============================================================================
REM build-all.bat — build BOTH native artefacts at once.
REM   1. AoeP2pInjector.exe  (x86, ~30 KB)  — process-suspended launcher + LL inject
REM   2. AoeP2pHook.dll       (x86, ~200 KB) — Detours hooks for ws2_32
REM
REM Both targets are x86 because age3y.exe is x86; CreateRemoteThread cross-
REM bitness is more trouble than maintaining two source trees would be.
REM
REM Prerequisite: open "Developer Command Prompt for VS" (x86 architecture)
REM and run this script. The script verifies cl.exe is present before
REM starting.
REM ============================================================================

setlocal enabledelayedexpansion

where /q cl.exe
if errorlevel 1 (
    echo.
    echo ERROR: cl.exe not on PATH. Open "Developer Command Prompt for VS" ^(x86^) first.
    echo.
    exit /b 1
)

set "ROOT=%~dp0"

echo.
echo === [1/2] AoeP2pInjector.exe (x86) ===
echo.
pushd "%ROOT%AoeP2pInjector"
if not exist "bin" mkdir "bin"
if not exist "bin\obj" mkdir "bin\obj"

cl.exe /nologo ^
    /O2 /MD /EHsc /W3 /std:c++17 ^
    /D_WIN32_WINNT=0x0601 /DWIN32 /D_UNICODE /DUNICODE ^
    /Fo"bin\obj\\" /Fe:"bin\AoeP2pInjector.exe" ^
    "src\main.cpp" ^
    /link /MACHINE:X86 /SUBSYSTEM:CONSOLE ^
    kernel32.lib user32.lib advapi32.lib
if errorlevel 1 (
    echo BUILD AoeP2pInjector FAILED
    popd
    exit /b 1
)
popd

echo.
echo === [2/2] AoeP2pHook.dll (x86) ===
echo.
call "%ROOT%AoeP2pHook\build.bat"
if errorlevel 1 exit /b 1

echo.
echo === ALL DONE ===
dir "%ROOT%AoeP2pInjector\bin\AoeP2pInjector.exe" "%ROOT%AoeP2pHook\bin\AoeP2pHook.dll" | findstr /v "^$"
exit /b 0
