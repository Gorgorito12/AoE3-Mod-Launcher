using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Fabricated rooms, so the lobby's players panel can be LOOKED AT without two accounts,
/// two machines and a match.
///
/// <para>The same reasoning as <see cref="TournamentDemoData"/> and the toast preview: a
/// populated roster needs other people to exist before anybody can see whether it renders,
/// so "how does it look" is otherwise unanswerable until it is already in front of players.
/// This is what the handoff's own "show me a screenshot before you carry on" asks for.</para>
///
/// <para>UI-free and allocation-cheap on purpose — no WPF types here, so the samples can be
/// asserted on from a plain test thread.</para>
/// </summary>
public static class RoomDemoData
{
    /// <summary>One seat that somebody is sitting in.</summary>
    public sealed class Seat
    {
        public required string UserId { get; init; }
        public required string Login { get; init; }
        public bool Ready { get; init; }
        public bool IsHost { get; init; }

        /// <summary>Null means "not known", which the roster renders as ABSENCE — never as
        /// the 1500 the server hands a new player, which would read as earned.</summary>
        public double? Rating { get; init; }

        /// <summary>The two ladder places and the badge the player chose (design handoff 51).
        /// Null is "the server did not say" and draws no badge; 0 is Discovery.</summary>
        public int? LadderRank { get; init; }
        public int? LadderRankTeam { get; init; }
        public double? RatingTeam { get; init; }
        public string? BadgeMode { get; init; }

        /// <summary>Rated matches on each ladder and the deviation, so a player still being
        /// placed reads "1490? ELO" (rating v3). Null is "the server did not say".</summary>
        public int? GamesPlayed { get; init; }
        public int? GamesPlayedTeam { get; init; }
        public double? Rd { get; init; }

        /// <summary>The team picked in the room (1 or 2); null = no team yet.</summary>
        public int? Team { get; init; }

        /// <summary>Watches instead of playing (design 87k).</summary>
        public bool Observer { get; init; }

        /// <summary>No Radmin address reported yet.</summary>
        public bool NoVpn { get; init; }
    }

    /// <summary>A line of the sample chat; no author means a system line.</summary>
    public sealed record ChatSample(string? Who, string Text, DateTime? At = null);

    /// <summary>One room, as the window would draw it.</summary>
    public sealed class Sample
    {
        /// <summary>The name <c>--demo-room=&lt;name&gt;</c> selects it by.</summary>
        public required string Name { get; init; }

        public required string Code { get; init; }
        public required string RoomName { get; init; }

        /// <summary>Total seats. The roster draws this many rows: the occupied ones from
        /// <see cref="Players"/>, the rest as free seats.</summary>
        public required int Seats { get; init; }

        public required bool Competitive { get; init; }
        public required IReadOnlyList<Seat> Players { get; init; }

        /// <summary>Paint the room as a quick match still waiting for its rival (design 96).</summary>
        public bool QuickSearch { get; init; }

        /// <summary>With <see cref="QuickSearch"/>: Radmin is not ready.</summary>
        public bool QuickRadminMissing { get; init; }

        /// <summary>With <see cref="QuickSearch"/>: the time shown on the clock.</summary>
        public string QuickElapsed { get; init; } = "02:14";

        /// <summary>The server's win probability for the room (rating v3); null = none shown.</summary>
        public RoomOdds? Odds { get; init; }

        /// <summary>Which seat is "me" in the sample (rating v3's rooms). Null: nobody, as before.</summary>
        public string? ViewerId { get; init; }

        /// <summary>"My" standing for the sample — where "your record 9–2" comes from. Null: none.</summary>
        public EloSnapshot? ViewerStanding { get; init; }

        /// <summary>The mod the window wears.</summary>
        public string ModId { get; init; } = "wol";

        /// <summary>The room has a password.</summary>
        public bool Password { get; init; }

        /// <summary>Observer seats.</summary>
        public int SpectatorSlots { get; init; }

        /// <summary>Paint this stage of a match; null = whatever the room is.</summary>
        public RoomStage? Stage { get; init; }

        /// <summary>The finished match, for 87f, 87g and 87j.</summary>
        public MatchOutcomeView? Result { get; init; }

        /// <summary>Who left and made the viewer the host (87i).</summary>
        public string? HostLeft { get; init; }

        /// <summary>The viewer's Radmin is not ready (87h).</summary>
        public bool VpnMissing { get; init; }

        /// <summary>The rival a rematch invited (87l).</summary>
        public string? RematchRival { get; init; }

        public double? RematchRivalRating { get; init; }

        /// <summary>What the chat shows.</summary>
        public IReadOnlyList<ChatSample> Chat { get; init; } = Array.Empty<ChatSample>();

        public int FreeSeats => Math.Max(0, Seats - Players.Count);
    }

