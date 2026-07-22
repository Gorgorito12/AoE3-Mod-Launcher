using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Lets a total-conversion that ships the STOCK <c>age3y.exe</c> (no UHC patch)
/// run from its own isolated folder, without touching the registry.
///
/// The stock AoE3 engine locates its <c>.bar</c>/data by the registry
/// <c>setuppath</c> (<c>HKLM\…\Age of Empires 3 Expansion Pack 2\1.0\setuppath</c>,
/// which points at the real <c>…\bin</c>), NOT the launch working directory —
/// so a mod cloned into a separate folder loads the VANILLA content and its own
/// changes never appear. UHC-patched mods (WoL, Improvement Mod, ESOC) sidestep
/// this because their custom <c>.exe</c> reads the working directory; a mod that
/// ships the stock exe (e.g. Struggle of Indonesia) does not.
///
/// Rather than repoint the global registry value (needs admin, and a bad restore
/// bricks vanilla), we make the folder <c>setuppath</c> ALREADY points at resolve
/// to the mod's folder with a DIRECTORY JUNCTION around launch, and restore the
/// real folder when anything else launches or at the next startup — the exact
/// pattern <see cref="AoE3UserDataRedirect"/> uses for <c>My Games</c>.
///
/// Safety is load-bearing: it NEVER deletes a real folder — it only moves the
/// real setup folder aside (once) and creates/removes a junction (removed with
/// <c>recursive:false</c>, which drops only the link, never the target). Every
/// operation is best-effort try/caught so it can never block a launch. Junctions
/// need no elevation (<c>mklink /J</c>). Gated by
/// <see cref="Models.ModProfile.SetupPathRedirect"/>.
/// </summary>
public static class AoE3SetupPathRedirect
{
    private const string AsideSuffix = " (AoE3 vanilla)";

    /// <summary>
    /// Point the registry <c>setuppath</c> folder at <paramref name="modFolder"/>
    /// with a junction. No-op when the setup path can't be resolved or the mod
    /// folder is empty/missing.
    /// </summary>
    public static void EnsureRedirected(string? modFolder)
    {
        if (string.IsNullOrWhiteSpace(modFolder) || !Directory.Exists(modFolder)) return;
        var setupPath = ResolveSetupPath();
        if (setupPath == null) { DiagnosticLog.Write("AoE3SetupPathRedirect: no setuppath in registry — skipping."); return; }
        try { EnsureRedirectedAt(setupPath, modFolder); }
        catch (Exception ex) { DiagnosticLog.Write($"AoE3SetupPathRedirect.EnsureRedirected failed: {ex.Message}"); }
    }

    /// <summary>
    /// Restore the real setup folder (remove any junction, move the aside folder
    /// back). Safe no-op when nothing was redirected.
    /// </summary>
    public static void EnsureDefault()
    {
        var setupPath = ResolveSetupPath();
        if (setupPath == null) return;
        try { EnsureDefaultAt(setupPath); }
        catch (Exception ex) { DiagnosticLog.Write($"AoE3SetupPathRedirect.EnsureDefault failed: {ex.Message}"); }
    }

    // ---- testable cores (explicit setup path) -----------------------------------

    /// <summary>Core of <see cref="EnsureRedirected"/> against an explicit setup path.</summary>
    internal static bool EnsureRedirectedAt(string setupPath, string modFolder)
    {
        var std = setupPath.TrimEnd('\\', '/');
        var aside = std + AsideSuffix;
        var target = modFolder.TrimEnd('\\', '/');

        if (IsJunction(std))
        {
            // Already a junction — done if it points at our target, else drop the
            // stale link and re-point below.
            if (JunctionPointsAt(std, target)) return true;
            Directory.Delete(std, recursive: false);   // removes ONLY the link
        }
        else if (Directory.Exists(std))
        {
            // Real setup folder present. Move it aside ONCE. If an aside already
            // exists, a prior restore didn't finish — bail rather than clobber the
            // real game files; leave the default intact.
            if (Directory.Exists(aside))
            {
                DiagnosticLog.Write(
                    "AoE3SetupPathRedirect: aside setup folder already exists — not redirecting (leaving default).");
                return false;
            }
            Directory.Move(std, aside);
        }
        else
        {
            // Nothing at the setup path to move aside and no junction — can't
            // safely create a link where the vanilla folder should be. Bail.
            DiagnosticLog.Write($"AoE3SetupPathRedirect: setup path '{std}' does not exist — skipping.");
            return false;
        }

        CreateJunction(std, target);
        DiagnosticLog.Write($"AoE3SetupPathRedirect: '{std}' → junction to '{target}'.");
        return true;
    }

    /// <summary>Core of <see cref="EnsureDefault"/> against an explicit setup path.</summary>
    internal static void EnsureDefaultAt(string setupPath)
    {
        var std = setupPath.TrimEnd('\\', '/');
        var aside = std + AsideSuffix;

        if (IsJunction(std))
        {
            Directory.Delete(std, recursive: false);   // drop the link only
            DiagnosticLog.Write($"AoE3SetupPathRedirect: removed '{std}' junction.");
        }
        // Restore the real setup folder if it's parked aside and nothing occupies std.
        if (Directory.Exists(aside) && !Directory.Exists(std))
        {
            Directory.Move(aside, std);
            DiagnosticLog.Write($"AoE3SetupPathRedirect: restored real '{std}'.");
        }
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>
    /// The folder the stock AoE3 engine loads its <c>.bar</c>/data from — the
    /// registry <c>setuppath</c> under the TAD (Expansion Pack 2) key. Reads
    /// HKLM (no elevation needed for a read); tries the WOW6432Node view first
    /// (64-bit Windows) then the plain view. Null if empty/unreadable.
    /// </summary>
    private static string? ResolveSetupPath()
    {
        foreach (var sub in new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0",
            @"SOFTWARE\Microsoft\Microsoft Games\Age of Empires 3 Expansion Pack 2\1.0",
        })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(sub);
                if (key?.GetValue("setuppath") is string val && !string.IsNullOrWhiteSpace(val))
                    return val.TrimEnd('\\', '/');
            }
            catch { /* try the next view */ }
        }
        return null;
    }

    internal static bool IsJunction(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch { return false; }
    }

    private static bool JunctionPointsAt(string junction, string target)
    {
        try
        {
            var t = Directory.ResolveLinkTarget(junction, returnFinalTarget: false);
            return t != null && string.Equals(
                Path.GetFullPath(t.FullName).TrimEnd('\\', '/'),
                Path.GetFullPath(target).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void CreateJunction(string link, string target)
    {
        // mklink /J needs no elevation; run hidden and wait.
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p == null) throw new IOException("Failed to start cmd for mklink");
        p.WaitForExit(10_000);
        if (!p.HasExited || p.ExitCode != 0)
            throw new IOException($"mklink /J failed (exit {(p.HasExited ? p.ExitCode : -1)}) for '{link}' → '{target}'");
    }
}
