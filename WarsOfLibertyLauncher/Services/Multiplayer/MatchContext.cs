using System;
using System.Collections.Generic;
using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Everything the launcher needs to report a match, captured the moment AoE3 is launched.
///
/// <para><b>Why this type exists.</b> A real match went unrecorded because the host closed the
/// room while it was still running. Leaving the room tears the socket down, and that teardown
/// cleared the roster, killed the game and set "we are not the host" — all before the
/// <c>OnGameExited</c> that the kill itself triggered. The report then read live state that no
/// longer described the match and skipped itself with "not host of this room". Nothing was
/// wrong with the reporting logic; it was simply asking questions whose answers had already
/// been thrown away.</para>
///
/// <para>So the facts of a match are captured once, at launch, and consumed at exit — the same
/// shape as <c>MainWindow._settingsSyncProfile</c> ("the profile captured AT LAUNCH, not the one
/// on screen"). The property that matters is a negative one: <see cref="CanReport"/> has no
/// parameter and no field through which live room state can enter, so no teardown can change
/// its answer. That is the whole fix, expressed as a type.</para>
///
/// <para>Pure and free of WPF, like its sibling <see cref="MatchResultResolver"/> — which is
/// what turns this context's participant list plus the recording into each player's score.</para>
/// </summary>
/// <param name="IsHost">
/// Whether WE are the room's host, decided at launch. Only the host reports: the exit runs on
/// every player's client and the POST inserts a row per participant, so N reporters would mean
/// N duplicate matches.
/// </param>
/// <param name="Participants">Normalised room roster — see <see cref="Capture"/>.</param>
/// <param name="ReporterUserId">Our own user id, needed to work out who won from the host's score.</param>
/// <param name="StartedAtUtc">When AoE3 was launched. Also the start stamp sent to the backend.</param>
public sealed record MatchContext(
    bool IsHost,
    IReadOnlyList<string> Participants,
    string? LobbyId,
    string? ModId,
    string? ReporterUserId,
    DateTime StartedAtUtc)
{
    /// <summary>
    /// How many humans the recording should show. Same number the report uses as its
    /// participant count — before this they were computed separately (one raw, one filtered),
    /// so a member with a blank id could make the replay search look for a head count the
    /// report would never send.
    /// </summary>
    public int ExpectedHumans => Participants.Count;

    /// <summary>
    /// Take the facts of the match that is about to start.
    ///
    /// <para>The roster is captured at START, not at exit, on purpose: it can churn during a
    /// 30-minute game, and "who was in the room when the game began" is the closest the
    /// launcher can get to "who played" — AoE3 never tells us who actually entered the LAN
    /// game. Blank ids are dropped and duplicates collapsed here rather than at each use, and
    /// the result is sorted so the same room always produces the same list (the backend does
    /// not care about order; a deterministic one is simply testable).</para>
    /// </summary>
    public static MatchContext Capture(
        IEnumerable<string?>? roomMemberIds,
        string? lobbyId,
        string? modId,
        string? reporterUserId,
        bool isHost,
        DateTime startedAtUtc)
    {
        var participants = (roomMemberIds ?? Enumerable.Empty<string?>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new MatchContext(
            isHost,
            participants,
            string.IsNullOrWhiteSpace(lobbyId) ? null : lobbyId,
            string.IsNullOrWhiteSpace(modId) ? null : modId,
            string.IsNullOrWhiteSpace(reporterUserId) ? null : reporterUserId,
            startedAtUtc);
    }

    /// <summary>Length of the match, in whole seconds, never negative.</summary>
    public int DurationSeconds(DateTime endedAtUtc)
        => (int)Math.Max(0, (endedAtUtc - StartedAtUtc).TotalSeconds);

    /// <summary>
    /// Whether this match may be reported, and — when not — why.
    ///
    /// <para>The reason is returned rather than logged so this stays pure, and so the cause
    /// itself can be tested: "it skipped" and "it skipped for the right reason" are different
    /// claims, and the first one hid the incident this type was written for. The caller logs it
    /// verbatim behind the same prefix it always used, so existing debug logs stay comparable.</para>
    ///
    /// <para>The last two gates are anti-noise: the backend rejects fewer than 2 participants
    /// anyway, and a sub-3-minute session is almost certainly "opened AoE3, closed it" rather
    /// than a real game.</para>
    /// </summary>
    public (bool Ok, string Reason) CanReport(DateTime endedAtUtc, int minSeconds)
    {
        if (!IsHost)
            return (false, "not host of this room");

        if (string.IsNullOrEmpty(ReporterUserId))
            return (false, "no reporter id");

        if (string.IsNullOrEmpty(LobbyId) || string.IsNullOrEmpty(ModId))
            return (false, $"lobbyId='{LobbyId}' modId='{ModId}'");

        if (Participants.Count < 2)
            return (false,
                $"only {Participants.Count} participant(s), need >= 2 (multiplayer match, not a solo launch)");

        var duration = DurationSeconds(endedAtUtc);
        if (duration < minSeconds)
            return (false, $"duration {duration}s < {minSeconds}s");

        return (true, "ok");
    }

    /// <summary>
    /// A copy that will never report, for use when the room hands the host role to somebody else
    /// mid-match.
    ///
    /// <para>This guard only exists BECAUSE the context now survives a teardown. Before, a host
    /// who lost the socket lost the roster too and fell silent by accident; now they would report
    /// — and so would the player the room promoted, putting the same match into two people's
    /// ratings twice. It is deliberately one-way at the call site: losing the role silences us,
    /// gaining it does NOT arm us, since the previous host may be disconnected and may never have
    /// received the frame that would have silenced them. A false negative costs one match in the
    /// history; a false positive corrupts two people's rating.</para>
    /// </summary>
    public MatchContext WithHostLost() => this with { IsHost = false };
}
