using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the enrol window (design 82) is showing.</summary>
public enum EnrollMode
{
    /// <summary>A confirmation: a 1v1, or "everyone enters alone".</summary>
    Solo,

    /// <summary>A captain enters a saved team and says who plays.</summary>
    Saved,

    /// <summary>A captain builds the line-up here.</summary>
    Build,

    /// <summary>A saved team is needed and no team of this player can enter.</summary>
    NoTeams,

    /// <summary>Already in, alone.</summary>
    Enrolled,

    /// <summary>Already in, as the captain of a team.</summary>
    TeamCaptain,

    /// <summary>Already in, as a member of somebody else's team.</summary>
    TeamMember,
}

/// <summary>One line of the facts card.</summary>
public sealed record EnrollFact(string LabelKey, string Text);

/// <summary>A team of the player's, and why it can or cannot enter.</summary>
public sealed record EnrollTeam(TeamSummary Team, bool CanEnter, string Line);

/// <summary>A player offered as a teammate.</summary>
public sealed record EnrollPlayer(string Id, string Name, bool Online, string? PlayedAgo = null);

/// <summary>A group of the teammates' list.</summary>
public sealed record EnrollPlayerGroup(string TitleKey, IReadOnlyList<EnrollPlayer> Players);

/// <summary>
/// The rules of the enrol window, apart from the window: which of its shapes applies, and what it
/// says about the tournament. It reads the tournament the server sent and adds nothing.
/// </summary>
public static class EnrollView
{
    internal static bool EntersAlone(TournamentSummary t)
        => t.Format == "1v1" || t.TeamSource == "draft";

    public static EnrollMode ModeOf(TournamentDetail t, string? me, IReadOnlyList<TeamSummary>? teams)
    {
        if (TournamentPermissions.MyEntrant(t, me) is { } mine)
        {
            if (mine.Kind == "solo" || (mine.MemberIds?.Count ?? 1) <= 1) return EnrollMode.Enrolled;
            return string.Equals(mine.CaptainUserId, me, StringComparison.Ordinal)
                ? EnrollMode.TeamCaptain : EnrollMode.TeamMember;
        }
        if (EntersAlone(t)) return EnrollMode.Solo;
        if (t.TeamSource == "adhoc") return EnrollMode.Build;
        return TeamsOf(t, me, teams).Any(x => x.CanEnter) ? EnrollMode.Saved : EnrollMode.NoTeams;
    }

    /// <summary>The player's teams, those that can enter first, each with its reason.</summary>
    public static IReadOnlyList<EnrollTeam> TeamsOf(TournamentDetail t, string? me, IReadOnlyList<TeamSummary>? teams)
    {
        var size = TournamentPermissions.RosterSizeOf(t);
        return (teams ?? Array.Empty<TeamSummary>())
            .Where(x => x.Disbanded != true)
            .Select(x => TeamRow(x, me, size))
            .OrderBy(x => x.CanEnter ? 0 : 1)
            .ToList();
    }

    private static EnrollTeam TeamRow(TeamSummary team, string? me, int size)
    {
        var members = team.Members ?? new List<TeamMember>();
        if (!string.Equals(team.OwnerUserId, me, StringComparison.Ordinal))
        {
            var captain = members.FirstOrDefault(m => m.UserId == team.OwnerUserId);
            return new EnrollTeam(team, false, Strings.Format("EnTeamNotCaptain", captain?.DisplayName ?? team.OwnerUserId ?? "?"));
        }
        var missing = size - members.Count;
        if (missing > 0)
        {
            return new EnrollTeam(team, false, missing == 1
                ? Strings.Format("EnTeamShortOne", members.Count, size)
                : Strings.Format("EnTeamShort", members.Count, size, missing));
        }
        return new EnrollTeam(team, true, Strings.Format("EnTeamMembers", members.Count,
            string.Join(", ", members.Select(m => m.DisplayName ?? m.UserId))));
    }

