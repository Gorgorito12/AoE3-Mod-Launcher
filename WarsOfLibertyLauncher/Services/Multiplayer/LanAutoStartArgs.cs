using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The launch arguments that take Age of Empires III from the main menu straight into the room's
/// LAN game: <c>+uhcLanHost</c> for the host, <c>uhcLanJoin="&lt;host's Radmin address&gt;"</c> for
/// everybody else, and for the host <c>uhcLanRules=N</c>, the Game Rules entry chosen when the room
/// was created.
///
/// <para>The game does not understand any of them on its own. They are read by the <c>lanAutoStart</c>
/// key of the things-new <c>UHC.dll</c>, which opens the LAN screen, picks the rules and presses Host,
/// or fills Direct IP and presses Connect. A mod opts in by shipping that DLL with the line
/// <c>lanAutoStart</c> in its cfg; <b>the launcher writes no file into the mod</b>. Every other
/// mod ignores the arguments and opens on its main menu as before — measured on Wars of Liberty's
/// own DLL and on the stock game — so the launcher passes them to every room without asking which
/// mod supports it.</para>
///
/// <para>A joiner with no usable host address gets nothing rather than a guess: a wrong address
/// would press Connect at somebody else's machine for three minutes. Pure, so the rules are pinned by
/// <c>LanAutoStartArgsTests</c>.</para>
/// </summary>
public static class LanAutoStartArgs
{
    public const string HostToken = "+uhcLanHost";
    public const string JoinToken = "uhcLanJoin";
    public const string RulesToken = "uhcLanRules";

    // The eight entries the game's own fill adds, in its order (FUN_008BA62E). The treaties are real
    // modes: Wars of Liberty's string table calls them "Not supported", which is its team's word,
    // not the engine's — measured, the stock game hosts all three tried.
    /// <summary>Standard: the game's own default, so it is never passed.</summary>
    public const int Standard = 0;
    public const int Classic = 1;
    public const int Treaty10 = 2;
    public const int Treaty20 = 3;
    public const int Treaty30 = 4;
    public const int Treaty40 = 5;
    public const int TreatyNoBlockade = 6;
    public const int KingOfTheHill = 7;

    /// <summary>The Game Rules entries the create-room dialog offers, in its order.</summary>
    public static readonly IReadOnlyList<int> OfferedRules = new[]
    {
        Standard, Classic, Treaty10, Treaty20, Treaty30, Treaty40, TreatyNoBlockade, KingOfTheHill,
    };

    public static bool IsOfferedRules(int rules) => OfferedRules.Contains(rules);

    public static bool IsTreaty(int rules) => rules is >= Treaty10 and <= TreatyNoBlockade;

    public static string RulesLabelKey(int rules) => rules switch
    {
        Classic => "MpCreateDialogRulesClassic",
        Treaty10 => "MpCreateDialogRulesTreaty10",
        Treaty20 => "MpCreateDialogRulesTreaty20",
        Treaty30 => "MpCreateDialogRulesTreaty30",
        Treaty40 => "MpCreateDialogRulesTreaty40",
        TreatyNoBlockade => "MpCreateDialogRulesTreatyNoBlockade",
        KingOfTheHill => "MpCreateDialogRulesKoth",
        _ => "MpCreateDialogRulesStandard",
    };

    /// <summary>One sentence on what the rules do, from the game's own rollover texts.</summary>
    public static string RulesDescriptionKey(int rules) => rules switch
    {
        Classic => "MpCreateDialogRulesClassicDesc",
        Treaty10 or Treaty20 or Treaty30 or Treaty40 => "MpCreateDialogRulesTreatyDesc",
        TreatyNoBlockade => "MpCreateDialogRulesTreatyNoBlockadeDesc",
        KingOfTheHill => "MpCreateDialogRulesKothDesc",
        _ => "MpCreateDialogRulesStandardDesc",
    };

    /// <summary>The treaty's length in minutes, 0 for every other entry.</summary>
    public static int TreatyMinutes(int rules) => rules switch
    {
        Treaty10 => 10,
        Treaty20 => 20,
        Treaty30 => 30,
        Treaty40 or TreatyNoBlockade => 40,
        _ => 0,
    };

    /// <summary>
    /// The arguments for this player, with a leading space, or "" when there is none to give.
    /// <paramref name="gameRules"/> is the host's choice; Standard, an entry not offered, or a
    /// joiner adds nothing.
    /// </summary>
    public static string For(bool isHost, string? hostRadminIp, int gameRules = Standard)
    {
        if (isHost)
        {
            return gameRules != Standard && IsOfferedRules(gameRules)
                ? $" {HostToken} {RulesToken}={gameRules}"
                : " " + HostToken;
        }
        var address = Normalize(hostRadminIp);
        return address == null ? "" : $" {JoinToken}=\"{address}\"";
    }

    /// <summary>
    /// The host's address as dotted IPv4, or null. Only a plain IPv4 passes: the server relays
    /// what the host's launcher reported, and this string goes onto a command line, so anything
    /// that could carry a quote or a space is refused.
    /// </summary>
    internal static string? Normalize(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var text = ip.Trim();
        if (text.Split('.').Length != 4) return null;
        if (!IPAddress.TryParse(text, out var parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            return null;
        if (IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.Broadcast))
            return null;
        return parsed.ToString();
    }
}