    /// <summary>
    /// THE ONE THE HANDOFF IS ABOUT: a competitive 1v1 with the host alone in it, so the
    /// panel has to draw one player and one free seat. This is the room in the screenshot
    /// that started the change — the row it cut in half and the seat it never drew.
    /// </summary>
    public static Sample OneVOne() => new()
    {
        Name = "1v1",
        Code = "5Q8HB6HM",
        RoomName = "Wars of Liberty · Ranked 1v1",
        Seats = 2,
        Competitive = true,
        Players = new[]
        {
            new Seat
            {
                UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383,
                LadderRank = 5, LadderRankTeam = 2, RatingTeam = 1612, BadgeMode = "team",
            },
        },
    };

    /// <summary>Half a team room: the free seats have to say "a player", not "an opponent".</summary>
    public static Sample TwoVTwo() => new()
    {
        Name = "2v2",
        Code = "K3M7TQ2P",
        RoomName = "Wars of Liberty · Ranked 2v2",
        Seats = 4,
        Competitive = true,
        Players = new[]
        {
            new Seat
            {
                UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383,
                LadderRank = 5, LadderRankTeam = 2, RatingTeam = 1612, BadgeMode = "1v1",
            },
            // No team match yet: a team room still shows the TEAM badge, as the double
            // Discovery shield, and never quietly falls back to the 1v1 one.
            new Seat
            {
                UserId = "demo-2", Login = "papillo", Ready = true, Rating = 1517,
                LadderRank = 3, LadderRankTeam = 0, BadgeMode = "highest",
            },
        },
    };

    /// <summary>
    /// Eight seats, all taken — the most rows AoE 3 can produce, and the case that proves
    /// the panel grew rather than started scrolling. One player has no rating, because a
    /// room where everybody happens to have one would hide that the line can be shorter.
    /// </summary>
    public static Sample Full() => new()
    {
        Name = "full",
        Code = "9WD4XR1B",
        RoomName = "Wars of Liberty · Casual 4v4",
        Seats = 8,
        Competitive = false,
        Players = new[]
        {
            // A casual room: each player's own choice decides (design handoff 51b), so the
            // modes are mixed on purpose - Highest picking each side, 1v1, Teams, and none.
            new Seat
            {
                UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383,
                LadderRank = 5, LadderRankTeam = 2, RatingTeam = 1612, BadgeMode = "highest",
            },
            new Seat
            {
                UserId = "demo-2", Login = "papillo", Ready = true, Rating = 1517,
                LadderRank = 3, LadderRankTeam = 1, RatingTeam = 1590, BadgeMode = "1v1",
            },
            new Seat
            {
                UserId = "demo-3", Login = "mandosrex", Ready = true, Rating = 1604,
                LadderRank = 1, LadderRankTeam = 4, RatingTeam = 1540, BadgeMode = "team",
            },
            new Seat
            {
                UserId = "demo-4", Login = "69metal69", Rating = 1298,
                LadderRank = 2, LadderRankTeam = 6, RatingTeam = 1480, BadgeMode = "highest",
            },
            new Seat { UserId = "demo-5", Login = "vonHabsburg", Ready = true, Rating = 1442, LadderRank = 7 },
            new Seat { UserId = "demo-6", Login = "zipa_dh", Rating = 1361 },
            new Seat { UserId = "demo-7", Login = "cuchillero", Ready = true, Rating = 1205 },
            new Seat { UserId = "demo-8", Login = "sin_elo", },
        },
    };

    /// <summary>
    /// The name nobody can fit. Discord allows far longer logins than a 352px column, and
    /// the row's whole layout rests on the name being the ONE thing that trims — so this
    /// sample exists to be looked at, not to be plausible.
    /// </summary>
    public static Sample LongName() => new()
    {
        Name = "long-name",
        Code = "QQ77ZZ03",
        RoomName = "Wars of Liberty · Ranked 1v1",
        Seats = 2,
        Competitive = true,
        Players = new[]
        {
            new Seat
            {
                UserId = "demo-1",
                Login = "un_nombre_de_discord_absurdamente_largo_para_la_columna",
                IsHost = true,
                Rating = 1383,
                // A medal after a name that cannot fit: the name trims to make room for it.
            },
        },
    };

    /// <summary>Every sample, in the order the scenario list shows them.</summary>
    public static IReadOnlyList<Sample> All() => new[]
    {
        OneVOne(), TwoVTwo(), Full(), LongName(), QuickSearching("quick", false, "02:14"),
        QuickSearching("quick-radmin", true, "02:14"), QuickSearching("quick-long", false, "25:41"),
        Design87("87a"), Design87("87b"), Design87("87c"), Design87("87d"), Design87("87e"), Design87("87f"),
        Design87("87g"), Design87("87h"), Design87("87i"), Design87("87j"), Design87("87k"), Design87("87l"), Design87("87live"),
    };

