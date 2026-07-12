using System;
using System.Diagnostics;
using System.IO;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Gives a mod an EXCLUSIVE <c>My Games</c> save folder even though the AoE3
/// engine writes to the fixed <c>My Games\Age of Empires 3\</c> path.
///
/// Some total conversions (WoL, Improvement Mod) ship builds that already write
/// to their OWN <c>My Games\&lt;name&gt;</c> folder, so they need nothing. Others
/// (e.g. King's Return / <c>age3k.exe</c>) write to the SHARED
/// <c>Age of Empires 3</c> folder, mixing their saves with vanilla. For those
/// (<see cref="Models.ModProfile.UserDataRedirect"/>) the launcher, around
/// launch, points the standard folder at the mod's exclusive folder with a
/// DIRECTORY JUNCTION, and restores the real vanilla folder when anything else
/// launches or at the next startup.
///
/// Safety is load-bearing: it NEVER deletes a real folder — it only moves the
/// real <c>Age of Empires 3</c> aside (once) and creates/removes a junction
/// (removed with <c>recursive:false</c>, which drops only the link, never the
/// target). Every operation is best-effort try/caught so it can never block a
/// launch. Junctions need no elevation (<c>mklink /J</c>).
/// </summary>
public static class AoE3UserDataRedirect
{
    private const string StdName = "Age of Empires 3";
    private const string AsideName = "Age of Empires 3 (AoE3 vanilla)";

    /// <summary>
    /// Redirect the standard AoE3 save folder to <paramref name="targetFolder"/>
    /// (the mod's exclusive folder name under <c>My Games</c>). No-op when the
    /// My Games root can't be resolved or the folder name is empty.
    /// </summary>
    public static void EnsureRedirected(string? targetFolder)
    {
        if (string.IsNullOrWhiteSpace(targetFolder)) return;
        var root = ResolveMyGamesRoot();
        if (root == null) return;
        try { EnsureRedirectedIn(root, targetFolder); }
        catch (Exception ex) { DiagnosticLog.Write($"AoE3UserDataRedirect.EnsureRedirected failed: {ex.Message}"); }
    }

    /// <summary>
    /// Restore the standard AoE3 save folder to the real vanilla directory
    /// (remove any junction, move the aside folder back). Safe no-op when nothing
    /// was redirected.
    /// </summary>
    public static void EnsureDefault()
    {
        var root = ResolveMyGamesRoot();
        if (root == null) return;
        try { EnsureDefaultIn(root); }
        catch (Exception ex) { DiagnosticLog.Write($"AoE3UserDataRedirect.EnsureDefault failed: {ex.Message}"); }
    }

    // ---- testable cores (explicit My Games root) --------------------------------

    /// <summary>Core of <see cref="EnsureRedirected"/> against an explicit My Games root.</summary>
    internal static bool EnsureRedirectedIn(string myGamesRoot, string targetFolder)
    {
        var std = Path.Combine(myGamesRoot, StdName);
        var aside = Path.Combine(myGamesRoot, AsideName);
        var target = Path.Combine(myGamesRoot, targetFolder);

        Directory.CreateDirectory(target);   // the mod's exclusive folder must exist

        if (IsJunction(std))
        {
            // Already a junction — done if it points at our target, else drop the
            // stale link and re-point below.
            if (JunctionPointsAt(std, target)) return true;
            Directory.Delete(std, recursive: false);   // removes ONLY the link
        }
        else if (Directory.Exists(std))
        {
            // Real vanilla folder present. Move it aside ONCE. If an aside already
            // exists, a prior restore didn't finish — bail rather than clobber the
            // user's data; leave the default intact.
            if (Directory.Exists(aside))
            {
                DiagnosticLog.Write(
                    "AoE3UserDataRedirect: aside vanilla folder already exists — not redirecting (leaving default).");
                return false;
            }
            Directory.Move(std, aside);
        }

        CreateJunction(std, target);
        DiagnosticLog.Write($"AoE3UserDataRedirect: '{StdName}' → junction to '{targetFolder}'.");
        return true;
    }

    /// <summary>Core of <see cref="EnsureDefault"/> against an explicit My Games root.</summary>
    internal static void EnsureDefaultIn(string myGamesRoot)
    {
        var std = Path.Combine(myGamesRoot, StdName);
        var aside = Path.Combine(myGamesRoot, AsideName);

        if (IsJunction(std))
        {
            Directory.Delete(std, recursive: false);   // drop the link only
            DiagnosticLog.Write($"AoE3UserDataRedirect: removed '{StdName}' junction.");
        }
        // Restore the real vanilla folder if it's parked aside and nothing occupies std.
        if (Directory.Exists(aside) && !Directory.Exists(std))
        {
            Directory.Move(aside, std);
            DiagnosticLog.Write($"AoE3UserDataRedirect: restored real '{StdName}'.");
        }
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>
    /// The <c>My Games</c> directory where AoE3 actually writes. Reuses the
    /// dual-root resolution (redirected Documents vs physical), picking the root
    /// whose <c>Age of Empires 3</c> exists (or the first candidate). Null if
    /// unresolvable.
    /// </summary>
    private static string? ResolveMyGamesRoot()
    {
        try
        {
            var candidates = UserDataService.GetCandidateUserDataFolders(StdName); // <root>\My Games\Age of Empires 3
            var chosen = UserDataService.PickUserDataFolder(candidates, Directory.Exists);
            var root = chosen == null ? null : Path.GetDirectoryName(chosen);      // <root>\My Games
            return string.IsNullOrEmpty(root) ? null : root;
        }
        catch { return null; }
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
