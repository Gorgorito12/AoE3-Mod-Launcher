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
}
