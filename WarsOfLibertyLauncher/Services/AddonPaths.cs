using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// Path normalisation for addon archives, kept pure so the rules can be tested
/// without unpacking anything.
/// </summary>
public static class AddonPaths
{
    /// <summary>
    /// Returns the single wrapper folder every entry sits under, or <c>""</c>
    /// when there is none.
    ///
    /// Packing an overlay inside one folder is normal — the gun-smoke addon ships
    /// all 197 of its files under <c>AO3/</c>. Extracted verbatim that lands in
    /// <c>&lt;install&gt;\AO3\art\…</c>, which the game never reads: the addon
    /// doesn't fail, it just quietly does nothing, and nothing in the UI would
    /// suggest why.
    ///
    /// This mirrors <see cref="NativeInstallService.NormalizePayloadRoot"/>, which
    /// solves the same problem for mod payloads but works on an already-extracted
    /// folder — addons are classified straight from the zip's entry names, before
    /// anything is written.
    ///
    /// Stripping only happens when EVERY entry shares one root and nothing loose
    /// sits beside it. That conservative rule is the point: over-stripping a flat
    /// archive would relocate every file and break an addon that was fine.
    /// </summary>
    /// <summary>
    /// Top-level folders of an Age of Empires III install. A shared root with one
    /// of these names is the game's own folder, never a wrapper.
    ///
    /// Without this the rule eats real addons: an archive that ships only art
    /// files has <c>art/</c> as its single common root, and stripping it would
    /// scatter every file into the install root. Distinguishing the two cases is
    /// the whole difficulty — <c>AO3/</c> is a wrapper, <c>art/</c> is a
    /// destination — and the folder name is the only signal available before
    /// anything is extracted.
    /// </summary>
    private static readonly HashSet<string> GameRootFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "art", "data", "sound", "startup", "ui", "movies", "music",
        "ai", "ai2", "ai3", "scenario", "random maps", "shaders", "bin", "logs",
        // The random-map folders, ALL FOUR. Only "rm" was here, and the omission had teeth:
        // AoE3 keeps one per expansion - rm (vanilla), rm2 (TWC), rm3 (TAD) - and a total
        // conversion adds its own, which for Wars of Liberty is rm4 and holds 438 files.
        //
        // A map pack is the natural shape for that content and it is exactly the shape this
        // list could not see: a zip rooted at RM4/ had one common root, the root was not
        // recognised as a game folder, so it was treated as a wrapper and stripped. Every
        // map then landed in the install ROOT, the addon reported success, and nothing
        // appeared in the game - the silent failure this file's own header describes for
        // AO3/, arrived at from the opposite direction.
        "rm", "rm2", "rm3", "rm4",
    };

    public static string StripCommonRoot(IEnumerable<string>? entries)
    {
        if (entries == null) return "";

        string? root = null;
        bool any = false;

        foreach (var raw in entries)
        {
            var norm = Normalize(raw);
            if (norm.Length == 0) continue;

            any = true;
            var slash = norm.IndexOf('/');

            // A file sitting at the archive root means there is no single wrapper.
            if (slash <= 0) return "";

            var first = norm[..slash];
            if (root == null) root = first;
            else if (!string.Equals(root, first, StringComparison.OrdinalIgnoreCase)) return "";
        }

        if (!any || root == null) return "";

        // A game folder is where files BELONG, not something they are wrapped in.
        return GameRootFolders.Contains(root) ? "" : root + "/";
    }

    /// <summary>
    /// Applies <see cref="StripCommonRoot"/> to one entry. Returns <c>""</c> for
    /// an entry outside the prefix, which the caller drops.
    /// </summary>
    public static string RemovePrefix(string entry, string prefix)
    {
        var norm = Normalize(entry);
        if (prefix.Length == 0) return norm;
        return norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? norm[prefix.Length..]
            : "";
    }

    /// <summary>
    /// Normalizes a zip entry to the manifest's convention: install-relative with
    /// FORWARD slashes.
    ///
    /// Load-bearing and invisible to the type system.
    /// <see cref="NativeInstallService.RecaptureHashes"/> converts its input with
    /// <c>Replace('\\','/')</c> and matches it against
    /// <c>InstallManifest.OverlayFiles</c>, so a backslash path silently fails to
    /// match: the fingerprint never lands in <c>FileHashes</c>, verify keeps
    /// calling the addon's files corrupt, and Repair wipes the addon — the exact
    /// failure the addon system exists to prevent.
    /// </summary>
    public static string Normalize(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return "";
        var s = entry.Trim().Replace('\\', '/');
        while (s.StartsWith("./", StringComparison.Ordinal)) s = s[2..];
        return s.TrimStart('/');
    }
}
