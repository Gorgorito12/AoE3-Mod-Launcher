using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Joins each Discord account to the recording slot that player occupied, from the in-game names
/// every launcher publishes in the room.
///
/// <para><b>Why this is separate from <see cref="MatchTeamMap"/>, which used to own it.</b> That
/// class answers "which SIDE was each account on", so after building this same join it refuses
/// any slot whose <c>teamid</c> is negative — and a negative team id is what <em>all fourteen
/// measured 1v1 recordings</em> carry, because AoE3 writes "no team" when there are no teams.
/// That refusal is right for teams and fatal for anything else: asking the team map for the join
/// would have left every 1v1 without a civilization, which is most of the matches that rate.</para>
///
/// <para>So the join lives here, the team rules stay there, and <see cref="MatchTeamMap"/> is
/// built on top of this — one implementation, so the two can never disagree about who played
/// where.</para>
///
/// <para><b>Every rule is a refusal, and that is the safety property.</b> A half-filled map would
/// attach a real person's civilization to somebody else's slot in a stored match, and nothing
/// downstream could tell. Null means "we do not know", which callers already handle by reporting
/// nothing.</para>
/// </summary>
public static class MatchSlotMap
{
    /// <summary>
    /// Discord user id to the slot that account played, or <b>null</b> when the answer is not
    /// certain.
    /// </summary>
    /// <param name="players">The recording's slots, as parsed. Only humans are considered.</param>
    /// <param name="inGameNames">
    /// Discord user id to the AoE3 profile name that player published in the room. Self-reported:
    /// see <see cref="MatchTeamMap"/> for why it cannot be inferred.
    /// </param>
    public static IReadOnlyDictionary<string, ReplayParserService.ReplayPlayer>? Resolve(
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        IReadOnlyDictionary<string, string>? inGameNames)
        => Resolve(players, inGameNames, out _);

    /// <summary>
    /// The same join, and the REASON it refused.
    ///
    /// <para>Six different refusals used to be one silent <c>null</c>, and a diagnostic bundle
    /// could not tell them apart: "there was no recording yet", "the head counts disagreed" and
    /// "somebody never published their AoE3 profile name" all looked like a match that simply had
    /// no civilizations. The reason is handed BACK rather than logged here so this class stays
    /// pure and unit-testable; the caller writes it.</para>
    ///
    /// <para><paramref name="refusal"/> is empty on success.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ReplayParserService.ReplayPlayer>? Resolve(
        IReadOnlyList<ReplayParserService.ReplayPlayer>? players,
        IReadOnlyDictionary<string, string>? inGameNames,
        out string refusal)
    {
        refusal = "";
        if (players == null)
        {
            refusal = "no recording was read";
            return null;
        }
        if (inGameNames == null || inGameNames.Count == 0)
        {
            refusal = "nobody in the room published an AoE3 profile name";
            return null;
        }

        var humans = players.Where(p => p.IsHuman).ToList();

        // The recording has to be of THIS match: the same people, all of them. A count mismatch
        // means the wrong file, or somebody the room never saw. LooksLikeThisMatch already
        // refuses such a recording upstream; this repeats it because a map built from a
        // mismatched pair would be silently wrong rather than merely absent.
        if (humans.Count != inGameNames.Count)
        {
            refusal = $"the recording has {humans.Count} human(s) and "
                      + $"{inGameNames.Count} name(s) were published";
            return null;
        }

        // Two players called the same thing cannot be told apart, and picking one would be
        // inventing an answer. Rare, and free to check.
        if (HasDuplicateNames(humans.Select(p => p.Name)))
        {
            refusal = "two slots in the recording share a name";
            return null;
        }
        if (HasDuplicateNames(inGameNames.Values))
        {
            refusal = "two members of the room published the same name";
            return null;
        }

        var bySlot = new Dictionary<string, ReplayParserService.ReplayPlayer>(StringComparer.Ordinal);
        foreach (var (userId, declared) in inGameNames)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(declared))
            {
                refusal = "a member published a blank name";
                return null;
            }

            // Same comparison FindPlayerSlot uses for the local player, so one machine's answer
            // about itself and this method's answer about everyone can never disagree.
            var match = humans.FirstOrDefault(p =>
                string.Equals(p.Name.Trim(), declared.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                refusal = $"no slot in the recording is called '{declared.Trim()}'";
                return null;
            }

            bySlot[userId] = match;
        }

        return bySlot;
    }

    internal static bool HasDuplicateNames(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
            if (!seen.Add((n ?? "").Trim())) return true;
        return false;
    }
}
