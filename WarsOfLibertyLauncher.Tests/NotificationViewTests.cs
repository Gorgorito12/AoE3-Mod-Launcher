using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>What the bell shows and how (design 70).</summary>
public class NotificationViewTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Local);

    private static NotificationItem Item(NotificationKind kind, double daysAgo = 0, bool read = false, string? target = null)
        => new()
        {
            Kind = kind,
            CreatedAtUtc = Now.AddDays(-daysAgo).ToUniversalTime(),
            Read = read,
            TargetId = target,
        };

    [Theory]
    [InlineData(NotificationKind.RoomCreated)]
    [InlineData(NotificationKind.MatchRated)]
    [InlineData(NotificationKind.SeasonEnded)]
    [InlineData(NotificationKind.RatingRefund)]
    public void THE_ONE_THAT_MATTERS_MultiplayerStaysOutOfTheBell(NotificationKind kind)
    {
        Assert.False(NotificationView.BelongsInTheBell(kind));
        Assert.Equal(0, NotificationView.Unread(new[] { Item(kind) }));
    }

    /// <summary>Eleven of fifteen: the month's highlights joined the bell on the maintainer's request
    /// (2026-11-10, "para que todos lo vean"), under Launcher, like the project's announcements; a
    /// medal (design 121) joined it under Mods, since it is earned in one game.</summary>
    [Fact]
    public void ElevenOfTheFifteenKindsRemain()
    {
        Assert.Equal(11, Enum.GetValues<NotificationKind>().Count(NotificationView.BelongsInTheBell));
        Assert.True(NotificationView.BelongsInTheBell(NotificationKind.AchievementUnlocked));
        Assert.True(NotificationView.Passes(NotificationFilter.Mods, NotificationKind.AchievementUnlocked));
        Assert.True(NotificationView.BelongsInTheBell(NotificationKind.MonthlyHighlights));
        Assert.True(NotificationView.Passes(NotificationFilter.Launcher, NotificationKind.MonthlyHighlights));
    }

    /// <summary>Design 120: only the launcher's version and the project's news wear the launcher's
    /// icon. Being offline and the month's highlights sit under Launcher but are about something
    /// else, and no mod notice is the launcher's.</summary>
    [Fact]
    public void OnlyTheVersionAndTheNewsAreTheLaunchersOwn()
    {
        var own = Enum.GetValues<NotificationKind>().Where(NotificationView.IsAboutTheLauncher).ToList();
        Assert.Equal(new[] { NotificationKind.LauncherUpdate, NotificationKind.Announcement }, own);
        Assert.All(own, k => Assert.False(NotificationView.IsAboutAMod(k)));
        Assert.All(own, k => Assert.True(NotificationView.Passes(NotificationFilter.Launcher, k)));
    }

    [Fact]
    public void EveryBellKindFallsInExactlyOneFilter()
    {
        foreach (var kind in Enum.GetValues<NotificationKind>().Where(NotificationView.BelongsInTheBell))
            Assert.NotEqual(NotificationView.Passes(NotificationFilter.Mods, kind),
                NotificationView.Passes(NotificationFilter.Launcher, kind));
    }

    [Fact]
    public void ADayIsTheCalendarsNotTwentyFourHours()
    {
        var justBeforeMidnight = new DateTime(2026, 11, 5, 23, 59, 0, DateTimeKind.Local).ToUniversalTime();
        var justAfter = new DateTime(2026, 11, 6, 0, 1, 0, DateTimeKind.Local);
        Assert.Equal(NotificationDay.Yesterday, NotificationView.DayOf(justBeforeMidnight, justAfter));
    }

    [Fact]
    public void GroupsRunFromTodayBackwards()
    {
        var groups = NotificationView.Group(new List<NotificationItem>
        {
            Item(NotificationKind.NewMod, 30),
            Item(NotificationKind.UpdateFinished),
            Item(NotificationKind.Installed, 1),
            Item(NotificationKind.UpdateAvailable, 3),
        }, NotificationFilter.All, Now);

        Assert.Equal(new[] { NotificationDay.Today, NotificationDay.Yesterday, NotificationDay.ThisWeek, NotificationDay.Earlier },
            groups.Select(g => g.Day));
    }

    [Fact]
    public void LosingTheConnectionIsAmberAndGettingItBackIsGreen()
    {
        Assert.Equal(NotificationTone.Error, NotificationView.ToneOf(Item(NotificationKind.Connectivity, target: "offline")));
        Assert.Equal(NotificationTone.Ok, NotificationView.ToneOf(Item(NotificationKind.Connectivity, target: "online")));
    }

    /// <summary>Last month's highlights are gold, not the blue of an ordinary notice.</summary>
    [Fact]
    public void TheMonthsHighlightsAreGold()
        => Assert.Equal(NotificationTone.Highlight, NotificationView.ToneOf(Item(NotificationKind.MonthlyHighlights)));

    [Fact]
    public void ClearingTheReadOnesLeavesWhatTheBellDoesNotShow()
    {
        var center = new NotificationCenter(new LauncherConfig());
        center.Items.Add(Item(NotificationKind.UpdateFinished, read: true));
        center.Items.Add(Item(NotificationKind.MatchRated, read: true));
        center.Items.Add(Item(NotificationKind.NewMod));

        center.ClearRead();

        Assert.Equal(new[] { NotificationKind.MatchRated, NotificationKind.NewMod }, center.Items.Select(i => i.Kind));
    }
}
