@echo off
REM ============================================================================
REM build.bat — compile AoeP2pHook.dll (Win32 x86) for injection into age3y.exe.
REM
REM Usage:
REM   1. Open "Developer Command Prompt for VS 2026" (Start menu),
REM      OR have %comspec% with vcvarsall.bat x86 already sourced.
REM   2. cd to this folder.
REM   3. Run:  build.bat
REM
REM Output:  bin\AoeP2pHook.dll  (x86, ~200 KB)
REM
REM We compile Detours sources straight into our DLL instead of pre-building
REM a static lib. Detours is small enough and this keeps the build a single
REM cl.exe invocation — no nmake, no detours.lib to manage.
REM ============================================================================

setlocal enabledelayedexpansion

REM Sanity: cl.exe must be on PATH (i.e. vcvarsall x86 already ran).
where /q cl.exe
if errorlevel 1 (
    echo.
    echo ERROR: cl.exe not found on PATH.
    echo Open "Developer Command Prompt for VS" ^(x86^) and re-run this script.
    echo.
    exit /b 1
)

set "SCRIPT_DIR=%~dp0"
set "DETOURS_DIR=%SCRIPT_DIR%..\..\third_party\Detours"
set "OUT_DIR=%SCRIPT_DIR%bin"
set "OBJ_DIR=%OUT_DIR%\obj"

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
if not exist "%OBJ_DIR%" mkdir "%OBJ_DIR%"

REM Detours sources needed for x86 builds. We pick the x86 disassembler only
REM (disolx86.cpp) because age3y.exe is 32-bit; the other arch variants would
REM bloat the DLL with code we'd never run.
set DETOURS_SRC=
set DETOURS_SRC=%DETOURS_SRC% "%DETOURS_DIR%\src\detours.cpp"
set DETOURS_SRC=%DETOURS_SRC% "%DETOURS_DIR%\src\modules.cpp"
set DETOURS_SRC=%DETOURS_SRC% "%DETOURS_DIR%\src\disasm.cpp"
set DETOURS_SRC=%DETOURS_SRC% "%DETOURS_DIR%\src\image.cpp"
set DETOURS_SRC=%DETOURS_SRC% "%DETOURS_DIR%\src\creatwth.cpp"

echo.
echo === Compiling AoeP2pHook.dll (Win32 x86) ===
echo.

cl.exe /nologo ^
    /LD ^
    /O2 ^
    /MD ^
    /EHsc ^
    /std:c++17 ^
    /W3 ^
    /I"%DETOURS_DIR%\src" ^
    /D_WIN32_WINNT=0x0601 ^
    /DWIN32 ^
    /D_USRDLL ^
    /D_WINDLL ^
    /DDETOURS_X86 ^
    /Fo"%OBJ_DIR%\\" ^
    /Fe:"%OUT_DIR%\AoeP2pHook.dll" ^
    "%SCRIPT_DIR%src\dllmain.cpp" ^
    %DETOURS_SRC% ^
    /link ^
        /DLL ^
        /MACHINE:X86 ^
        /SUBSYSTEM:WINDOWS ^
        ws2_32.lib kernel32.lib user32.lib advapi32.lib

if errorlevel 1 (
    echo.
    echo === BUILD FAILED ===
    exit /b 1
)

echo.
echo === BUILD OK ===
for %%F in ("%OUT_DIR%\AoeP2pHook.dll") do echo Output: %%F  ^(%%~zF bytes^)
echo.
echo Next steps:
echo   * Test injection: launcher will CreateProcess(SUSPENDED) + LoadLibrary remoto.
echo   * Verify: %%LOCALAPPDATA%%\AoeP2pHook.log should appear on next AoE3 launch.
exit /b 0
