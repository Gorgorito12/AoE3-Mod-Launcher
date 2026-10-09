using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What a match row offers for its recording (design handoff 63).</summary>
public enum ReplayAvailability
{
    /// <summary>No recording, or a server that does not say: the right column stays empty.</summary>
    None,

    /// <summary>In the bucket and inside its year: the download button.</summary>
    Available,

    /// <summary>There was one and the year is over: the word "expired", no button.</summary>
    Expired,
}

/// <summary>What a heading of the Matches view names.</summary>
public enum MatchGroupKind
{
    Today,
    Yesterday,
    /// <summary>Two to six days ago, local time.</summary>
    ThisWeek,
    Month,
    OlderThanAYear,
}

/// <summary>Which heading a match falls under. <see cref="Year"/> and <see cref="Month"/> are only meaningful for a month.</summary>
public readonly record struct MatchGroupKey(MatchGroupKind Kind, int Year, int Month);

/// <summary>
/// A run of matches under one heading: today, yesterday, this week, a calendar month, or "older
/// than one year". <see cref="Year"/> and <see cref="Month"/> are only meaningful for a month.
/// </summary>
public sealed record MatchGroup<T>(MatchGroupKind Kind, int Year, int Month, IReadOnlyList<T> Items)
{
    public bool OlderThanAYear => Kind == MatchGroupKind.OlderThanAYear;

    public MatchGroupKey Key => new(Kind, Year, Month);
}

/// <summary>
/// The decisions behind downloading a recording from Ranking (handoff 63), kept pure so they
/// are tested off the UI thread: which state a row's button is in, where a file lands, how the
/// Matches view lays its rows out and how it groups them.
/// </summary>
public static class ReplayBrowse
{
    /// <summary>
    /// How long a recording is kept: the bucket's lifecycle rule. The server sends the expiry
    /// itself; this is only what "older than one year" means for the Matches view's last group.
    /// </summary>
    public const int RetentionDays = 365;

    /// <summary>Rows say "N h/d ago" up to this many days, then a date.</summary>
    public const int RelativeAgeDays = 30;

    /// <summary>
    /// The button's state from the two fields the server sends.
    ///
    /// <para><b>A server that sends no <c>has_replay</c> offers nothing</b>, whatever else it
    /// sends: an older backend is "we do not know", and a button that answers "no recording"
    /// on click is worse than no button. A recording past its date reads expired even while
    /// the server still says it has one — the bucket deletes on its own schedule, and the
    /// date is what the player was promised.</para>
    /// </summary>
    public static ReplayAvailability Decide(bool? hasReplay, string? expiresAt, DateTime nowUtc)
    {
        var expires = ParseUtc(expiresAt);
        if (hasReplay == true)
            return expires is DateTime e && e <= nowUtc ? ReplayAvailability.Expired : ReplayAvailability.Available;
        if (hasReplay == false && expires != null)
            return ReplayAvailability.Expired;
        return ReplayAvailability.None;
    }

    /// <summary>The state of a community match's recording.</summary>
    public static ReplayAvailability Decide(CommunityMatch m, DateTime nowUtc)
        => Decide(m.HasReplay, m.ReplayExpiresAt, nowUtc);