    private static Seat Gorgo(bool host = true, bool ready = false, double? rating = 1383, int? team = null, int? games = 40)
        => new() { UserId = "demo-gorgo", Login = "Gorgo", IsHost = host, Ready = ready, Rating = rating, GamesPlayed = games, RatingTeam = rating, GamesPlayedTeam = 20, Team = team };

    private static Seat Cuzco(bool ready = false, int? team = null, double? rating = null, int? games = 4)
        => new() { UserId = "demo-cuzco", Login = "Cuzco", Ready = ready, Rating = rating, RatingTeam = rating, GamesPlayed = games, GamesPlayedTeam = games, Team = team };

    private static Seat Malinche(bool ready = false, int? team = null)
        => new() { UserId = "demo-malinche", Login = "Malinche", Ready = ready, Rating = 1188, RatingTeam = 1188, GamesPlayedTeam = 20, Team = team };

    private static Seat Kaiser(bool host = false, bool ready = false, int? team = null, bool observer = false)
        => new() { UserId = "demo-kaiser", Login = "Kaiser", IsHost = host, Ready = ready, Rating = 1630, RatingTeam = 1630, GamesPlayedTeam = 20, Team = team, Observer = observer };

    private static DateTime Today(int hour, int minute) => DateTime.Today.AddHours(hour).AddMinutes(minute);

    private static MatchOutcomeView Outcome(MatchVerdict verdict, double before, double after,
        string? unrated = null, int? placementPlayed = null, int? streak = null, int? rank = null)
        => new(verdict, "wol", "ESOC Baja California", verdict == MatchVerdict.Win ? 24 * 60 : 31 * 60, 2,
            before, after, "Kaiser", unrated != null ? 1630 : verdict == MatchVerdict.Win ? 1611 : 1638, 12, 7, 60, UnratedReason: unrated,
            RecordingPath: "Record Game 12.age3Yrec",
            MyCiv: "Británicos", RivalCiv: "Franceses",
            PlacementPlayed: placementPlayed, PlacementRequired: placementPlayed == null ? null : 10,
            StreakCurrent: streak, EnteredRank: rank, LadderSize: rank == null ? null : 9,
            FormatLabelKey: "MpFormat1v1");

