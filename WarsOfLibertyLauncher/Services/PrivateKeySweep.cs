using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Finds the private Age of Empires III registry keys (<see cref="SetupPathPatcher"/>) that no
/// longer serve anything, for the launcher's own "Uninstall from this PC".
///
/// <para><b>Why only these.</b> Each install of a mod that ships the stock executable carries a
/// key under HKLM, created once and removed by that mod's own uninstall. The launcher does not
/// touch installed mods when it removes itself — a mod still on disk needs its key to start — so
/// the only keys it may take with it are LEFTOVERS: keys whose install is gone (a folder deleted
/// by hand, a copy wiped by a reinstall of Windows apps, an uninstall that could not get admin).
/// Those are harmless but they are the launcher's mess, and an uninstall that leaves its mess
/// behind is not an uninstall.</para>
///
/// <para><b>Every rule below refuses on doubt</b>, because the failure directions are not equal:
/// a key kept is a few bytes nobody reads, a key removed while something still uses it is a mod
/// that opens straight into the 25-character product-key dialog. A key is a leftover only when
/// ALL of these hold: its name is one a catalogue mod's install could have been given
/// (<see cref="SetupPathPatcher.CandidateKeys"/>) and the helper accepts
/// (<see cref="PrivateKeyHelper.IsAllowedKey"/>); no install the launcher knows of names it in its
/// manifest; it exists with an absolute <c>setuppath</c>; that folder is PROVABLY gone — on a
/// drive that is present, answering "not found" rather than "access denied" (another Windows
/// account's install, a disconnected drive and an unreadable folder all keep the key); and its
/// licence values are the base game's (the launcher clones them; a key with different ones was
/// not made by it).</para>
/// </summary>
public static class PrivateKeySweep
{
    /// <summary>What a folder probe can say. Only <see cref="Missing"/> is evidence.</summary>
    public enum PathState { Exists, Missing, Unknown }

    /// <summary>The most keys one uninstall removes — the helper's own cap.</summary>
    public const int MaxKeys = PrivateKeyHelper.MaxKeysPerRemoval;

