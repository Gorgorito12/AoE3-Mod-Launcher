using System;
using System.Collections.Generic;
using System.Linq;

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
    }

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
            new Seat { UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383 },
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
            new Seat { UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383 },
            new Seat { UserId = "demo-2", Login = "papillo", Ready = true, Rating = 1517 },
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
            new Seat { UserId = "demo-1", Login = "gorgorito_12", IsHost = true, Rating = 1383 },
            new Seat { UserId = "demo-2", Login = "papillo", Ready = true, Rating = 1517 },
            new Seat { UserId = "demo-3", Login = "mandosrex", Ready = true, Rating = 1604 },
            new Seat { UserId = "demo-4", Login = "69metal69", Rating = 1298 },
            new Seat { UserId = "demo-5", Login = "vonHabsburg", Ready = true, Rating = 1442 },
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
            },
        },
    };

    /// <summary>Every sample, in the order the scenario list shows them.</summary>
    public static IReadOnlyList<Sample> All() => new[]
    {
        OneVOne(), TwoVTwo(), Full(), LongName(),
    };

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