    /// <summary>An ISO-8601 (or SQLite) UTC timestamp, or null.</summary>
    public static DateTime? ParseUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc)
            : null;
    }

    /// <summary>
    /// Where a download is saved without asking: <paramref name="fileName"/> in
    /// <paramref name="folder"/> when that is free, else "name (2).age3Yrec", "(3)"… — a file
    /// already there is never overwritten (handoff 63). Past 99 a short random suffix, which
    /// only a folder of a hundred copies of one match would ever reach.
    /// </summary>
    public static string UniqueReplayPath(string folder, string fileName, Func<string, bool> exists)
    {
        var first = Path.Combine(folder, fileName);
        if (!exists(first)) return first;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var n = 2; n <= 99; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!exists(candidate)) return candidate;
        }
        return Path.Combine(folder, $"{stem}-{Guid.NewGuid().ToString("N")[..8]}{ext}");
    }

    /// <summary>The Matches view's columns of rows: 1 below 900 px, 2 below 1900, 3 from there.</summary>
    public static int Columns(double width)
        => width >= 1900 ? 3 : width >= 900 ? 2 : 1;

    /// <summary>
    /// A row's age: "N h/d ago" (<paramref name="recent"/>, the launcher's own wording) up to
    /// <see cref="RelativeAgeDays"/>, then "28 sep", then "sep 2025" past a year — in the
    /// launcher's language, never Windows'.
    /// </summary>
    public static string AgeText(DateTime reportedUtc, DateTime nowUtc, CultureInfo culture,
        Func<TimeSpan, string> recent)
    {
        var elapsed = nowUtc - reportedUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed <= TimeSpan.FromDays(RelativeAgeDays)) return recent(elapsed);

        var local = reportedUtc.ToLocalTime();
        var month = ShortMonth(local.Month, culture);
        return elapsed > TimeSpan.FromDays(RetentionDays)
            ? $"{month} {local.Year}"
            : $"{local.Day} {month}";
    }

    /// <summary>
    /// A month's three-letter name in the culture's own case ("sep" in Spanish, "Sep" in
    /// English). The first three letters of the full name, because some cultures' abbreviations
    /// carry a period or a fourth letter ("sept.") the design does not.
    /// </summary>
    public static string ShortMonth(int month, CultureInfo culture)
    {
        var full = culture.DateTimeFormat.GetMonthName(month);
        return full.Length <= 3 ? full : full[..3];
    }

    /// <summary>A month heading: "OCTOBER 2026" / "OCTUBRE 2026".</summary>
    public static string MonthHeading(int year, int month, CultureInfo culture)
        => $"{culture.DateTimeFormat.GetMonthName(month).ToUpper(culture)} {year}";

    /// <summary>
    /// The Matches view's headings, in the order the matches arrived.
    ///
    /// <para><b>Newest first</b> (<paramref name="ascending"/> false): TODAY, YESTERDAY and THIS
    /// WEEK (two to six days ago), then one heading per calendar month, then "older than one
    /// year", whose recordings are gone. The recent headings are what make "the newest ones"
    /// findable at a glance: a month heading over sixty cards does not.</para>
    ///
    /// <para><b>Oldest first</b>: months only, and "older than one year" therefore comes FIRST,
    /// because that is where those matches arrive.</para>
    ///
    /// <para>Days are counted in LOCAL time — "yesterday" is the player's yesterday. A match with no
    /// usable date joins the heading of the match before it (the server orders by date, so that is
    /// its neighbour), or the first heading when it leads the list; only a list of nothing but
    /// undated matches goes under the heading for "now". It is never dropped.</para>
    ///
    /// <para><paramref name="continueFrom"/> groups a page APPENDED to a list already on screen: it
    /// is the heading of the last match drawn, so an undated match at the start of the new page
    /// joins that heading — where grouping the whole list at once would put it. The caller merges
    /// a returned group whose key is already drawn into the one on screen.</para>
    /// </summary>
    public static IReadOnlyList<MatchGroup<T>> GroupForBrowse<T>(
        IEnumerable<T> items, Func<T, DateTime?> reportedUtc, DateTime nowUtc, bool ascending,
        MatchGroupKey? continueFrom = null)
    {
        var groups = new List<(MatchGroupKey Key, List<T> Items)>();
        var undated = new List<T>();
        var today = nowUtc.ToLocalTime().Date;
        var last = continueFrom;

        foreach (var item in items)
        {
            MatchGroupKey key;
            if (reportedUtc(item) is not DateTime when)
            {
                if (last == null)
                {
                    undated.Add(item);
                    continue;
                }
                key = last.Value;
            }
            else
            {
                key = KeyOf(when, nowUtc, today, ascending);
            }

            var index = groups.FindIndex(g => g.Key == key);
            if (index < 0)
            {
                groups.Add((key, new List<T>()));
                index = groups.Count - 1;
            }
            groups[index].Items.Add(item);
            last = key;
        }

        if (undated.Count > 0)
        {
            if (groups.Count > 0) groups[0].Items.InsertRange(0, undated);
            else
            {
                var now = nowUtc.ToLocalTime();
                groups.Add((ascending
                    ? new MatchGroupKey(MatchGroupKind.Month, now.Year, now.Month)
                    : new MatchGroupKey(MatchGroupKind.Today, 0, 0), undated));
            }
        }

        return groups.Select(g => new MatchGroup<T>(g.Key.Kind, g.Key.Year, g.Key.Month, g.Items)).ToList();
    }

    private static MatchGroupKey KeyOf(DateTime whenUtc, DateTime nowUtc, DateTime todayLocal, bool ascending)
    {
        if (nowUtc - whenUtc > TimeSpan.FromDays(RetentionDays)) return new(MatchGroupKind.OlderThanAYear, 0, 0);
        var local = whenUtc.ToLocalTime();
        if (!ascending)
        {
            var days = (todayLocal - local.Date).Days;
            if (days <= 0) return new(MatchGroupKind.Today, 0, 0);
            if (days == 1) return new(MatchGroupKind.Yesterday, 0, 0);
            if (days < 7) return new(MatchGroupKind.ThisWeek, 0, 0);
        }
        return new(MatchGroupKind.Month, local.Year, local.Month);
    }

    /// <summary>
    /// Whether the Matches view should ask for its next page now: there is one, nothing is in
    /// flight, the last request did not fail, the view is laid out, and the end of what is loaded
    /// is less than one screen below what the reader sees. The last clause also covers a list too
    /// short to fill the window, which therefore fills itself.
    ///
    /// <para><b>A failure stops it until the reader presses Retry.</b> Scrolling is not consent to
    /// try again: a page that failed would otherwise be re-requested on every scroll tick, against
    /// a per-IP quota (30 a minute) that a NAT shares.</para>
    /// </summary>
    public static bool ShouldLoadMore(double extentHeight, double viewportHeight, double verticalOffset,
        bool hasMore, bool busy, bool failed)
    {
        if (!hasMore || busy || failed) return false;
        if (!(viewportHeight > 0)) return false;
        return extentHeight - verticalOffset - viewportHeight < viewportHeight;
    }

    /// <summary>"1.4 MB" / "1,4 MB": one decimal, in the launcher's language.</summary>
    public static string SizeText(long bytes, CultureInfo culture)
        => (Math.Max(0, bytes) / 1048576.0).ToString("0.0", culture) + " MB";

    /// <summary>
    /// The match a toast is about: "A vs B · map". The first player of each side, so a team
    /// game does not read as a list; the map only when the server named one.
    /// </summary>
    public static string MatchLine(IReadOnlyList<MatchParticipantLine> players, string? mapName, string versusWord)
    {
        var sides = MatchParticipantsView.SidesOf(players);
        var names = sides.Count >= 2
            ? string.Join($" {versusWord} ", sides.Take(2).Select(s => s[0].Name))
            : string.Join(" · ", players.Select(p => p.Name));
        var map = string.IsNullOrWhiteSpace(mapName) ? null : mapName!.Replace('_', ' ');
        if (string.IsNullOrWhiteSpace(names)) return map ?? "";
        return map == null ? names : $"{names} · {map}";
    }
}
