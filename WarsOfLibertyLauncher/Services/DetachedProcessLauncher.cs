using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Launches a process RE-PARENTED under <c>explorer.exe</c> instead of under THIS
/// launcher, so a forced <b>Task Manager → "End task"</b> on the launcher does not
/// cascade-kill it. Windows' "End task" terminates the target's whole <i>process
/// tree</i> (the process + its descendants); a game launched as a normal child of
/// the launcher is a descendant and gets force-killed along with it. By creating it
/// with <c>PROC_THREAD_ATTRIBUTE_PARENT_PROCESS</c> pointing at explorer.exe, the new
/// process is a child of explorer — outside the launcher's tree — so it survives.
///
/// This is best-effort: <see cref="StartReparented"/> returns -1 when re-parenting
/// isn't possible (no explorer in this session, insufficient rights, or the interop
/// fails), and the caller falls back to a plain <see cref="Process.Start(ProcessStartInfo)"/>.
/// Launching the game must never fail because of this hardening.
/// </summary>
internal static class DetachedProcessLauncher
{
    private const uint PROCESS_CREATE_PROCESS = 0x0080;
    private const int PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = 0x00020000;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue,
        IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    /// <summary>
    /// Starts <paramref name="fileName"/> re-parented under explorer.exe. Returns the
    /// new process id on success, or <c>-1</c> if re-parenting wasn't possible (the
    /// caller should then fall back to a normal launch). Never throws.
    /// </summary>
    public static int StartReparented(string fileName, string? arguments, string? workingDir)
    {
        IntPtr hParent = IntPtr.Zero;
        IntPtr attrList = IntPtr.Zero;
        IntPtr hValue = IntPtr.Zero;
        bool attrListInit = false;
        try
        {
            int parentPid = FindReparentTargetPid();
            if (parentPid <= 0) return -1;

            hParent = OpenProcess(PROCESS_CREATE_PROCESS, false, parentPid);
            if (hParent == IntPtr.Zero) return -1;

            // Size the attribute list (1 attribute), then allocate + initialize it.
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attrList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size)) return -1;
            attrListInit = true;

            // The attribute value is a POINTER to the parent handle.
            hValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(hValue, hParent);
            if (!UpdateProcThreadAttribute(
                    attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PARENT_PROCESS,
                    hValue, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                return -1;

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            si.lpAttributeList = attrList;

            // CreateProcessW may write to lpCommandLine, so it must be a mutable buffer.
            var cmd = new StringBuilder();
            cmd.Append('"').Append(fileName).Append('"');
            if (!string.IsNullOrWhiteSpace(arguments))
                cmd.Append(' ').Append(arguments);

            bool ok = CreateProcess(
                null, cmd, IntPtr.Zero, IntPtr.Zero,
                bInheritHandles: false,
                dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT,
                lpEnvironment: IntPtr.Zero,
                lpCurrentDirectory: string.IsNullOrEmpty(workingDir) ? null : workingDir,
                ref si, out var pi);

            if (!ok) return -1;

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        catch
        {
            return -1;
        }
        finally
        {
            if (attrListInit) DeleteProcThreadAttributeList(attrList);
            if (attrList != IntPtr.Zero) Marshal.FreeHGlobal(attrList);
            if (hValue != IntPtr.Zero) Marshal.FreeHGlobal(hValue);
            if (hParent != IntPtr.Zero) CloseHandle(hParent);
        }
    }

    /// <summary>
    /// explorer.exe running in this session is the ideal foster parent: long-lived,
    /// same user/session, openable for <c>PROCESS_CREATE_PROCESS</c>.
    /// </summary>
    private static int FindReparentTargetPid()
    {
        try
        {
            var explorer = Process.GetProcessesByName("explorer").FirstOrDefault(p => !p.HasExited);
            if (explorer != null) return explorer.Id;
        }
        catch { /* fall through to -1 → caller uses normal launch */ }
        return -1;
    }
}
