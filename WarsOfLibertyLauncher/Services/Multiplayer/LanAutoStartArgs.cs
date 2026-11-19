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

    private static readonly IReadOnlyList<int> StandardOnly = new[] { Standard };
    private static readonly IReadOnlyList<int> WithoutTreaties =
        OfferedRules.Where(r => !IsTreaty(r)).ToArray();

    /// <summary>
    /// The entries a room may use. A competitive room plays Standard only, like the quick match and
    /// tournament rooms: one ladder, one way to play it. A mod that marks the treaties unsupported
    /// (<c>ModProfile.NoTreatyRules</c>) never offers them.
    /// </summary>
    public static IReadOnlyList<int> RulesOfferedFor(bool competitive, bool noTreaty) =>
        competitive ? StandardOnly : noTreaty ? WithoutTreaties : OfferedRules;

    /// <summary>The rules a room is created with: the choice when that room may use it, else Standard.</summary>
    public static int EffectiveRules(int chosen, bool competitive, bool noTreaty) =>
        RulesOfferedFor(competitive, noTreaty).Contains(chosen) ? chosen : Standard;

    /// <summary>
    /// The rules a room plays with, as every member must see them (server migration 0033): the
    /// SERVER's value whenever it sent one — it is the same for the creator, a joiner, a host
    /// promoted after the creator left and a rematch — else <paramref name="localChoice"/>, which
    /// is what an older server leaves the creator with and Standard for everybody else. Always
    /// through <see cref="EffectiveRules"/>, so a value this mod cannot use is never launched.
    /// </summary>
    public static int RoomRules(int? serverRules, int localChoice, bool competitive, bool noTreaty) =>
        EffectiveRules(serverRules ?? localChoice, competitive, noTreaty);

    /// <summary>
    /// Whether a room's rules deserve a chip: only when they were STATED and are not Standard. A
    /// chip on every room saying "Standard" says nothing, and an older server's silence is not a
    /// claim that the room is Standard.
    /// </summary>
    public static bool ShowsRulesChip(int? rules) => rules is { } r && r != Standard && OfferedRules.Contains(r);

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
    ///
    /// <para><paramref name="forceStandard"/> (a competitive room) passes Standard EXPLICITLY: the
    /// game's Game Rules dropdown opens on whatever the host last picked by hand, so leaving the
    /// argument out would not guarantee Standard. <paramref name="enabled"/> false (the player
    /// switched the automatic entry off in the room) gives nothing at all.</para>
    /// </summary>
    public static string For(bool isHost, string? hostRadminIp, int gameRules = Standard,
        bool forceStandard = false, bool enabled = true)
    {
        if (!enabled) return "";
        if (isHost)
        {
            if (forceStandard) return $" {HostToken} {RulesToken}={Standard}";
            return gameRules != Standard && IsOfferedRules(gameRules)
                ? $" {HostToken} {RulesToken}={gameRules}"
                : " " + HostToken;
        }
        var address = Normalize(hostRadminIp);
        return address == null ? "" : $" {JoinToken}=\"{address}\"";
    }

    /// <summary>
    /// Whether a cfg enables the key: a line whose first word is <c>lanAutoStart</c>, in any case, not
    /// a comment. The cfg reader's own rules: a first word starting with <c>#</c> is a comment, and so
    /// is anything after <c>//</c>.
    /// </summary>
    internal static bool CfgEnablesLanAutoStart(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw;
            var slash = line.IndexOf("//", System.StringComparison.Ordinal);
            if (slash >= 0) line = line[..slash];
            var first = line.Split(new[] { ' ', '\t', '=' }, System.StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (first != null && string.Equals(first, "lanAutoStart", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Which cfg of an install turns the key on, or null when none does: <c>Startup\*.cfg</c> (read
    /// by a things-new <c>UHC.dll</c>) or <c>engine-ext\engine-ext.cfg</c> (its <c>dinput8.dll</c>,
    /// beside another DLL). Read-only, best-effort: it only feeds a log line, so a player's report
    /// says whether the game could act on the argument at all.
    /// </summary>
    public static string? CfgEnablingLanAutoStart(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        try
        {
            var candidates = new List<string>();
            var startup = System.IO.Path.Combine(installPath, "Startup");
            if (System.IO.Directory.Exists(startup))
                candidates.AddRange(System.IO.Directory.GetFiles(startup, "*.cfg"));
            var engineExt = System.IO.Path.Combine(installPath, "engine-ext", "engine-ext.cfg");
            if (System.IO.File.Exists(engineExt)) candidates.Add(engineExt);
            foreach (var cfg in candidates)
                if (CfgEnablesLanAutoStart(System.IO.File.ReadLines(cfg)))
                    return System.IO.Path.GetRelativePath(installPath, cfg);
        }
        catch (System.Exception)
        {
        }
        return null;
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
