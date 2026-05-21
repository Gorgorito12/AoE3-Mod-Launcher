// AoeP2pInjector.exe — tiny x86 helper that the .NET launcher shells out
// to whenever it wants to launch age3y.exe with AoeP2pHook.dll injected.
//
// Why a separate helper instead of doing the inject from the .NET launcher
// directly: the launcher is x64, age3y.exe is x86. Cross-bitness
// CreateRemoteThread is doable but tedious — you have to read the target's
// PEB to find the x86 kernel32 base, parse its export table, and stitch
// LoadLibraryW's address from there. A small x86 helper sidesteps all of
// that: it runs in the same bitness as the target, so a plain
// GetProcAddress("LoadLibraryW") gives the right pointer.
//
// Usage (called by the launcher):
//   AoeP2pInjector.exe "<age3y.exe path>" "<dll path>" "<extra args for age3y>"
//
// Exit codes:
//   0  success
//   1  bad args
//   2  CreateProcess failed
//   3  VirtualAllocEx / WriteProcessMemory failed
//   4  CreateRemoteThread failed
//   5  LoadLibraryW inside the target returned 0 (DLL refused to load)
//   6  ResumeThread failed
//
// stdout is reserved for the launcher to parse; we keep it quiet unless
// something useful needs reporting.

#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdio>
#include <cwchar>

// Print to stderr so the launcher can capture it without interfering with
// any future stdout protocol.
static void Err(const wchar_t* fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    vfwprintf(stderr, fmt, ap);
    va_end(ap);
    fputwc(L'\n', stderr);
}

int wmain(int argc, wchar_t* argv[])
{
    if (argc < 3)
    {
        Err(L"Usage: AoeP2pInjector.exe <target-exe> <dll-path> [extra-args]");
        return 1;
    }

    const wchar_t* targetExe = argv[1];
    const wchar_t* dllPath   = argv[2];
    const wchar_t* extraArgs = (argc >= 4) ? argv[3] : L"";

    // Build the command line CreateProcess expects: "<exe>" <args>.
    // Quote the exe path so directories with spaces (Program Files etc.)
    // don't confuse the parser.
    wchar_t cmdLine[32768];
    swprintf_s(cmdLine, L"\"%s\" %s", targetExe, extraArgs);

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi = {};

    // CREATE_SUSPENDED: the main thread is created but never resumed, so
    // we have a quiescent process to inject into. age3y.exe hasn't run a
    // single instruction yet.
    BOOL ok = CreateProcessW(
        nullptr,
        cmdLine,
        nullptr, nullptr,
        FALSE,
        CREATE_SUSPENDED,
        nullptr, nullptr,
        &si, &pi);
    if (!ok)
    {
        Err(L"CreateProcess failed (Win32 %lu) for: %s", GetLastError(), cmdLine);
        return 2;
    }

    // Reserve memory in the target for the DLL path string we're about
    // to push into LoadLibraryW. Includes the null terminator.
    SIZE_T pathBytes = (wcslen(dllPath) + 1) * sizeof(wchar_t);
    LPVOID remoteMem = VirtualAllocEx(
        pi.hProcess, nullptr, pathBytes,
        MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remoteMem)
    {
        Err(L"VirtualAllocEx failed (Win32 %lu)", GetLastError());
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 3;
    }

    SIZE_T written = 0;
    if (!WriteProcessMemory(pi.hProcess, remoteMem, dllPath, pathBytes, &written) ||
        written != pathBytes)
    {
        Err(L"WriteProcessMemory failed (Win32 %lu)", GetLastError());
        VirtualFreeEx(pi.hProcess, remoteMem, 0, MEM_RELEASE);
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 3;
    }

    // GetProcAddress(LoadLibraryW) works because we're a 32-bit process
    // ourselves; kernel32 is loaded at the same base as it would be in
    // age3y.exe (KnownDLL + per-arch shared loader address).
    HMODULE kernel32 = GetModuleHandleW(L"kernel32.dll");
    if (!kernel32)
    {
        Err(L"GetModuleHandle(kernel32) failed (Win32 %lu)", GetLastError());
        VirtualFreeEx(pi.hProcess, remoteMem, 0, MEM_RELEASE);
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 4;
    }
    FARPROC loadLibraryW = GetProcAddress(kernel32, "LoadLibraryW");
    if (!loadLibraryW)
    {
        Err(L"GetProcAddress(LoadLibraryW) returned null");
        VirtualFreeEx(pi.hProcess, remoteMem, 0, MEM_RELEASE);
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 4;
    }

    // Spawn a thread in the target whose entry point is LoadLibraryW and
    // whose argument is the DLL path we just wrote. The thread returns
    // the HMODULE LoadLibrary handed back.
    HANDLE remoteThread = CreateRemoteThread(
        pi.hProcess, nullptr, 0,
        reinterpret_cast<LPTHREAD_START_ROUTINE>(loadLibraryW),
        remoteMem,
        0, nullptr);
    if (!remoteThread)
    {
        Err(L"CreateRemoteThread failed (Win32 %lu)", GetLastError());
        VirtualFreeEx(pi.hProcess, remoteMem, 0, MEM_RELEASE);
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 4;
    }

    // Wait for LoadLibrary to finish, then read its return value to make
    // sure the DLL actually loaded (not just that the thread started).
    WaitForSingleObject(remoteThread, INFINITE);
    DWORD loadResult = 0;
    GetExitCodeThread(remoteThread, &loadResult);
    CloseHandle(remoteThread);
    VirtualFreeEx(pi.hProcess, remoteMem, 0, MEM_RELEASE);

    if (loadResult == 0)
    {
        Err(L"LoadLibraryW in target returned NULL — DLL refused to load");
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 5;
    }

    // Now that the hooks are in place, let age3y.exe start executing.
    if (ResumeThread(pi.hThread) == (DWORD)-1)
    {
        Err(L"ResumeThread failed (Win32 %lu)", GetLastError());
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 6;
    }

    // Print the PID so the launcher can track the game process for the
    // "watched launch" flow (game-closed callback etc.).
    wprintf(L"PID=%lu\n", pi.dwProcessId);

    // We deliberately don't WaitForSingleObject(pi.hProcess) here — the
    // launcher wants the injector to exit immediately so it can keep
    // tabs on the game process itself.
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