    /// <summary>The line under the tournament's name.</summary>
    public static string Meta(TournamentDetail t)
    {
        var parts = new List<string> { t.Format ?? "1v1" };
        var team = t.Format != "1v1";
        if (t.Capacity is { } capacity) parts.Add(Strings.Format(team ? "EnMetaTeams" : "EnMetaPlaces", capacity));
        parts.Add(Strings.Format("EnMetaEntered", Taken(t)));
        parts.Add(Strings.Get(!team ? (t.EntryMode == "approval" ? "EnHowApproval" : "EnHowOpen")
            : t.TeamSource switch
            {
                "adhoc" => "EnHowAdhoc",
                "draft" => "EnHowDraft",
                _ => "EnHowRegistered",
            }));
        return string.Join(" · ", parts);
    }

    private static int Taken(TournamentSummary t) => t.ConfirmedCount ?? t.EntrantCount ?? 0;

    private static int? PlacesLeft(TournamentSummary t)
        => t.Capacity is { } capacity ? Math.Max(0, capacity - Taken(t)) : null;

    private static string PlacesLine(TournamentSummary t)
    {
        if (PlacesLeft(t) is not { } left) return Strings.Format("EnPlacesCount", Taken(t));
        var capacity = t.Capacity!.Value;
        return left switch
        {
            0 => Strings.Format("EnPlacesFull", Taken(t), capacity),
            1 => Strings.Format("EnPlacesLeftOne", Taken(t), capacity),
            _ => Strings.Format("EnPlacesLeft", Taken(t), capacity, left),
        };
    }

    private static string LeftLine(TournamentSummary t) => PlacesLeft(t) switch
    {
        null or 0 => "",
        1 => " " + Strings.Get("EnStateLeftOne"),
        { } left => " " + Strings.Format("EnStateLeft", left),
    };

    /// <summary>"Gorgo (captain) · Cuzco", from the frozen line-up.</summary>
    public static string LineupOf(TournamentEntrant entrant)
    {
        var names = (entrant.MemberIds ?? new List<string>()).Select(id =>
        {
            var name = entrant.Members?.FirstOrDefault(m => m.UserId == id)?.DisplayName ?? id;
            return id == entrant.CaptainUserId ? Strings.Format("EnCaptainMark", name) : name;
        });
        return string.Join(" · ", names);
    }

    public static IReadOnlyList<EnrollFact> Facts(TournamentDetail t, string? me, EnrollMode mode)
    {
        var mine = TournamentPermissions.MyEntrant(t, me);
        switch (mode)
        {
            case EnrollMode.Solo:
                return new[]
                {
                    new EnrollFact("EnFactFormat", Strings.Format(t.Format == "1v1" ? "EnFormatSolo" : "EnFormatDraft", t.Format ?? "1v1")),
                    new EnrollFact("EnFactPlaces", PlacesLine(t)),
                    new EnrollFact("EnFactHow", Strings.Get(t.EntryMode == "approval" ? "EnHowApprovalLine" : "EnHowOpenLine")),
                    new EnrollFact("EnFactChange", Strings.Get("EnChangeSolo")),
                };
            case EnrollMode.Enrolled:
                return new[]
                {
                    new EnrollFact("EnFactState", StateLine(t, mine, Strings.Get("EnStateIn"))),
                    new EnrollFact("EnFactNext", Strings.Get("EnNextLine")),
                    new EnrollFact("EnFactChange", Strings.Get("EnChangeSolo")),
                };
            case EnrollMode.TeamCaptain:
                return new[]
                {
                    new EnrollFact("EnFactState", StateLine(t, mine, Strings.Format("EnStateTeamIn", mine?.DisplayName ?? ""))),
                    new EnrollFact("EnFactLineup", mine == null ? "" : LineupOf(mine)),
                    new EnrollFact("EnFactChange", Strings.Get("EnChangeTeam")),
                };
            case EnrollMode.TeamMember:
                var captain = mine?.Members?.FirstOrDefault(m => m.UserId == mine.CaptainUserId)?.DisplayName
                              ?? mine?.CaptainUserId ?? "?";
                return new[]
                {
                    new EnrollFact("EnFactState", StateLine(t, mine, Strings.Format("EnStateTeamIn", mine?.DisplayName ?? ""))),
                    new EnrollFact("EnFactLineup", mine == null ? "" : LineupOf(mine)),
                    new EnrollFact("EnFactLeave", Strings.Format("EnLeaveMember", captain)),
                };
            default:
                return Array.Empty<EnrollFact>();
        }
    }

