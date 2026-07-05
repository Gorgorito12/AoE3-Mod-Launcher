using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pins <see cref="TranslationRegistryService.MergeFolderEntries"/> — the pure
/// (no-I/O) merge of several repos' folder-scan results into one entry per id.
/// Covers: distinct ids preserved, same-id version union + dedup by contentHash,
/// display/metadata coming from a REAL entry (never blank), and the
/// "default repo wins the top-level metadata" rule.
/// </summary>
public class TranslationMergeTests
{
    private static TranslationIndexEntry Entry(
        string sourceRepo, string id, string name,
        params (string ver, string hash, string date)[] vers)
    {
        var versions = vers.Select(t => new TranslationVersion
        {
            Version = t.ver,
            ContentHash = t.hash,
            Date = t.date,
            SourceRepo = sourceRepo,
            DownloadUrl = $"https://raw.githubusercontent.com/{sourceRepo}/main/translations/{id}/{t.ver}/{id}.zip",
        }).ToList();
        var newest = versions[0];
        return new TranslationIndexEntry
        {
            Id = id,
            Name = name,
            Language = id,
            Author = "author-" + sourceRepo,
            Version = newest.Version,
            ContentHash = newest.ContentHash,
            DownloadUrl = newest.DownloadUrl,
            TargetMod = "wol",
            FromFolder = true,
            SourceRepo = sourceRepo,
            Versions = versions,
        };
    }

    private static IReadOnlyList<IReadOnlyList<TranslationIndexEntry>> Repos(
        params IReadOnlyList<TranslationIndexEntry>[] perRepo) => perRepo;

    [Fact]
    public void DistinctIds_AllPreserved_InFirstSeenOrder()
    {
        var merged = TranslationRegistryService.MergeFolderEntries(Repos(
            new[] { Entry("def/repo", "es", "Español", ("1.0", "hA", "2026-01")) },
            new[] { Entry("bob/fr", "fr", "Français", ("1.0", "hF", "2026-01")) }));

        Assert.Equal(new[] { "es", "fr" }, merged.Select(e => e.Id).ToArray());
        Assert.All(merged, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
    }

    [Fact]
    public void SameId_UnionsVersions_DefaultKeepsTopLevelMetadata()
    {
        var merged = TranslationRegistryService.MergeFolderEntries(Repos(
            new[] { Entry("def/repo", "es", "Español-A", ("1.0", "hA", "2026-01")) },   // default
            new[] { Entry("bob/es", "es", "Español-B", ("2.0", "hB", "2026-02")) }));   // newer

        var e = Assert.Single(merged);
        Assert.Equal("es", e.Id);
        // Metadata comes from the DEFAULT entry (a real entry, not a version).
        Assert.Equal("Español-A", e.Name);
        Assert.Equal("es", e.Language);
        Assert.Equal("def/repo", e.SourceRepo);
        Assert.Equal("1.0", e.Version);          // default's newest drives one-click apply
        // But the version picker sees BOTH, newest-first.
        Assert.Equal(2, e.Versions.Count);
        Assert.Equal("2.0", e.Versions[0].Version);
        Assert.Equal("bob/es", e.Versions[0].SourceRepo);
    }

    [Fact]
    public void SameId_IdenticalContentHash_IsDeduped()
    {
        var merged = TranslationRegistryService.MergeFolderEntries(Repos(
            new[] { Entry("def/repo", "es", "Español-A", ("1.0", "hSAME", "2026-01")) },
            new[] { Entry("bob/es", "es", "Español-B", ("1.0", "hSAME", "2026-01")) }));

        var e = Assert.Single(merged);
        Assert.Single(e.Versions);   // one deduped version
    }

    [Fact]
    public void SameId_DefaultAbsent_NewestOwnerBecomesBase()
    {
        var merged = TranslationRegistryService.MergeFolderEntries(Repos(
            new[] { Entry("def/repo", "fr", "Français", ("1.0", "hF", "2026-01")) },   // default has NO "es"
            new[] { Entry("bob/es", "es", "Español-B", ("2.0", "hB", "2026-02")) }));

        var es = Assert.Single(merged, e => e.Id == "es");
        Assert.Equal("Español-B", es.Name);      // the only owner wins
        Assert.Equal("bob/es", es.SourceRepo);
        Assert.False(string.IsNullOrWhiteSpace(es.Language));
    }
}