    /// <summary>
    /// Pure: which of <paramref name="candidates"/> are leftovers. <paramref name="liveKeys"/> is
    /// asked for only when at least one key looks like a leftover — reading manifests is the
    /// expensive part (a Wars of Liberty manifest is tens of MB) and, with no mod of this kind ever
    /// installed, there is nothing to read them for.
    /// </summary>
    internal static IReadOnlyList<string> SelectOrphans(
        IEnumerable<string> candidates,
        Func<string, SetupPathPatcher.KeyFacts?> readFacts,
        Func<string, PathState> probe,
        Func<IEnumerable<string>> liveKeys)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suspects = new List<string>();
        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var key = raw.Trim();
            if (!seen.Add(key)) continue;
            if (!PrivateKeyHelper.IsAllowedKey(key)) continue;
            if (!IsOrphan(readFacts(key), probe)) continue;
            suspects.Add(key);
        }
        if (suspects.Count == 0) return suspects;

        var live = new HashSet<string>(
            liveKeys().Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return suspects.Where(k => !live.Contains(k)).Take(MaxKeys).ToList();
    }

    /// <summary>
    /// Pure: the per-key half of the rule, shared with the elevated helper, which re-checks every
    /// key it is handed instead of trusting the command line.
    /// </summary>
    internal static bool IsOrphan(SetupPathPatcher.KeyFacts? facts, Func<string, PathState> probe)
    {
        if (facts is not { } f) return false;
        if (string.IsNullOrWhiteSpace(f.SetupPath)) return false;
        if (!f.LicenceMatchesBase) return false;
        bool qualified;
        try { qualified = Path.IsPathFullyQualified(f.SetupPath); }
        catch { qualified = false; }
        return qualified && probe(f.SetupPath) == PathState.Missing;
    }

    /// <summary>
    /// Every key an install of a catalogue mod using <see cref="ModProfile.PrivateSetupPath"/> can
    /// have been given. A mod that has left the catalogue contributes nothing, so its keys are
    /// kept — refusing on doubt again: the launcher no longer knows what that mod was called.
    /// </summary>
    internal static IReadOnlyList<string> CandidateKeysFor(IEnumerable<ModProfile> profiles)
    {
        var keys = new List<string>();
        foreach (var profile in profiles)
        {
            if (profile == null || profile.IsStockGame || !profile.PrivateSetupPath) continue;
            string name;
            try { name = SetupPathPatcher.KeyNameFor(profile); }
            catch { continue; }
            try
            {
                foreach (var key in SetupPathPatcher.CandidateKeys(name)) keys.Add(key);
            }
            catch (ArgumentException)
            {
                // A name the patcher refuses was never given a key — nothing more to look for.
            }
        }
        return keys;
    }

    /// <summary>
    /// Pure: what a folder probe says, from the two questions it asks the disk. A missing drive
    /// root is <see cref="PathState.Unknown"/> — a disconnected external drive is the classic
    /// case of an install that is fine and merely absent today. "Not found" is
    /// <see cref="PathState.Missing"/>; anything else the disk answers (access denied, a file
    /// where the folder should be, a path it rejects) is <see cref="PathState.Unknown"/>.
    /// </summary>
    internal static PathState ProbePath(string? path, Func<string, bool> rootExists, Func<string, FileAttributes> attributesOf)
    {
        if (string.IsNullOrWhiteSpace(path)) return PathState.Unknown;
        string full;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return PathState.Unknown;
            full = Path.GetFullPath(path);
        }
        catch
        {
            return PathState.Unknown;
        }

        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || !rootExists(root)) return PathState.Unknown;

        try
        {
            var attributes = attributesOf(full);
            return (attributes & FileAttributes.Directory) != 0 ? PathState.Exists : PathState.Unknown;
        }
        catch (FileNotFoundException) { return PathState.Missing; }
        catch (DirectoryNotFoundException) { return PathState.Missing; }
        catch { return PathState.Unknown; }
    }

    /// <summary><see cref="ProbePath(string?, Func{string, bool}, Func{string, FileAttributes})"/> against the real disk.</summary>
    public static PathState ProbePath(string? path) => ProbePath(path, Directory.Exists, File.GetAttributes);

    /// <summary>
    /// The private keys the installs this launcher knows of still use, read from their manifests.
    /// Straight from <see cref="LauncherConfig.Mods"/> — never through <c>GetState</c>, which would
    /// create an entry for every mod just to look.
    /// </summary>
    internal static IEnumerable<string> LiveKeysOf(LauncherConfig config)
    {
        var keys = new List<string>();
        foreach (var state in config.Mods.Values)
        {
            if (state == null) continue;
            foreach (var path in state.AllInstallPaths())
            {
                try
                {
                    var key = InstallManifest.TryLoad(path)?.PrivateSetupPathKey;
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                }
                catch
                {
                    // An unreadable manifest names nothing; its key is still protected by its
                    // folder existing, which is what the probe checks first.
                }
            }
        }
        return keys;
    }

    /// <summary>The leftover keys on this PC. Reads the registry and the disk; run it off the UI thread.</summary>
    public static IReadOnlyList<string> FindOrphans(LauncherConfig config)
    {
        try
        {
            var orphans = SelectOrphans(
                CandidateKeysFor(ModRegistry.All),
                SetupPathPatcher.ReadKeyFacts,
                ProbePath,
                () => LiveKeysOf(config));
            if (orphans.Count > 0)
                DiagnosticLog.Write($"PrivateKeySweep: {orphans.Count} leftover key(s): {string.Join(" | ", orphans)}.");
            return orphans;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"PrivateKeySweep: could not look for leftover keys: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Removes the HKLM half of each key in this process — only when it is elevated; otherwise
    /// the helper does it (<see cref="PrivateKeyHelper"/>). Each key is re-checked first, as the
    /// helper does, so a key that stopped being a leftover since the dialog opened is kept.
    /// Returns how many could not be removed.
    /// </summary>
    public static int RemoveMachineKeys(IReadOnlyList<string> keys)
    {
        var failed = 0;
        foreach (var key in keys)
        {
            if (!PrivateKeyHelper.IsAllowedKey(key)) { failed++; continue; }
            var facts = SetupPathPatcher.ReadKeyFacts(key);
            if (facts == null) continue;                    // already gone
            if (!IsOrphan(facts, ProbePath)) { failed++; continue; }
            if (!SetupPathPatcher.RemovePrivateKeyMachine(key)) failed++;
        }
        return failed;
    }

    /// <summary>
    /// The per-user half, for every key whose HKLM half is now gone — never one that is still
    /// there, so a refused or cancelled elevation leaves both halves exactly as they were.
    /// </summary>
    public static void RemoveUserMarkers(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
            if (!SetupPathPatcher.MachineKeyExists(key))
                SetupPathPatcher.RemoveLeftoverUserMarker(key);
    }
}
