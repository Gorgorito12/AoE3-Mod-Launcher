using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services.Repair;

/// <summary>Why one damaged file could, or could not, be restored on its own.</summary>
internal enum GranularVerdict
{
    /// <summary>The payload holds an entry of the recorded size, in a method we can inflate.</summary>
    Coverable,
    /// <summary>Must never be written from the payload (addon-owned, translated, patched exe…).</summary>
    Excluded,
    /// <summary>The manifest records no fingerprint to prove a restored copy against.</summary>
    NoFingerprint,
    /// <summary>The payload has no such entry (a patch added it, or the name did not decode).</summary>
    NotInPayload,
    /// <summary>The payload's entry is another size: a later patch changed the file.</summary>
    SizeDiffers,
    /// <summary>Stored in a compression method the launcher does not read.</summary>
    Unsupported,
}

internal sealed record GranularItem(string Path, GranularVerdict Verdict, PayloadEntry? Entry);

/// <summary>What a per-file restore would do for one repair — see <see cref="GranularPlanner"/>.</summary>
internal sealed record GranularPlan(IReadOnlyList<GranularItem> Items, long EstimatedBytes)
{
    public int Coverable => Items.Count(i => i.Verdict == GranularVerdict.Coverable);
    public bool AllCoverable => Items.Count > 0 && Coverable == Items.Count;
}

/// <summary>
/// Decides, from the payload's central directory and the install manifest alone, which damaged
/// files a per-file ("granular") restore could put back and what it would cost to fetch them.
///
/// <para><b>This is the SHADOW half of the granular restore, and it writes nothing.</b> Repair runs
/// it before its full re-lay and only LOGS the answer, so the decision to let it act is taken on
/// measured numbers from real repairs rather than on an estimate. The earlier granular repair was
/// removed for exactly the failures these verdicts refuse: it restored the payload's bytes over files
/// a patch had since changed (<see cref="GranularVerdict.SizeDiffers"/>, and the SHA check a real
/// restore would add after inflating), and it had no answer for files a payload does not contain.</para>
///
/// <para>A size match is necessary, not sufficient: a patched file of the same size is only caught by
/// hashing what was inflated against the manifest — which is what a restore must do before writing.</para>
/// </summary>
internal static class GranularPlanner
{
    /// <summary>Local file header (30 bytes) plus a generous allowance for its name and extra.</summary>
    internal const long LocalHeaderAllowance = 30 + 512;

    internal static GranularPlan Plan(
        IReadOnlyList<PayloadEntry> entries,
        IEnumerable<string> damaged,
        IReadOnlyDictionary<string, FileFingerprint> manifestHashes,
        ISet<string> excluded)
    {
        var files = entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var prefix = NativeInstallService.ResolvePayloadPrefix(files.Select(e => Normalize(e.Name)));
        var byPath = new Dictionary<string, PayloadEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in files)
        {
            var n = Normalize(e.Name);
            if (prefix.Length > 0)
            {
                if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                n = n[prefix.Length..];
            }
            if (n.Length > 0) byPath[n] = e;
        }

        var hashes = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in manifestHashes) hashes[Normalize(kv.Key)] = kv.Value;

        var items = new List<GranularItem>();
        long bytes = 0;
        foreach (var raw in damaged.Select(Normalize).Where(p => p.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            byPath.TryGetValue(raw, out var entry);
            GranularVerdict verdict;
            if (excluded.Contains(raw)) verdict = GranularVerdict.Excluded;
            else if (!hashes.TryGetValue(raw, out var fp) || string.IsNullOrEmpty(fp.Sha256))
                verdict = GranularVerdict.NoFingerprint;
            else if (entry == null) verdict = GranularVerdict.NotInPayload;
            else if (entry.Size != fp.Size) verdict = GranularVerdict.SizeDiffers;
            else if (entry.Method is not (0 or 8)) verdict = GranularVerdict.Unsupported;
            else
            {
                verdict = GranularVerdict.Coverable;
                bytes += entry.CompressedSize + LocalHeaderAllowance;
            }
            items.Add(new GranularItem(raw, verdict, entry));
        }
        return new GranularPlan(items, bytes);
    }

    /// <summary>
    /// The files a restore from the payload must never write, from profile and manifest data:
    /// addon-owned files (their bytes are the addon's), translation-covered files while a
    /// translation is active (the live file is SUPPOSED to differ), and the executable the launcher
    /// patches for a private setup key (the payload's copy is the unpatched one).
    /// </summary>
    internal static HashSet<string> Excluded(ModProfile profile, IEnumerable<string>? addonOwned, bool translationActive)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in addonOwned ?? Enumerable.Empty<string>()) set.Add(Normalize(p));
        if (translationActive && profile.Translations?.CoveredFiles != null)
            foreach (var p in profile.Translations.CoveredFiles) set.Add(Normalize(p));
        if (profile.PrivateSetupPath)
            set.Add(Normalize(string.IsNullOrWhiteSpace(profile.GameExecutable) ? "age3y.exe" : profile.GameExecutable));
        return set;
    }

    private static string Normalize(string p) => (p ?? "").Trim().Replace('\\', '/').TrimStart('/');
}
