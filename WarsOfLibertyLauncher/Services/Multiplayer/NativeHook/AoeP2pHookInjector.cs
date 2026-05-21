using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WarsOfLibertyLauncher.Services.Multiplayer.NativeHook;

/// <summary>
/// Thin wrapper around <c>AoeP2pInjector.exe</c> (x86 helper) and
/// <c>AoeP2pHook.dll</c> (x86 Detours-based ws2_32 hook DLL).
///
/// The launcher is a 64-bit .NET process. age3y.exe is 32-bit. To inject
/// a DLL into a 32-bit target, the easiest path is to shell out to a tiny
/// helper that runs in the target's bitness — it can call
/// <c>GetProcAddress(LoadLibraryW)</c> directly without parsing the
/// target's PE / PEB to find a cross-arch function pointer.
///
/// Lifecycle:
///   * The launcher invokes <see cref="LaunchWithHookAsync"/> with the
///     full age3y.exe path and the AoE3 command-line arguments.
///   * The helper does CreateProcess(SUSPENDED) + VirtualAllocEx +
///     WriteProcessMemory + CreateRemoteThread(LoadLibraryW) + ResumeThread,
///     and prints the resulting PID on its stdout.
///   * This wrapper parses that PID and returns a <see cref="Process"/>
///     handle attached to it, so the existing "watched launch" code
///     (game-closed callback, post-match flow) keeps working unchanged.
///
/// When the native artefacts are missing (e.g. a build made on a machine
/// without the C++ workload, or a developer iterating on the C# side
/// only), <see cref="IsAvailable"/> returns false and
/// <see cref="LaunchWithHookAsync"/> throws. Callers should check
/// availability first and fall back to a plain Process.Start as needed.
/// </summary>
public static class AoeP2pHookInjector
{
    /// <summary>
    /// Native helper that does the actual process creation + LoadLibrary
    /// inject. Lives next to the launcher .exe (same folder).
    /// </summary>
    private const string InjectorFileName = "AoeP2pInjector.exe";

    /// <summary>
    /// Detours-based hook DLL that gets loaded into age3y.exe by the
    /// injector. Lives next to the launcher .exe (same folder).
    /// </summary>
    private const string HookDllFileName = "AoeP2pHook.dll";

    /// <summary>
    /// Returns true when both native artefacts are present alongside
    /// the launcher executable. The C++ workload of Visual Studio is
    /// needed to build them — fresh checkouts on machines without it
    /// produce a publish folder where these files are missing.
    /// </summary>
    public static bool IsAvailable()
    {
        var (injector, dll) = ResolvePaths();
        return File.Exists(injector) && File.Exists(dll);
    }

    /// <summary>
    /// Launch <paramref name="age3yExePath"/> with <paramref name="extraArgs"/>,
    /// pre-injecting <see cref="HookDllFileName"/>. Returns a
    /// <see cref="Process"/> tracking the running game so callers can
    /// hook into its Exited event.
    /// </summary>
    /// <exception cref="FileNotFoundException">
    ///   When the native artefacts haven't been built or shipped.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///   When the injector helper failed (non-zero exit code or unable
    ///   to parse the PID it printed).
    /// </exception>
    public static async Task<Process> LaunchWithHookAsync(
        string age3yExePath,
        string extraArgs)
    {
        var (injector, dll) = ResolvePaths();
        if (!File.Exists(injector))
            throw new FileNotFoundException($"AoeP2pInjector not found at: {injector}");
        if (!File.Exists(dll))
            throw new FileNotFoundException($"AoeP2pHook DLL not found at: {dll}");

        var psi = new ProcessStartInfo
        {
            FileName = injector,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(age3yExePath);
        psi.ArgumentList.Add(dll);
        psi.ArgumentList.Add(extraArgs ?? string.Empty);
        // age3y.exe normally runs from its own folder so it can find
        // sibling .dll's (granny2.dll, binkw32.dll, etc.). Set the
        // helper's working dir to the target's so the spawned game
        // inherits it.
        psi.WorkingDirectory = Path.GetDirectoryName(age3yExePath) ?? string.Empty;

        // Hand the LAN↔mesh bridge's TCP port (if any) to the hook
        // via an environment variable. The injector helper inherits
        // our env block; its CreateProcess(age3y.exe) inherits it in
        // turn (no explicit env block passed), so the var lands inside
        // the game and the hook's DllMain can read it with
        // GetEnvironmentVariableW("AOE_P2P_HOOK_PORT"). When no bridge
        // is active (e.g. the user launches Solo via this same path),
        // we skip the var and the hook stays in Phase-1 logging mode.
        var bridge = AoeP2pBridgeService.Current;
        if (bridge != null)
        {
            var portStr = bridge.Port.ToString(CultureInfo.InvariantCulture);
            psi.Environment["AOE_P2P_HOOK_PORT"] = portStr;
            DiagnosticLog.Write(
                $"AoeP2pHookInjector: passing bridge TCP port '{portStr}' " +
                "to the spawned age3y.exe via AOE_P2P_HOOK_PORT.");
        }

        DiagnosticLog.Write(
            $"AoeP2pHookInjector: launching via helper. exe='{age3yExePath}' args='{extraArgs}' dll='{dll}'");

        using var helper = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null for the injector.");

        var stdoutTask = helper.StandardOutput.ReadToEndAsync();
        var stderrTask = helper.StandardError.ReadToEndAsync();
        await helper.WaitForExitAsync().ConfigureAwait(false);
        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

        if (!string.IsNullOrWhiteSpace(stderr))
            DiagnosticLog.Write($"AoeP2pInjector stderr: {stderr}");

        if (helper.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"AoeP2pInjector failed (exit {helper.ExitCode}). " +
                $"stderr: {stderr}. stdout: {stdout}.");
        }

        // Helper prints "PID=<n>" on success. Parse it so we can return
        // a Process handle the launcher can monitor.
        var pidLine = stdout
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("PID=", StringComparison.Ordinal));
        if (pidLine == null || !int.TryParse(pidLine.AsSpan(4), out var pid))
        {
            throw new InvalidOperationException(
                $"AoeP2pInjector reported success but no PID line was found in stdout. Full stdout: '{stdout}'.");
        }

        try
        {
            var gameProcess = Process.GetProcessById(pid);
            DiagnosticLog.Write($"AoeP2pHookInjector: age3y.exe running as PID {pid}, hook DLL loaded.");
            return gameProcess;
        }
        catch (ArgumentException ex)
        {
            // The game exited between the injector returning and us
            // looking it up. Rare but possible if the hook DLL itself
            // crashed in DllMain.
            throw new InvalidOperationException(
                $"AoeP2pInjector reported PID {pid} but the process no longer exists. " +
                "The hook DLL may have failed in DllMain; check %LOCALAPPDATA%\\AoeP2pHook.log.",
                ex);
        }
    }

    /// <summary>
    /// Resolve absolute paths to the two native artefacts. They live
    /// next to the launcher .exe in any normal install layout (same
    /// folder as Aoe3ModLauncher.exe). In single-file publish mode the
    /// launcher .exe is in publish/, so are these.
    /// </summary>
    private static (string injectorPath, string dllPath) ResolvePaths()
    {
        // AppContext.BaseDirectory returns the launcher's folder both in
        // normal runs and in IncludeAllContentForSelfExtract=false single
        // file builds — exactly what we want here.
        var dir = AppContext.BaseDirectory;
        return (
            Path.Combine(dir, InjectorFileName),
            Path.Combine(dir, HookDllFileName));
    }
}
