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
    /// A map name split into the NAME and the pack it came from: <c>ESOC_Fertile Crescent</c>
    /// becomes <c>Fertile Crescent</c> plus <c>ESOC</c>.
    ///
    /// <para>The launcher already refuses to print an internal identifier where a player can
    /// see it - that rule was written for civilizations, which are resolved against the mod's
    /// own text table. Maps have no such table anywhere, and <b>none is invented here</b>: all
    /// this does is separate a prefix that is already in the string. The pack is real
    /// information and worth keeping; it is just not part of the map's name.</para>
    ///
    /// <para>The prefix is only taken when it looks like a pack tag: a short run of UPPERCASE
    /// letters and digits with a real name after it. The case is what carries the distinction
    /// and it is the whole rule - <c>ESOC</c>, <c>KOTH</c> and <c>WOL</c> are tags, while
    /// <c>Great_Plains</c> and <c>Painted_Desert</c> are two-word map names joined the way the
    /// engine joins them, and a length test alone happily eats the first word of both. A wrong
    /// guess here is a map nobody recognises, which is worse than the identifier it
    /// replaced.</para>
    /// </summary>
    public static (string Name, string? Pack) MapLabel(string? mapName)
    {
        string pretty = PrettyMap(mapName);
        if (string.IsNullOrEmpty(pretty)) return ("", null);

        string raw = mapName!.Trim();
        int cut = raw.IndexOf('_');
        if (cut <= 0 || cut >= raw.Length - 1) return (pretty, null);

        string prefix = raw[..cut];
        string rest = raw[(cut + 1)..].Trim();
        if (rest.Length == 0) return (pretty, null);

        // Short, and UPPERCASE. Without the case test "Great" reads as a pack and the map
        // becomes "Plains".
        if (prefix.Length is < 2 or > 8) return (pretty, null);
        bool anyLetter = false;
        foreach (char c in prefix)
        {
            if (!char.IsLetterOrDigit(c)) return (pretty, null);
            if (char.IsLower(c)) return (pretty, null);
            if (char.IsLetter(c)) anyLetter = true;
        }
        if (!anyLetter) return (pretty, null);

        return (PrettyMap(rest), prefix);
    }

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