    private static string StateLine(TournamentSummary t, TournamentEntrant? mine, string entered) => mine?.Status switch
    {
        "pending" => Strings.Get("EnStatePending"),
        "waitlist" => Strings.Get("EnStateWaitlist"),
        _ => entered + LeftLine(t),
    };

    /// <summary>Who may not be picked: everybody already in this tournament.</summary>
    public static HashSet<string> TakenPlayers(TournamentDetail t)
        => new((t.Entrants ?? new List<TournamentEntrant>())
            .Where(e => e.Status is "pending" or "confirmed" or "waitlist")
            .SelectMany(e => e.MemberIds ?? new List<string>()), StringComparer.Ordinal);

    /// <summary>
    /// The list of teammates. With no search: who played with you recently, then who is online.
    /// With one: whoever matches, online first, then the server's other results.
    /// </summary>
    public static IReadOnlyList<EnrollPlayerGroup> Groups(
        IEnumerable<EnrollPlayer> recent, IEnumerable<EnrollPlayer> online, IEnumerable<EnrollPlayer> found, string? query, string? me)
    {
        var text = (query ?? "").Trim();
        bool Keep(EnrollPlayer p) => p.Id != me;
        bool Fits(EnrollPlayer p) => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
        var near = text.Length == 0 ? recent.Where(Keep).DistinctBy(p => p.Id).ToList() : new List<EnrollPlayer>();
        var seen = new HashSet<string>(near.Select(p => p.Id), StringComparer.Ordinal);
        var live = online.Where(p => Keep(p) && !seen.Contains(p.Id) && (text.Length == 0 || Fits(p))).DistinctBy(p => p.Id).ToList();
        foreach (var p in live) seen.Add(p.Id);
        var rest = text.Length == 0
            ? new List<EnrollPlayer>()
            : found.Where(p => Keep(p) && !seen.Contains(p.Id)).DistinctBy(p => p.Id).ToList();

        var groups = new List<EnrollPlayerGroup>();
        if (near.Count > 0) groups.Add(new EnrollPlayerGroup("EnGroupRecent", near));
        if (live.Count > 0) groups.Add(new EnrollPlayerGroup("EnGroupOnline", live));
        if (rest.Count > 0) groups.Add(new EnrollPlayerGroup("EnGroupOther", rest));
        return groups;
    }

    /// <summary>What still stops a saved team, or null.</summary>
    public static string? LineupBlock(int chosen, int size)
    {
        if (chosen == size) return null;
        if (chosen > size) return Strings.Format("EnBlockExtra", chosen - size, size);
        return size - chosen == 1 ? Strings.Get("EnBlockMoreOne") : Strings.Format("EnBlockMore", size - chosen);
    }

    /// <summary>What still stops a built line-up, or null. The captain counts as one of the team.</summary>
    public static string? BuildBlock(int mates, int size, string? teamName)
    {
        var missing = size - 1 - mates;
        if (missing > 0) return missing == 1 ? Strings.Get("EnBlockMatesOne") : Strings.Format("EnBlockMates", missing);
        return (teamName ?? "").Trim().Length < 2 ? Strings.Get("EnBlockName") : null;
    }

    /// <summary>"A, B and C".</summary>
    public static string JoinNames(IReadOnlyList<string> names)
        => names.Count < 2 ? string.Concat(names)
            : string.Join(", ", names.Take(names.Count - 1)) + Strings.Get("EnAnd") + names[^1];

    /// <summary>The two letters of a team's crest.</summary>
    public static string CrestOf(string? name)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]) && w.Length > 2).ToList();
        if (words.Count >= 2) return (char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[^1][0])).ToString();
        var one = (name ?? "?").Trim();
        return one.Length == 0 ? "?" : one[..Math.Min(2, one.Length)].ToUpperInvariant();
    }

    /// <summary>The players who were asked for and are in the tournament by now: what a refusal
    /// of the whole line-up comes down to.</summary>
    public static IReadOnlyList<string> NowTaken(TournamentDetail fresh, IEnumerable<string> chosen)
    {
        var taken = TakenPlayers(fresh);
        return chosen.Where(taken.Contains).ToList();
    }
}