    /// <summary>
    /// The states of design 87. Same people and the same words as the prototype, because each one
    /// is held up against it.
    /// </summary>
    private static Sample Design87(string id)
    {
        const string night = "Partida de la noche";
        const string ranked = "WoL 1v1 · mapas ESOC";
        ChatSample[] Casual(int n)
        {
            var lines = new List<ChatSample> { new(null, "Abriste la sala.") };
            if (n >= 2)
            {
                lines.Add(new(null, "Cuzco entró en la sala."));
                lines.Add(new("Cuzco", "buenas, ¿mapas al azar?", Today(20, 44)));
            }
            if (n >= 3) lines.Add(new(null, "Malinche entró en la sala."));
            return lines.ToArray();
        }
        ChatSample[] Finished(string last) => new ChatSample[]
        {
            new(null, "Entraste en la sala de Kaiser."),
            new("Kaiser", "gl hf", Today(20, 51)),
            new(null, "Partida terminada. Kaiser envió el resultado."),
            new("Kaiser", last, Today(21, 17)),
        };
        Sample Room(string name, string room, int seats, bool comp, Seat[] players, string? viewer = "demo-gorgo") => new()
        {
            Name = name, Code = "7KQ2WX", RoomName = room, Seats = seats, Competitive = comp,
            Players = players, ViewerId = viewer,
        };
        Sample Over(string name, MatchOutcomeView result, Seat me, string last) => new()
        {
            Name = name, Code = "7KQ2WX", RoomName = ranked, Seats = 2, Competitive = true,
            Players = new[] { Kaiser(host: true, ready: true), me }, ViewerId = "demo-gorgo",
            Result = result, Stage = MultiplayerStage(result), Chat = Finished(last),
        };

        return id switch
        {
            "87a" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false, Password = true,
                Players = new[] { Gorgo() }, ViewerId = "demo-gorgo", Chat = Casual(1),
            },
            "87b" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = ranked, Seats = 2, Competitive = true,
                Players = new[] { Kaiser(host: true, ready: true), Gorgo(host: false) }, ViewerId = "demo-gorgo",
                Chat = new ChatSample[] { new(null, "Entraste en la sala de Kaiser."), new("Kaiser", "gl hf", Today(20, 51)) },
            },
            "87c" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false,
                Players = new[] { Gorgo(), Cuzco(), Malinche() }, ViewerId = "demo-gorgo", Chat = Casual(3),
            },
            "87d" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = "Equipos 2v2 · sábado", Seats = 4, Competitive = true,
                Players = new[]
                {
                    Gorgo(team: 1), Cuzco(ready: true, team: 1, rating: 1244, games: 20), Kaiser(ready: true, team: 2), Malinche(ready: true),
                },
                ViewerId = "demo-gorgo",
                Chat = new ChatSample[]
                {
                    new(null, "Abriste la sala."), new(null, "Cuzco eligió el equipo 1."),
                    new(null, "Kaiser eligió el equipo 2."), new("Kaiser", "Malinche, vente al 2", Today(21, 12)),
                },
            },
            "87e" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false,
                Players = new[] { Gorgo(), Cuzco(ready: true), Malinche(ready: true) }, ViewerId = "demo-gorgo",
                Stage = RoomStage.GameClosed,
                Chat = Casual(3).Concat(new ChatSample[] { new(null, "Partida en marcha."), new(null, "AoE3 se cerró en tu PC. La partida sigue.") }).ToArray(),
            },
            "87live" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false,
                Players = new[] { Gorgo(), Cuzco(ready: true), Malinche(ready: true) }, ViewerId = "demo-gorgo",
                Stage = RoomStage.InGame,
                Chat = Casual(3).Concat(new ChatSample[] { new(null, "Partida en marcha.") }).ToArray(),
            },
            "87f" => Over(id, Outcome(MatchVerdict.Win, 1383, 1402, streak: 3, rank: 5), Gorgo(host: false), "gg, bien jugado"),
            "87g" => Over(id, Outcome(MatchVerdict.Loss, 1400, 1388, placementPlayed: 5), Gorgo(host: false, rating: null, games: 5), "gg"),
            "87h" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false, VpnMissing = true,
                Players = new[] { Gorgo(), Cuzco(ready: true), Malinche(ready: true) }, ViewerId = "demo-gorgo", Chat = Casual(3),
            },
            "87i" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = "Partida de Kaiser", Seats = 4, Competitive = false, HostLeft = "Kaiser",
                Players = new[] { Gorgo(), Cuzco(), Malinche() }, ViewerId = "demo-gorgo",
                Chat = new ChatSample[]
                {
                    new(null, "Entraste en la sala de Kaiser."), new(null, "Cuzco entró en la sala."),
                    new(null, "Malinche entró en la sala."), new("Kaiser", "me tengo que ir, perdón", Today(20, 58)),
                    new(null, "Kaiser salió de la sala. Ahora hospedas tú."),
                },
            },
            "87j" => Over(id, Outcome(MatchVerdict.Win, 1383, 1383, unrated: "No cuenta para el ELO: fue una partida corta con una cuenta nueva."),
                Gorgo(host: false), "gg, bien jugado"),
            "87k" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = night, Seats = 4, Competitive = false, SpectatorSlots = 2,
                Players = new[] { Gorgo(), Cuzco(), Malinche(), Kaiser(ready: true, observer: true) }, ViewerId = "demo-gorgo", Chat = Casual(3),
            },
            "87l" => new()
            {
                Name = id, Code = "7KQ2WX", RoomName = ranked + " (revancha)", Seats = 2, Competitive = true,
                Players = new[] { Gorgo(rating: 1402) }, ViewerId = "demo-gorgo",
                RematchRival = "Kaiser", RematchRivalRating = 1611,
                Chat = new ChatSample[] { new(null, "Abriste la sala de la revancha."), new(null, "Invitaste a Kaiser.") },
            },
            _ => Room(id, night, 4, false, new[] { Gorgo() }),
        };
    }

    private static RoomStage MultiplayerStage(MatchOutcomeView result)
    {
        if (result.InPlacement) return RoomStage.ResultPlacement;
        return result.LooksUnrated ? RoomStage.ResultUnrated : RoomStage.ResultRated;
    }

    /// <summary>The room a quick match waits in: a host alone, the code in the header.</summary>
    private static Sample QuickSearching(string name, bool radminMissing, string elapsed)
    {
        var solo = OneVOne();
        return new Sample
        {
            Name = name,
            Code = "QV0M6C7Z",
            RoomName = "Quick match · Wars of Liberty 1v1",
            Seats = 2,
            Competitive = true,
            Players = solo.Players,
            QuickSearch = true,
            QuickRadminMissing = radminMissing,
            QuickElapsed = elapsed,
        };
    }

    /// <summary>
    /// Resolve <c>--demo-room=&lt;name&gt;</c>. An unknown or missing name falls back to the
    /// 1v1, which is the room the redesign is about.
    /// </summary>
    public static Sample ByName(string? scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario)) return OneVOne();
        var wanted = scenario.Trim();
        return All().FirstOrDefault(
            s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? OneVOne();
    }
}
