using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Pure-logic regression tests for <see cref="NotificationCenter"/> — the
/// Steam-style notification bell backing store. A no-op persist callback keeps
/// these off the real <c>launcher-config.json</c>. Covers the per-kind dedup
/// rules, the 50-item cap, and the unread accounting that drives the badge.
/// </summary>
public class NotificationCenterTests
{
    private static NotificationCenter NewCenter(out LauncherConfig config)
    {
        config = new LauncherConfig();
        return new NotificationCenter(config, persist: () => { });
    }

    [Fact]
    public void UpdateAvailable_SameVersion_DedupesToOneItem()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseUpdateAvailable("wol", "1.0.5", "t", "b"));
        Assert.False(center.RaiseUpdateAvailable("wol", "1.0.5", "t", "b")); // same (mod, version)
        Assert.Equal(1, center.Items.Count(i => i.Kind == NotificationKind.UpdateAvailable));
    }

    [Fact]
    public void UpdateAvailable_NewerVersion_BellsAgain()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseUpdateAvailable("wol", "1.0.5", "t", "b"));
        Assert.True(center.RaiseUpdateAvailable("wol", "1.0.6", "t", "b")); // a genuinely newer version
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.UpdateAvailable));
    }

    [Fact]
    public void UpdateFinished_SupersedesAvailable_AndResetsLatch()
    {
        var center = NewCenter(out _);

        center.RaiseUpdateAvailable("wol", "1.0.6", "t", "b");
        center.RaiseUpdateFinished("wol", "1.0.6", "t", "b");

        // The pending "available" item is dropped; one "finished" remains.
        Assert.DoesNotContain(center.Items, i => i.Kind == NotificationKind.UpdateAvailable);
        Assert.Single(center.Items, i => i.Kind == NotificationKind.UpdateFinished);

        // Latch reset → a FUTURE version can bell "available" again.
        Assert.True(center.RaiseUpdateAvailable("wol", "1.0.7", "t", "b"));
    }

    [Fact]
    public void NewTranslation_DedupesByKey()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseNewTranslation("wol", "es@1.0", "es", "t", "b"));
        Assert.False(center.RaiseNewTranslation("wol", "es@1.0", "es", "t", "b")); // same id@version
        Assert.True(center.RaiseNewTranslation("wol", "es@1.1", "es", "t", "b"));  // new version → bells
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.NewTranslation));
    }

    [Fact]
    public void Add_TrimsToFiftyMostRecent()
    {
        var center = NewCenter(out _);

        for (int i = 0; i < 60; i++)
            center.RaiseUpdateAvailable("wol", $"1.0.{i}", "t", $"body {i}");

        Assert.Equal(NotificationCenter.MaxItems, center.Items.Count);
        // Newest first → the most recent version is at index 0.
        Assert.Contains("body 59", center.Items[0].Body);
    }

    [Fact]
    public void MarkAllRead_ZeroesUnreadCount()
    {
        var center = NewCenter(out _);
        center.RaiseUpdateAvailable("wol", "1.0.5", "t", "b");
        center.RaiseUpdateFinished("wol", "1.0.5", "t", "b");
        Assert.True(center.UnreadCount > 0);

        center.MarkAllRead();

        Assert.Equal(0, center.UnreadCount);
        Assert.All(center.Items, i => Assert.True(i.Read));
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        var center = NewCenter(out _);
        center.RaiseUpdateAvailable("wol", "1.0.5", "t", "b");

        center.Clear();

        Assert.Empty(center.Items);
        Assert.Equal(0, center.UnreadCount);
    }

    [Fact]
    public void Constructor_SeedsFromConfig_NewestFirst()
    {
        var config = new LauncherConfig();
        config.Notifications.Add(new NotificationItem
        {
            Title = "old", CreatedAtUtc = new System.DateTime(2026, 1, 1),
        });
        config.Notifications.Add(new NotificationItem
        {
            Title = "new", CreatedAtUtc = new System.DateTime(2026, 6, 1),
        });

        var center = new NotificationCenter(config, persist: () => { });

        Assert.Equal(2, center.Items.Count);
        Assert.Equal("new", center.Items[0].Title); // newest first
    }

    [Fact]
    public void LauncherUpdate_DedupesByTag()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseLauncherUpdate("v1.0.6", "t", "b"));
        Assert.False(center.RaiseLauncherUpdate("v1.0.6", "t", "b")); // same tag
        Assert.True(center.RaiseLauncherUpdate("v1.0.7", "t", "b"));   // new tag → bells
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.LauncherUpdate));
    }

    [Fact]
    public void Connectivity_DedupesConsecutiveSameState()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseConnectivity(offline: true, "off", "b"));
        Assert.False(center.RaiseConnectivity(offline: true, "off", "b")); // same state, no spam
        Assert.True(center.RaiseConnectivity(offline: false, "on", "b"));   // flip → bells
        Assert.True(center.RaiseConnectivity(offline: true, "off", "b"));   // flip back → bells
        Assert.Equal(3, center.Items.Count(i => i.Kind == NotificationKind.Connectivity));
    }

    [Fact]
    public void NewMod_DedupesById()
    {
        var center = NewCenter(out _);

        Assert.True(center.RaiseNewMod("napoleonic-era", "t", "b"));
        Assert.False(center.RaiseNewMod("napoleonic-era", "t", "b")); // same id
        Assert.True(center.RaiseNewMod("colonial-wars", "t", "b"));
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.NewMod));
    }

    [Fact]
    public void Installed_AddsItem_NotDeduped()
    {
        var center = NewCenter(out _);

        // An install is user-initiated and raised once per install; unlike UpdateFinished
        // it is NOT deduped, so a second copy of the same version still confirms.
        Assert.True(center.RaiseInstalled("wol", "1.2.0d", "t", "b"));
        Assert.True(center.RaiseInstalled("wol", "1.2.0d", "t", "b"));
        Assert.Equal(2, center.Items.Count(i => i.Kind == NotificationKind.Installed));
    }

    [Fact]
    public void SeedCatalogBaseline_SuppressesExisting_ThenBellsOnlyNew()
    {
        var center = NewCenter(out var config);

        // First fetch: baseline the whole existing catalog silently → nothing bells.
        Assert.True(center.SeedCatalogBaseline(new[] { "wol", "aoe3-tad", "improvement-mod" }));
        Assert.Empty(center.Items);
        Assert.True(config.CatalogBaselineSeeded);

        // Baseline is one-shot.
        Assert.False(center.SeedCatalogBaseline(new[] { "another" }));

        // A pre-existing id doesn't bell; a genuinely-new one does.
        Assert.False(center.RaiseNewMod("improvement-mod", "t", "b"));
        Assert.True(center.RaiseNewMod("napoleonic-era", "t", "b"));
        Assert.Single(center.Items, i => i.Kind == NotificationKind.NewMod);
    }
}
