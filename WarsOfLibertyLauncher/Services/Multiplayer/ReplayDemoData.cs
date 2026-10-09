using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Sample community matches for Ranking › Matches (handoff 63), drawn by the rating preview and
/// by the tests. Fabricated — nothing here reaches a server.
///
/// <para>Shaped to exercise every state the view has: competitive matches with a recording,
/// casual ones and undecided ones with none, a competitive one nobody recorded, two months of
/// matches, and two matches older than a year whose recordings are gone. A fixture where every
/// row had a button would show none of the rules.</para>
/// </summary>
internal static class ReplayDemoData
{
    private static readonly (string A, string B)[] Pairs =
    {
        ("Kaiser", "Aluclown"), ("Geaf_Argento", "UnstoppableStreletsy"), ("El Taita", "NathanR06"),
        ("Aluclown", "alexari2040"), ("UnstoppableStreletsy", "Kaiser"), ("NathanR06", "Geaf_Argento"),
    };

    private static readonly string[] Maps =
    {
        "ESOC_Florida", "ESOC_Indonesia", "ESOC_Hudson Bay", "ESOC_Baja California", "Great_Plains", "ESOC_Parallel Rivers",
    };

    /// <summary>Forty matches, newest first, the last two older than a year.</summary>
    internal static List<CommunityMatch> Matches(DateTime nowUtc)
    {
        var list = new List<CommunityMatch>();
        for (var i = 0; i < 38; i++)
        {
            var (a, b) = Pairs[i % Pairs.Length];
            var casual = i % 5 == 3;
            var undecided = i % 7 == 4;
            var unrecorded = i == 8;
            var reported = nowUtc.AddHours(-(3 + i * 26));
            var recorded = !casual && !undecided && !unrecorded;
            list.Add(Match(i, a, b, Maps[i % Maps.Length], reported, competitive: !casual,
                decided: !undecided, recorded: recorded, nowUtc));
        }
        list.Add(Match(90, "Kaiser", "NathanR06", Maps[0], nowUtc.AddDays(-396), true, true, true, nowUtc));
        list.Add(Match(91, "Aluclown", "Kaiser", Maps[1], nowUtc.AddDays(-401), true, true, true, nowUtc));
        return list;
    }

    /// <summary>One page of <see cref="Matches"/>, the way the server would hand it out.</summary>
    internal static MatchBrowsePage Page(DateTime nowUtc, int size, int offset = 0)
    {
        var all = Matches(nowUtc);
        var items = all.Skip(offset).Take(size).ToList();
        return new MatchBrowsePage
        {
            Items = items,
            NextCursor = offset + size < all.Count ? $"demo-{offset + size}" : null,
            Total = offset == 0 ? all.Count : null,
        };
    }

    private static CommunityMatch Match(int i, string a, string b, string map, DateTime reported,
        bool competitive, bool decided, bool recorded, DateTime nowUtc)
    {
        var expires = reported.AddDays(ReplayBrowse.RetentionDays);
        return new CommunityMatch
        {
            Id = $"demo-match-{i}",
            ModId = ModRegistry.WolId,
            MapName = map,
            DurationSeconds = (14 + (i * 7) % 30) * 60,
            ReportedAt = reported.ToString("yyyy-MM-dd HH:mm:ss"),
            Competitive = competitive,
            Rated = competitive && decided,
            HasReplay = recorded && expires > nowUtc,
            ReplayExpiresAt = recorded ? expires.ToString("yyyy-MM-ddTHH:mm:ssZ") : null,
            ReplaySizeBytes = recorded && expires > nowUtc ? 900_000 + i * 37_000 : null,
            Participants = new List<MatchHistoryParticipant>
            {
                new() { UserId = "demo-" + a, DisplayName = a, DiscordUsername = a, Result = decided ? 1.0 : 0.5 },
                new() { UserId = "demo-" + b, DisplayName = b, DiscordUsername = b, Result = decided ? 0.0 : 0.5 },
            },
        };
    }
}
