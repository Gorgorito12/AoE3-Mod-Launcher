using System.Linq;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Which of the recordings on the player's own disk are games against PEOPLE, and how to name
/// one on screen.
///
/// <para><b>Why this reads the disk at all, when there is already a match history.</b> That
/// history comes from the lobby backend, and a row exists there only because the host reported
/// the match. A skirmish, a LAN game outside a room, or a match whose host closed the launcher
/// never appears. The recordings are the only record of those.</para>
///
/// <para><b>And why it says so little.</b> The end-of-match statistics screen — score,
/// resources, units, shipments — is written only into an AI's <c>.personality</c> memory file,
/// so for a game with no AI in it none of that exists anywhere: not in the recording, not in the
/// player's profile, not in the game log. What a recording does carry is who played, as whom,
/// with which explorer and which deck, and which slot lost.</para>
/// </summary>
public static class LocalMatchView
{
    /// <summary>Below this it is a game against the AI, which has its own section.</summary>
    public const int MinHumans = 2;

    public static int HumanCount(ReplayParserService.ReplayHeader? header) =>
        header?.Players.Count(p => p.IsHuman) ?? 0;

    /// <summary>
    /// A game with at least two people in it. An AI may also be present — that is still a game
    /// the player played against somebody — but a single human never is.
    /// </summary>
    public static bool IsHumanMatch(ReplayParserService.ReplayHeader? header) =>
        HumanCount(header) >= MinHumans;

    /// <summary>
    /// The map as the game names its file, made readable. Nothing is invented: an empty name
    /// stays empty and the caller decides what to show instead.
    /// </summary>
    public static string PrettyMap(string? mapName) =>
        string.IsNullOrWhiteSpace(mapName) ? "" : mapName.Replace('_', ' ').Trim();

    /// <summary>
    /// What a home city file name says about where the deck came from:
    /// <c>sp_Beijing_homecity.xml</c> becomes <c>Beijing</c>.
    ///
    /// <para>Anything that does not have that shape comes back empty rather than half-trimmed —
    /// a mod may name these differently, and a mangled word is worse than no word.</para>
    /// </summary>
    public static string HomeCityFrom(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "";

        var name = fileName.Trim();
        if (name.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 4);

        const string prefix = "sp_";
        const string suffix = "_homecity";
        if (!name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return "";
        if (!name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return "";

        var city = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
        return city.Trim();
    }
}
