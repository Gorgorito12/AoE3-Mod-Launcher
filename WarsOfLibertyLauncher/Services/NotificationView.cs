using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher.Services;

public enum NotificationFilter { All, Mods, Launcher }

public enum NotificationTone { Ok, Info, Error }

public enum NotificationDay { Today, Yesterday, ThisWeek, Earlier }

/// <summary>What the row's one button does. <c>None</c> draws no button.</summary>
public enum NotificationAction { None, Update, Play, View, RestartAndUpdate }

public sealed record NotificationDayGroup(NotificationDay Day, IReadOnlyList<NotificationItem> Items);

/// <summary>
/// The bell's rules (design 70): it is for mods and the launcher alone, in groups by day. Pure, so
/// they can be pinned without a window.
/// </summary>
public static class NotificationView
{
    /// <summary>The four multiplayer kinds stay out so they cannot bury the rest; each has a place
    /// of its own in the Multiplayer tab (70c). An item is still kept in the history. The month's
    /// highlights are the one multiplayer item that IS in the bell (the maintainer's call,
    /// 2026-11-10): once a month, news for everybody, under Launcher like an announcement.</summary>
    public static bool BelongsInTheBell(NotificationKind kind) => kind is not (
        NotificationKind.RoomCreated or NotificationKind.MatchRated
        or NotificationKind.SeasonEnded or NotificationKind.RatingRefund);

    public static bool IsAboutAMod(NotificationKind kind) => kind is
        NotificationKind.UpdateAvailable or NotificationKind.UpdateFinished or NotificationKind.Installed
        or NotificationKind.NewTranslation or NotificationKind.NewMod or NotificationKind.ModPatchPublished;

    public static bool Passes(NotificationFilter filter, NotificationKind kind) => filter switch
    {
        NotificationFilter.Mods => IsAboutAMod(kind),
        NotificationFilter.Launcher => !IsAboutAMod(kind),
        _ => true,
    };

    public static NotificationTone ToneOf(NotificationItem item) => item.Kind switch
    {
        NotificationKind.UpdateFinished or NotificationKind.Installed => NotificationTone.Ok,
        NotificationKind.Connectivity => item.TargetId == "offline" ? NotificationTone.Error : NotificationTone.Ok,
        _ => NotificationTone.Info,
    };

    public static NotificationAction ActionOf(NotificationKind kind) => kind switch
    {
        NotificationKind.UpdateAvailable => NotificationAction.Update,
        NotificationKind.UpdateFinished or NotificationKind.Installed => NotificationAction.Play,
        NotificationKind.LauncherUpdate => NotificationAction.RestartAndUpdate,
        NotificationKind.Connectivity => NotificationAction.None,
        _ => NotificationAction.View,
    };

    /// <summary>In local time: "yesterday" is what the player's calendar says.</summary>
    public static NotificationDay DayOf(DateTime createdUtc, DateTime nowLocal)
    {
        var day = createdUtc.ToLocalTime().Date;
        var today = nowLocal.Date;
        if (day >= today) return NotificationDay.Today;
        if (day == today.AddDays(-1)) return NotificationDay.Yesterday;
        return day > today.AddDays(-7) ? NotificationDay.ThisWeek : NotificationDay.Earlier;
    }

    public static IEnumerable<NotificationItem> BellItems(IEnumerable<NotificationItem> items)
        => items.Where(i => BelongsInTheBell(i.Kind));

    public static int Unread(IEnumerable<NotificationItem> items, NotificationFilter filter = NotificationFilter.All)
        => BellItems(items).Count(i => !i.Read && Passes(filter, i.Kind));

    public static IReadOnlyList<NotificationDayGroup> Group(
        IEnumerable<NotificationItem> items, NotificationFilter filter, DateTime nowLocal)
        => BellItems(items)
            .Where(i => Passes(filter, i.Kind))
            .OrderByDescending(i => i.CreatedAtUtc)
            .GroupBy(i => DayOf(i.CreatedAtUtc, nowLocal))
            .OrderBy(g => g.Key)
            .Select(g => new NotificationDayGroup(g.Key, g.ToList()))
            .ToList();
}
