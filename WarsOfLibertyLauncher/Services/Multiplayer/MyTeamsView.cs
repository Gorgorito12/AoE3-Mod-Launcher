using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>A format a team has enough players for, or how many it lacks.</summary>
public sealed record TeamFormatChip(string Text, bool Ok, string Tip);

/// <summary>The rules of the My teams window, apart from the window.</summary>
public static class MyTeamsView
{
    /// <summary>The server's cap on members per team (teams/rest.ts TEAM_MAX_MEMBERS).</summary>
    public const int MaxMembers = 6;

    public static string NameOf(TeamMember member)
        => member.DisplayName ?? member.DiscordUsername ?? member.UserId;

    /// <summary>What the rail says about what a team can enter.</summary>
    public static string Fit(int members) => Strings.Get(members >= 3 ? "MtFitBoth" : members == 2 ? "MtFitTwo" : "MtFitNone");

    public static IReadOnlyList<TeamFormatChip> Chips(int members)
    {
        TeamFormatChip Chip(int size, string label)
        {
            var missing = size - members;
            return missing <= 0
                ? new TeamFormatChip(label, true, Strings.Format("MtChipOkTip", label))
                : new TeamFormatChip(missing == 1 ? Strings.Format("MtChipShortOne", label) : Strings.Format("MtChipShort", label, missing),
                    false, Strings.Format("MtChipShortTip", label, size));
        }
        return new[] { Chip(2, "2v2"), Chip(3, "3v3") };
    }

    /// <summary>The captain, then the viewer, then who is online, then the rest: each in the order
    /// the server gave them.</summary>
    public static IReadOnlyList<TeamMember> Roster(TeamSummary team, string? me, Func<string, bool> isOnline)
    {
        int Rank(TeamMember m)
            => m.UserId == team.OwnerUserId ? 0 : m.UserId == me ? 1 : isOnline(m.UserId) ? 2 : 3;
        return (team.Members ?? new List<TeamMember>()).OrderBy(Rank).ToList();
    }

    /// <summary>"a day ago", from a server's timestamp, or null when it cannot be read.</summary>
    public static string? Ago(string? timestamp, DateTimeOffset now)
    {
        if (!DateTimeOffset.TryParse(timestamp, out var then)) return null;
        return Strings.Format("MtAgo", RoomAgeFormat.Coarse(now - then));
    }

    /// <summary>Why a new team cannot be created yet, or null.</summary>
    public static string? NameBlock(string? name, int minLength)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) return Strings.Get("MtBlockName");
        return text.Length < minLength ? Strings.Format("MtBlockShort", minLength) : null;
    }
}
