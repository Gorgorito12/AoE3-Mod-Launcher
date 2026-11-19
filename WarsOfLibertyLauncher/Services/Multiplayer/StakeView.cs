using System;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>What the room's "what's at stake" block draws.</summary>
public enum StakeState
{
    /// <summary>Nothing: a casual room, a spectator, an empty seat, unfinished teams.</summary>
    None,
    /// <summary>"If you win +8 · If you lose −17", from the server's stake.</summary>
    Shown,
    /// <summary>An older server (no stake): the bar and the percentage, as before.</summary>
    OddsOnly,
    /// <summary>Another player is being placed: the figures would give his rating back.</summary>
    HiddenPlacement,
    /// <summary>Another player keeps his rating private.</summary>
    HiddenPrivate,
}

/// <summary>The anti-farm discount on one branch (2026-11-18: shown in the room, amber).</summary>
/// <param name="OnWin">True: MY win is reduced (my Nth win in a row against them). False: my loss
/// is reduced (they have beaten me N times running).</param>
public sealed record StakeNote(bool OnWin, int Percent, int Streak);

/// <summary>
/// The pre-match stake as the room draws it (2026-11-18): the SERVER's figures (src/elo/stake.ts,
/// the same function that rates the match), rounded with the result card's own rule —
/// <see cref="MatchOutcomeView.Delta"/> + <see cref="RatingDisplay.FormatDelta"/> — so −16.5 is
/// −17 before the match and after it. Pure; pinned by <c>StakeViewTests</c>.
///
/// <para><b>Second layer of privacy.</b> The server already withholds figures while another
/// player is placing or private. This repeats it from what the launcher itself knows about the
/// room, so figures that arrive anyway (an older server, a race) are never drawn.</para>
///
/// <para><b>A stale stake is never drawn</b>: figures need the room complete by the launcher's own
/// count (<paramref name="roomComplete"/>) — somebody who just left is gone from the figures too.</para>
/// </summary>
public sealed record StakeView(StakeState State, int? Gain, int? Loss, int? WinPercent, StakeNote? Note, bool Tournament)
{
    public static readonly StakeView Nothing = new(StakeState.None, null, null, null, null, false);

    /// <param name="stake">The server's stake for this viewer; null from an older server or before one arrived.</param>
    /// <param name="oddsPercent">The server's win probability for this viewer, if any (the older-server fallback).</param>
    /// <param name="competitive">Only a ranked room has a stake.</param>
    /// <param name="viewerPlays">A spectator has none.</param>
    /// <param name="roomComplete">Both seats (or both teams) are taken, by the launcher's own count.</param>
    /// <param name="otherPlacing">Another player of the match is being placed (launcher's knowledge).</param>
    /// <param name="otherPrivate">Another player keeps his rating private (launcher's knowledge).</param>
    public static StakeView From(RoomStake? stake, int? oddsPercent, bool competitive, bool viewerPlays,
        bool roomComplete, bool otherPlacing, bool otherPrivate)
    {
        if (!competitive || !viewerPlays || !roomComplete) return Nothing;

        if (stake?.HiddenReason == "private" || otherPrivate) return Hidden(StakeState.HiddenPrivate, stake);
        if (stake?.HiddenReason == "placement" || otherPlacing) return Hidden(StakeState.HiddenPlacement, stake);

        if (stake is { Before: double before, WinAfter: double winAfter, LossAfter: double lossAfter })
        {
            var gain = MatchOutcomeView.Delta(before, winAfter);
            var loss = MatchOutcomeView.Delta(before, lossAfter);
            StakeNote? note = null;
            if (stake.WinFactor is double wf && wf < 1)
                note = new StakeNote(true, (int)Math.Round(wf * 100), stake.WinStreak ?? 0);
            else if (stake.LossFactor is double lf && lf < 1)
                note = new StakeNote(false, (int)Math.Round(lf * 100), stake.LossStreak ?? 0);
            return new StakeView(StakeState.Shown, gain, loss, stake.WinPct ?? oddsPercent, note, stake.Tournament == true);
        }

        return oddsPercent is int pct
            ? new StakeView(StakeState.OddsOnly, null, null, pct, null, false)
            : Nothing;
    }

    private static StakeView Hidden(StakeState state, RoomStake? stake)
        => new(state, null, null, null, null, stake?.Tournament == true);

    /// <summary>"+8" / "−17" as the result card prints them.</summary>
    public string? GainText => RatingDisplay.FormatDelta(Gain);
    public string? LossText => RatingDisplay.FormatDelta(Loss);

    /// <summary>Whether a newer frame may replace the one held: an older <c>seq</c> never does.</summary>
    public static bool IsNewer(RoomStake? held, RoomStake incoming)
        => held?.Seq is not int old || incoming.Seq is not int seq || seq >= old;
}
