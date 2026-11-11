using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// The rooms list of design 86, with nothing behind it: seven rooms that between them reach every
/// action and every heading, the chat and the players beside them, and the five states of the
/// page (86a-86e). Pure and WPF-free, so what each sample is FOR is pinned by tests.
/// </summary>
internal static class RoomListDemoData
{
    /// <summary>The page states the prototype draws.</summary>
    internal enum Scenario
    {
        /// <summary>86a: rooms, the chat open.</summary>
        Rooms,
        /// <summary>86b: no rooms, the chat folded.</summary>
        Empty,
        /// <summary>86c: the list still loading.</summary>
        Loading,
        /// <summary>86d: the list could not be loaded.</summary>
        Error,
        /// <summary>86e: no connection to the server.</summary>
        Offline,
    }

    /// <summary><c>86a</c>..<c>86e</c>, or a word (<c>empty</c>, <c>loading</c>…). Anything else is 86a.</summary>
    internal static Scenario Parse(string? name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        return n switch
        {
            "86b" or "empty" => Scenario.Empty,
            "86c" or "loading" => Scenario.Loading,
            "86d" or "error" => Scenario.Error,
            "86e" or "offline" => Scenario.Offline,
            _ => Scenario.Rooms,
        };
    }

    /// <summary>The chat is folded in 86b, the laptop sample, and open everywhere else.</summary>
    internal static bool ChatFolded(Scenario s) => s == Scenario.Empty;

    /// <summary>The mods the sample treats as installed: Hundred Days is not, so its room offers
    /// "Install mod".</summary>
    internal static bool IsInstalled(string modId)
        => !string.Equals(modId, HundredDays, StringComparison.OrdinalIgnoreCase);

    /// <summary>The one room whose version differs from the sample's copy.</summary>
    internal static bool VersionDiffers(string lobbyId) => lobbyId == OtherVersionId;

    /// <summary>The sample copy's fingerprint, and the one every room but the odd one carries.</summary>
    internal const string MyHash = "DEMO-SAME-VERSION";

    /// <summary>The sample's own internet ping, said once above the list.</summary>
    internal const double PingMs = 62;

    /// <summary>
    /// The sample's ping to each host over Radmin, so every state of the PING cell can be seen
    /// without a server: a figure, slow, no answer (-1) and a host not on Radmin (null).
    /// </summary>
    internal static int? HostPingMs(string lobbyId) => lobbyId switch
    {
        "DEMO86K1" => 38,
        "DEMO86N2" => 162,
        "DEMO86R1" => 51,
        OtherVersionId => -1,
        "DEMO86X2" => 96,
        _ => null,
    };

    internal const string Wol = "wol";
    internal const string HundredDays = "hundred-days";
    internal const string Improvement = "improvement-mod";
    internal const string OtherVersionId = "DEMO86V5";

