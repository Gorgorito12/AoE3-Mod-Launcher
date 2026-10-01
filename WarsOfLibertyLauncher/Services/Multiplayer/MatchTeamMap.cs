using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Works out which Discord account played on which side of a team game, from the recording's
/// team ids and the in-game names every player published in the room.
///
/// <para><b>The problem it exists for.</b> The recording knows the teams — the map-setup string the
/// game writes at start, see <see cref="ReplayParserService.ReplayPlayer.Team"/>; <b>not</b>
/// <c>gameplayer{N}teamid</c>, which is only the lobby's dropdown and is usually -1 — but it names
/// people by their AoE3 profile name. The backend knows people by their Discord id. Nothing joins
/// the two, which is why every match reported so far has carried <c>team = 0</c> for everybody.</para>
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

        // The side the GAME assigned (the map-setup string), with the lobby's dropdown only as the
        // fallback — see ReplayPlayer.Team. Keying this on the raw teamid is what refused all four
        // of the first competitive 2v2s: nobody had touched the dropdown, so every slot read -1.
        // A negative side is still "not known", and mixed with real ones it is not a team game we
        // understand, so the whole map is refused rather than half-read.
        if (bySlot.Values.Any(p => p.Team < 0)) return null;

        // Teams, in the order their lowest slot appears — so the numbers mean the same thing on
        // both machines that might report this match, rather than depending on dictionary order.
        var order = bySlot.Values
            .GroupBy(p => p.Team)
            .OrderBy(g => g.Min(p => p.Slot))
            .Select((g, index) => (Team: g.Key, Normalised: index))
            .ToDictionary(x => x.Team, x => x.Normalised);

        // One team is not a team game — it is everyone on the same side, which AoE3 allows to be
        // set up and which says nothing about who beat whom.
        if (order.Count < 2) return null;

        // Nor is a game where every side is ONE player. The setup string gives every recording a
        // side per player, so a 1v1 now reads as 0/1 and a free-for-all as 0/1/2/3 — real, and
        // not teams. Without this a 1v1 room would log a spurious format mismatch and a casual
        // 1v1 would start storing teams it never had; with it, both stay exactly as they were.
        if (bySlot.Values.GroupBy(p => p.Team).All(g => g.Count() < 2)) return null;

        return bySlot.ToDictionary(kv => kv.Key, kv => order[kv.Value.Team], StringComparer.Ordinal);
    }
}
