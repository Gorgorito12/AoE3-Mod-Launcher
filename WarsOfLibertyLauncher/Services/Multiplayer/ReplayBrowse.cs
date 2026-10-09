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

/// <summary>A run of matches under one heading: a calendar month, or "older than one year".</summary>
public sealed record MatchMonthGroup<T>(int Year, int Month, bool OlderThanAYear, IReadOnlyList<T> Items);

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
    /// Matches grouped by the month they were reported in (local time), in the order they
    /// arrived — newest first from the server — then ONE last group for everything older than
    /// a year, whose recordings are gone. A match with no usable date joins the newest group:
    /// it is not dropped.
    /// </summary>
    public static IReadOnlyList<MatchMonthGroup<T>> GroupByMonth<T>(
        IEnumerable<T> items, Func<T, DateTime?> reportedUtc, DateTime nowUtc)
    {
        var groups = new List<(int Year, int Month, List<T> Items)>();
        var older = new List<T>();
        foreach (var item in items)
        {
            var when = reportedUtc(item);
            if (when is DateTime w && nowUtc - w > TimeSpan.FromDays(RetentionDays))
            {
                older.Add(item);
                continue;
            }
            var local = (when ?? nowUtc).ToLocalTime();
            var group = groups.FirstOrDefault(g => g.Year == local.Year && g.Month == local.Month);
            if (group.Items == null)
            {
                group = (local.Year, local.Month, new List<T>());
                groups.Add(group);
            }
            group.Items.Add(item);
        }

        var result = groups.Select(g => new MatchMonthGroup<T>(g.Year, g.Month, false, g.Items)).ToList();
        if (older.Count > 0) result.Add(new MatchMonthGroup<T>(0, 0, true, older));
        return result;
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
