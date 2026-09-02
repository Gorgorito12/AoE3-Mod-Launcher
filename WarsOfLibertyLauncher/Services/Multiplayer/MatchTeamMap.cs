using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Works out which Discord account played on which side of a team game, from the recording's
/// team ids and the in-game names every player published in the room.
///
/// <para><b>The problem it exists for.</b> The recording knows the teams — <c>gameplayer{N}teamid</c>
/// has been parsed all along — but it names people by their AoE3 profile name. The backend knows
/// people by their Discord id. Nothing joins the two, which is why every match reported so far
/// has carried <c>team = 0</c> for everybody.</para>
///
/// <para><b>Guessing the link from the names was ruled out with measurement, not taste.</b> On one
/// machine the same person is <c>Gorgorito12</c> on Discord and <c>Gorgorito</c>, <c>gorgorito</c>
/// and <c>sdfs</c> in three different mods — the profile is per mod, and none of the three equals
/// the account. So each launcher publishes its OWN name over the room socket and this method only
/// matches what people declared about themselves.</para>
///
/// <para>Pure and free of WPF, like <see cref="MatchResultResolver"/> beside it, because every
/// rule below is a refusal and refusals are what needs pinning.</para>
/// </summary>
public static class MatchTeamMap
{
    /// <summary>
    /// The team each participant played on, normalised to <c>0, 1, 2…</c>, or <b>null</b> when the
    /// answer is not certain.
    ///
    /// <para><b>Null means "report no teams", which is exactly what happens today</b> — every
    /// participant goes down as team 0. That is the whole safety property: a HALF-filled map is
    /// worse than none, because it would put a real person on the wrong side of a real match in
    /// somebody else's history, and nothing downstream could tell.</para>
    /// </summary>
    /// <param name="players">The recording's slots, as parsed. Only humans are considered.</param>
    /// <param name="inGameNames">
    /// Discord user id → the AoE3 profile name that player published in the room. Self-reported:
    /// see the class docs for why it cannot be inferred.
    /// </param>
    public static IReadOnlyDictionary<string, int>? Resolve(
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        IReadOnlyDictionary<string, string>? inGameNames)
    {
        // Who played which slot is the same question the civilization needs answered, so it lives
        // in MatchSlotMap and is shared. Everything below is what makes this the TEAM map.
        var bySlot = MatchSlotMap.Resolve(players, inGameNames);
        if (bySlot == null) return null;

        // A negative team id is AoE3's "no team": it is what every 1v1 in a sample of fourteen
        // carries, and what a free-for-all carries. Mixed with real ids it is not a team game we
        // understand, so the whole map is refused rather than half-read.
        if (bySlot.Values.Any(p => p.TeamId < 0)) return null;

        // Teams, in the order their lowest slot appears — so the numbers mean the same thing on
        // both machines that might report this match, rather than depending on dictionary order.
        var order = bySlot.Values
            .GroupBy(p => p.TeamId)
            .OrderBy(g => g.Min(p => p.Slot))
            .Select((g, index) => (TeamId: g.Key, Normalised: index))
            .ToDictionary(x => x.TeamId, x => x.Normalised);

        // One team is not a team game — it is everyone on the same side, which AoE3 allows to be
        // set up and which says nothing about who beat whom.
        if (order.Count < 2) return null;

        return bySlot.ToDictionary(kv => kv.Key, kv => order[kv.Value.TeamId], StringComparer.Ordinal);
    }
}