    /// <summary>Seven rooms: Join, Join (password), Install mod, See match, Check my copy, Watch/Full
    /// (observers), and Full.</summary>
    internal static List<LobbySummary> Rooms(DateTime now)
    {
        LobbySummary Room(string id, string title, string mod, bool competitive, int current, int max,
            string host, double? rating, int minutes, string status = "open", bool isPrivate = false,
            int spectatorSlots = 0, int spectators = 0, int? placementPlayed = null, int startedMinutes = 0)
        {
            return new LobbySummary
            {
                Id = id,
                Title = title,
                ModId = mod,
                ModCombinedHash = id == OtherVersionId ? "DEMO-OTHER-VERSION" : MyHash,
                Competitive = competitive,
                CurrentPlayers = current,
                MaxPlayers = max,
                SpectatorSlots = spectatorSlots,
                SpectatorsPresent = spectators,
                IsPrivate = isPrivate,
                Status = status,
                CreatedAt = now.AddMinutes(-minutes).ToString("yyyy-MM-dd HH:mm:ss"),
                StartedAt = status == "in_game" ? now.AddMinutes(-startedMinutes).ToString("yyyy-MM-dd HH:mm:ss") : null,
                Host = new LobbyHost
                {
                    Id = "demo-" + host,
                    DiscordUsername = host,
                    DisplayName = host,
                    Rating = rating,
                    RatingTeam = rating,
                    Rd = 80,
                    RdTeam = 80,
                    GamesPlayed = placementPlayed ?? 40,
                    GamesPlayedTeam = placementPlayed ?? 40,
                    InPlacement = placementPlayed != null,
                    InPlacementTeam = placementPlayed != null,
                    // The rooms row wears the host's badge again (2026-11-10): a place that follows
                    // the rating, none while placing or with no rating.
                    LadderRank = placementPlayed != null || rating is not double r ? null : r >= 1600 ? 4 : r >= 1400 ? 9 : 22,
                    LadderRankTeam = placementPlayed != null || rating is not double rt ? null : rt >= 1600 ? 6 : rt >= 1400 ? 12 : 25,
                },
            };
        }

        return new List<LobbySummary>
        {
            Room("DEMO86K1", "WoL 1v1 · mapas ESOC", Wol, true, 1, 2, "Kaiser", 1630, 4),
            Room("DEMO86P3", "Práctica 3v3 de la casa", Wol, false, 3, 6, "Rioplatense", 1288, 7, isPrivate: true),
            Room("DEMO86N2", "Napoleónicas 2v2", HundredDays, false, 2, 4, "Malinche", null, 3, placementPlayed: 3),
            Room("DEMO86R1", "Revancha", Wol, true, 2, 2, "Almirante", 1402, 20, status: "in_game", startedMinutes: 14),
            Room(OtherVersionId, "Mapas nuevos 1v1", Wol, true, 1, 2, "Bicorne", 1318, 2),
            Room("DEMO86X2", "Exhibición 2v2", Wol, false, 5, 6, "Aluclown", 1541, 9, spectatorSlots: 2, spectators: 1),
            Room("DEMO86T8", "Todos contra todos", Improvement, false, 8, 8, "Zanjón", 1190, 12),
        };
    }

    /// <summary>The community's latest matches, for the middle card under the list.</summary>
    internal static List<CommunityMatch> RecentMatches(DateTime now)
    {
        CommunityMatch M(int i, string a, string b, string map, int minutes, int hoursAgo) => new()
        {
            Id = "demo86m" + i,
            ModId = Wol,
            MapName = map,
            DurationSeconds = minutes * 60,
            Competitive = true,
            ReportedAt = now.AddHours(-hoursAgo).ToString("o"),
            Participants = new List<MatchHistoryParticipant>
            {
                new() { UserId = "demo-" + a, DisplayName = a, Result = 1 },
                new() { UserId = "demo-" + b, DisplayName = b, Result = 0 },
            },
        };
        return new List<CommunityMatch>
        {
            M(0, "Kaiser", "UnstoppableStreletsy", "ESOC_Florida", 24, 22),
            M(1, "Aluclown", "El Taita", "ESOC_Yukon", 27, 26),
            M(2, "Kaiser", "UnstoppableStreletsy", "ESOC_Hudson Bay", 31, 50),
        };
    }

    /// <summary>The players on line beside the list.</summary>
    internal static IReadOnlyList<(string Login, double Rating, string Status)> Online { get; } = new[]
    {
        ("Kaiser", 1630.0, "online"),
        ("Aluclown", 1541.0, "in_game"),
        ("Rioplatense", 1288.0, "online"),
        ("Malinche", 1210.0, "online"),
        ("Bicorne", 1318.0, "online"),
    };

    /// <summary>The global chat: who said what, how many minutes ago.</summary>
    internal static IReadOnlyList<(string Login, string Body, int MinutesAgo)> Chat { get; } = new[]
    {
        ("Kaiser", "¿Alguien para un 1v1 en mapas ESOC?", 9),
        ("Malinche", "En un rato abro un 2v2 de Hundred Days", 6),
        ("Bicorne", "Subí mapas nuevos, prueben", 3),
        ("Rioplatense", "La práctica de 3v3 tiene contraseña: pídanla por privado", 1),
    };
}
