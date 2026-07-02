using System;
using System.Diagnostics;
using System.IO;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Exercises the re-parenting interop (`CreateProcess` +
/// `PROC_THREAD_ATTRIBUTE_PARENT_PROCESS`) that launches the game detached from the
/// launcher's process tree, so a forced Task Manager "End task" on the launcher can't
/// cascade-kill it. The key guarantee is safety: it must never throw and must always
/// leave a working fallback path — a returned -1 means "re-parenting unavailable, use
/// a normal launch". On a normal desktop (explorer running) it should succeed (>0);
/// in a headless runner it may return -1, which is fine.
/// </summary>
public class DetachedProcessLauncherTests
{
    [Fact]
    public void StartReparented_NeverThrows_AndReturnsValidPidOrFallback()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        // A malformed attribute list would make CreateProcess fail (→ -1) or crash;
        // a clean >0 pid means the parent-process attribute was accepted and the
        // process actually spawned.
        int pid = DetachedProcessLauncher.StartReparented(cmd, "/c exit", Environment.SystemDirectory);

        Assert.True(pid == -1 || pid > 0, $"unexpected pid {pid}");

        if (pid > 0)
        {
            // The interop really launched something; confirm it's a real process id,
            // then make sure it's gone (cmd /c exit ends on its own near-instantly).
            try
            {
                using var p = Process.GetProcessById(pid);
                try { p.Kill(entireProcessTree: true); } catch { /* already exited */ }
            }
            catch (ArgumentException)
            {
                // Already exited before we could open it — expected for `/c exit`.
            }
        }
    }
}
