using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

/// <summary>
/// The sources one mod's translations are read from, in the order they are fetched: the mod's
/// own repository first (official), then its own releases, then every source the player added.
/// Built by <see cref="UpdateService.EffectiveTranslationSources"/>, which returns
/// <see cref="Empty"/> for a mod with no Translations block — an added source can never inject
/// packs into a mod that doesn't take translations.
/// </summary>
public sealed class TranslationSources
{
    public static readonly TranslationSources Empty = new(Array.Empty<(TranslationSourceRef, bool)>());

    public TranslationSources(IReadOnlyList<(TranslationSourceRef Source, bool IsOfficial)> all) => All = all;

    public IReadOnlyList<(TranslationSourceRef Source, bool IsOfficial)> All { get; }

    public bool IsEmpty => All.Count == 0;

    /// <summary>Only the sources the player added (the notification feed already covers the rest).</summary>
    public TranslationSources OnlyUnofficial() => new(All.Where(s => !s.IsOfficial).ToList());
}

/// <summary>
/// One row of the Language tab's "Translation sources" list. <paramref name="PackCount"/> is the
/// number of cards the source gave this mod on the last refresh, or -1 when it hasn't been read yet.
/// </summary>
public sealed record TranslationSourceRow(
    string Key, string Label, string Location, TranslationSourceKind Kind, bool IsOfficial,
    bool Reachable, string? ErrorKey, int PackCount);

/// <summary>One version as a source lists it, with the pack-level text that travels with it.</summary>
public sealed record SourcedVersion(string Id, string Name, string Language, string? Description, TranslationVersion Version);

/// <summary>What fetching one source produced — or why it produced nothing.</summary>
public sealed class SourceFetchResult
{
    public required TranslationSourceRef Source { get; init; }
    public bool IsOfficial { get; init; }

    /// <summary>False when the source could not be read at all (missing, private, offline, not an index).</summary>
    public bool Reachable { get; init; }

    /// <summary>What the cards call this source: a repo, the index's own name, or its host.</summary>
    public string Label { get; init; } = "";

    public List<SourcedVersion> Versions { get; init; } = new();

    /// <summary>A string-table key naming the failure, when <see cref="Reachable"/> is false.</summary>
    public string? ErrorKey { get; init; }

    /// <summary>Technical detail for the log (an HTTP status, a JSON error).</summary>
    public string? ErrorDetail { get; init; }

    public static SourceFetchResult Failed(TranslationSourceRef source, bool isOfficial, string errorKey, string? detail = null) =>
        new() { Source = source, IsOfficial = isOfficial, Reachable = false, ErrorKey = errorKey, ErrorDetail = detail, Label = source.DisplayLocation };
}

/// <summary>
/// Turns what the sources list into the cards one mod shows: ONE CARD PER TRANSLATOR. The same
/// language id from two sources is two cards, each with its own version history, so a new version
/// from a translator lands at the top of THAT translator's card instead of being mixed into
/// someone else's. (The previous model merged every repository's versions under one id, which
/// put another translator's work inside the official card.)
///
/// <para>The target-mod filter runs per VERSION, before anything is grouped: a version made for
/// another mod is dropped even when the same source also offers this mod a pack with that id.
/// The mod's own sources may list packs with no <c>targetMod</c> (older packs predate the field);
/// a source the player added may not — it has to say which mod it is for.</para>
/// </summary>
public static class TranslationSourceGrouping
{
    public static bool AllowedForMod(TranslationVersion v, string? modId)
    {
        if (v == null) return false;
        if (string.IsNullOrWhiteSpace(modId)) return true;
        if (string.IsNullOrWhiteSpace(v.TargetMod)) return v.IsOfficial;
        return string.Equals(v.TargetMod.Trim(), modId.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One source's versions → that source's cards for <paramref name="modId"/>.</summary>
    public static List<TranslationIndexEntry> ForMod(IEnumerable<SourcedVersion> versions, string? modId)
    {
        var order = new List<string>();
        var byId = new Dictionary<string, List<SourcedVersion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sv in versions ?? Enumerable.Empty<SourcedVersion>())
        {
            if (sv == null || string.IsNullOrEmpty(sv.Id) || !AllowedForMod(sv.Version, modId)) continue;
            if (!byId.TryGetValue(sv.Id, out var list))
            {
                byId[sv.Id] = list = new List<SourcedVersion>();
                order.Add(sv.Id);
            }
            list.Add(sv);
        }

        var entries = new List<TranslationIndexEntry>(order.Count);
        foreach (var id in order)
        {
            var group = byId[id];
            var ordered = TranslationCompat.OrderVersions(group.Select(g => g.Version));
            if (ordered.Count == 0) continue;
            var newest = ordered[0];
            var meta = group.First(g => ReferenceEquals(g.Version, newest));
            entries.Add(new TranslationIndexEntry
            {
                Id = meta.Id,
                Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Id : meta.Name,
                Language = string.IsNullOrWhiteSpace(meta.Language) ? meta.Id : meta.Language,
                Author = newest.Author,
                Version = newest.Version,
                CompatibleWith = newest.CompatibleWith,
                DownloadUrl = newest.DownloadUrl,
                Size = newest.Size,
                Sha256 = string.IsNullOrEmpty(newest.Sha256) ? null : newest.Sha256,
                Description = meta.Description,
                TargetMod = newest.TargetMod,
                ContentHash = newest.ContentHash,
                FromFolder = newest.SourceKind == TranslationSourceKind.GitHubFolder,
                Versions = ordered,
                SourceRepo = newest.SourceRepo,
                SourceKey = newest.SourceKey,
                SourceLabel = newest.SourceLabel,
                SourceKind = newest.SourceKind,
                IsOfficial = newest.IsOfficial,
            });
        }
        return entries;
    }

    /// <summary>Every reachable source's cards for <paramref name="modId"/>, in fetch order.</summary>
    public static List<TranslationIndexEntry> BuildForMod(IEnumerable<SourceFetchResult> results, string? modId)
    {
        var all = new List<TranslationIndexEntry>();
        foreach (var r in results ?? Enumerable.Empty<SourceFetchResult>())
            if (r != null && r.Reachable) all.AddRange(ForMod(r.Versions, modId));
        return HideOfficialReleaseDuplicates(all);
    }

    /// <summary>
    /// The mod's own releases are the legacy copy of its own folder repository: where the folder
    /// repository offers an id, the release entry for that id is hidden, as it always was. Only
    /// the OFFICIAL pair is folded this way — two different translators are never merged.
    /// </summary>
    public static List<TranslationIndexEntry> HideOfficialReleaseDuplicates(List<TranslationIndexEntry> entries)
    {
        var officialFolderIds = new HashSet<string>(
            entries.Where(e => e.IsOfficial && e.SourceKind == TranslationSourceKind.GitHubFolder).Select(e => e.Id),
            StringComparer.OrdinalIgnoreCase);
        return entries
            .Where(e => !(e.IsOfficial && e.SourceKind == TranslationSourceKind.GitHubReleases && officialFolderIds.Contains(e.Id)))
            .ToList();
    }
}
