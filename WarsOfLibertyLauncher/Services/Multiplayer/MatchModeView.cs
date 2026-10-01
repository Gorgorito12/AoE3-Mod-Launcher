namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// What KIND of room a finished match was played in — the one word a match row leads with.
///
/// <para>Pure and WPF-free, like <see cref="MatchOutcomeView"/> beside it, because the whole
/// rule is a refusal and a refusal is worth pinning.</para>
///
/// <para><b>This is not "did it count".</b> The two questions look alike and come off the same
/// row: <c>competitive</c> is what the ROOM was, decided when it was created and never again,
/// while <c>rated</c> / <c>unrated_reason</c> is whether the server scored the match. A
/// competitive match ends unrated whenever nobody could read a recording, which is most of
/// them — so collapsing the two would label a real competitive game "casual". The launcher
/// renders both, separately, and works out neither for itself.</para>
/// </summary>
public static class MatchModeView
{
    /// <summary>
    /// The string key for the mode word, or <b>null when there is nothing honest to say</b>.
    ///
    /// <para>Null is the case that matters. The flag is joined from the lobby, so it is absent
    /// for every match stored before the field existed and for any whose lobby row has gone —
    /// and "we don't know what kind of room this was" is not "casual". Printing CASUAL there
    /// would quietly relabel a chunk of history, and nothing on screen could contradict it.</para>
    /// </summary>
    public static string? LabelKeyFor(bool? competitive) => competitive switch
    {
        true => "MpMatchModeCompetitive",
        false => "MpMatchModeCasual",
        null => null,
    };

    /// <summary>
    /// The whole label a match row leads its second line with: the mode word and the format,
    /// "COMPETITIVE 2v2" (design handoff turn 40). The format is part of the LABEL now, never a
    /// second segment later in the line — it used to appear there only when somebody won, so
    /// the same kind of match read two different ways.
    ///
    /// <para>Each half can be missing on its own, and each is dropped rather than guessed: an
    /// unknown mode leaves the format alone (see <see cref="LabelKeyFor"/> for why it is never
    /// "casual"), an unknown format leaves the word alone, and with neither there is no label.
    /// </para>
    /// </summary>
    /// <param name="competitive">The room's flag, null when the match predates it.</param>
    /// <param name="format">From <see cref="MatchParticipantsView.FormatOf"/>, or null.</param>
    /// <param name="word">Resolves a string key; the caller passes <c>Strings.Get</c>.</param>
    public static string? Label(bool? competitive, string? format, System.Func<string, string> word)
    {
        var key = LabelKeyFor(competitive);
        var mode = key == null ? null : word(key);
        var fmt = string.IsNullOrWhiteSpace(format) ? null : format;
        if (mode == null) return fmt;
        return fmt == null ? mode : mode + " " + fmt;
    }
}
