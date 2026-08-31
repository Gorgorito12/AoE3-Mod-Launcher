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
        if (players == null || inGameNames == null || inGameNames.Count == 0) return null;

        var humans = players.Where(p => p.IsHuman).ToList();

        // The recording has to be of THIS match: the same people, all of them. A count mismatch
        // means the wrong file, or somebody the room never saw — either way there is nothing
        // here worth reporting. LooksLikeThisMatch already refuses such a recording upstream;
        // this repeats it because a map built from a mismatched pair would be silently wrong
        // rather than merely absent.
        if (humans.Count != inGameNames.Count) return null;

        // Two players called the same thing cannot be told apart, and picking one would be
        // inventing an answer. Rare, and free to check.
        if (HasDuplicateNames(humans.Select(p => p.Name))) return null;
        if (HasDuplicateNames(inGameNames.Values)) return null;

        var bySlot = new Dictionary<string, ReplayParserService.ReplayPlayer>();
        foreach (var (userId, declared) in inGameNames)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(declared)) return null;

            // Same comparison FindPlayerSlot uses for the local player, so one machine's answer
            // about itself and this method's answer about everyone can never disagree.
            var match = humans.FirstOrDefault(p =>
                string.Equals(p.Name.Trim(), declared.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;

            bySlot[userId] = match;
        }

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

    private static bool HasDuplicateNames(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
            if (!seen.Add((n ?? "").Trim())) return true;
        return false;
    }
}
