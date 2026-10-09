using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Which end of the Matches list comes first.</summary>
public enum MatchBrowseSort
{
    Newest,
    Oldest,
}

/// <summary>What kind of room the Matches list keeps.</summary>
public enum MatchBrowseKind
{
    All,
    Competitive,
    Casual,
}

/// <summary>
/// Everything Ranking › Matches asks <c>GET /matches</c> for, and the same rules applied to the
/// preview's samples so the preview behaves like the server.
///
/// <para><b>Every filter runs on the server.</b> The list is paged by keyset thirty at a time, so
/// filtering what is loaded would show "Showing 30 of 412" over a list that is really twenty
/// long.</para>
///
/// <para><b>A request with nothing set is byte-for-byte the one this view made before the new
/// filters existed</b>: a default is never sent. An older server ignores parameters it does not
/// know and would answer with the whole list, which is why the view only offers the new controls
/// once a page says the server applies them (<see cref="MatchBrowsePage.Filters"/>).</para>
/// </summary>
public sealed record MatchBrowseQuery
{
    /// <summary>The only period windows the server accepts, in days.</summary>
    public static readonly IReadOnlyList<int> PeriodDays = new[] { 1, 7, 30 };

    /// <summary>The filters a page must advertise before the view offers them.</summary>
    public static readonly IReadOnlyList<string> NewFilters = new[] { "sort", "days", "kind", "decided" };

    public string Query { get; init; } = "";
    public bool ReplayOnly { get; init; }
    public string? ModId { get; init; }
    public MatchBrowseSort Sort { get; init; } = MatchBrowseSort.Newest;

    /// <summary>A window ending now, in days; null for any date.</summary>
    public int? Days { get; init; }

    public MatchBrowseKind Kind { get; init; } = MatchBrowseKind.All;

    /// <summary>Only matches somebody won.</summary>
    public bool DecidedOnly { get; init; }

    /// <summary>Whether a filter is narrowing the list. The order is not a filter.</summary>
    public bool IsFiltered => !string.IsNullOrEmpty(Query) || ReplayOnly || !string.IsNullOrEmpty(ModId) || NarrowsBeyondTheSearch;

    /// <summary>Whether a filter other than the search and the replay chip is on: the mod or one of the new four.</summary>
    public bool NarrowsBeyondTheSearch => !string.IsNullOrEmpty(ModId) || Days != null || Kind != MatchBrowseKind.All || DecidedOnly;

    /// <summary>Whether a page carrying these filter names applies every new one.</summary>
    public static bool ServerApplies(IReadOnlyCollection<string>? filters)
        => filters != null && NewFilters.All(f => filters.Contains(f, StringComparer.OrdinalIgnoreCase));

    /// <summary>The request path for one page. Defaults are left out.</summary>
    public string ToPath(string? cursor, int limit)
    {
        var path = $"matches?limit={limit}";
        if (!string.IsNullOrEmpty(cursor)) path += "&cursor=" + Uri.EscapeDataString(cursor);
        if (!string.IsNullOrWhiteSpace(Query)) path += "&q=" + Uri.EscapeDataString(Query.Trim());
        if (ReplayOnly) path += "&replay=1";
        if (!string.IsNullOrWhiteSpace(ModId)) path += "&mod=" + Uri.EscapeDataString(ModId.Trim());
        if (Sort == MatchBrowseSort.Oldest) path += "&sort=oldest";
        if (Days is int d && PeriodDays.Contains(d)) path += "&days=" + d;
        if (Kind == MatchBrowseKind.Competitive) path += "&kind=competitive";
        else if (Kind == MatchBrowseKind.Casual) path += "&kind=casual";
        if (DecidedOnly) path += "&decided=1";
        return path;
    }

    /// <summary>
    /// The server's rules over a list held in memory — the preview's samples. A match whose room
    /// kind is unknown is in neither kind, as on the server: calling an unknown room casual is
    /// the mistake the mode label refuses to make.
    /// </summary>
    public IEnumerable<CommunityMatch> Apply(IEnumerable<CommunityMatch> matches, DateTime nowUtc)
    {
        var all = matches;
        if (ReplayOnly) all = all.Where(m => ReplayBrowse.Decide(m, nowUtc) == ReplayAvailability.Available);
        if (!string.IsNullOrEmpty(Query))
            all = all.Where(m => m.Participants.Any(p =>
                p.DisplayName.Contains(Query, StringComparison.OrdinalIgnoreCase)
                || p.DiscordUsername.Contains(Query, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrEmpty(ModId))
            all = all.Where(m => string.Equals(m.ModId, ModId, StringComparison.OrdinalIgnoreCase));
        if (Kind == MatchBrowseKind.Competitive) all = all.Where(m => m.Competitive == true);
        else if (Kind == MatchBrowseKind.Casual) all = all.Where(m => m.Competitive == false);
        if (Days is int days)
        {
            var since = nowUtc.AddDays(-days);
            all = all.Where(m => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt) is DateTime r && r >= since);
        }
        if (DecidedOnly) all = all.Where(m => m.Participants.Any(p => p.Result >= 0.999));

        DateTime When(CommunityMatch m) => RoomAgeFormat.ParseCreatedUtc(m.ReportedAt) ?? DateTime.MinValue;
        return Sort == MatchBrowseSort.Oldest
            ? all.OrderBy(When).ThenBy(m => m.Id, StringComparer.Ordinal)
            : all.OrderByDescending(When).ThenByDescending(m => m.Id, StringComparer.Ordinal);
    }
}
