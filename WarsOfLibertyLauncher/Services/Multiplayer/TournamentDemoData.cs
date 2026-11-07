using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>
/// Seven fabricated tournaments, so somebody can look at a bracket without running one — and,
/// through <see cref="TournamentSimulator"/>, play one from its first match to a champion.
///
/// <para>The problem this exists for is the same one <c>PreviewNotificationToasts</c> exists for,
/// stated in its own doc comment: a bracket with sixteen entrants and three played rounds only
/// happens after sixteen people and fifteen games, which makes "how does it look" almost
/// impossible to answer by trying. This paints the real thing — the real cards, the real tokens,
/// the real layout — from data that never touched a server.</para>
///
/// <para>Pure and WPF-free, like <c>BracketLayout</c> beside it, so its shape can be pinned by
/// tests. That matters more than it sounds: the failure mode of a fixture like this is not
/// breaking, it is quietly decaying into four identical brackets that no longer show what they
/// claim to. <c>TournamentDemoDataTests</c> is what stops that happening unnoticed.</para>
///
/// <para><b>The fake "me".</b> Everything is written from the point of view of
/// <see cref="MeUserId"/>, because half the states a card can be in depend on whether the match
/// is yours. The caller passes this same id as <c>me</c> when rendering.</para>
/// </summary>
internal static class TournamentDemoData
{
    /// <summary>The viewer, in every sample. Not a real id and deliberately shaped like one.</summary>
    internal const string MeUserId = "demo-me";

    internal const string RunningId = "DEMOCUP1";
    internal const string TeamsId = "DEMOCUP2";
    internal const string RegistrationId = "DEMOCUP3";
    internal const string FinishedId = "DEMOCUP4";
    internal const string MyRoomId = "DEMOCUP5";
    internal const string WaitingId = "DEMOCUP6";
    internal const string OrganiserId = "DEMOCUP7";
    internal const string Big32Id = "DEMOCUP8";

    /// <summary>
    /// Entrant names.
    ///
    /// <para>NOT localised, unlike the tournament names below, and that is on purpose: a player
    /// name is a proper noun and nothing else in the launcher translates one. Invented so they
    /// cannot be mistaken for anybody real, and varied in length so the card's text trimming gets
    /// exercised rather than flattered.</para>
    /// </summary>
    private static readonly string[] SoloNames =
    {
        "Gorgo", "Aluclown", "Mariscal", "Rioplatense", "TercioViejo", "Vandalia",
        "ElNorte", "Sombrerete", "Cuzco", "Malinche", "Bicorne", "Zanjón",
        "PatagonBravo", "Almirante", "Chasqui", "Filibustero",
    };

    private static readonly string[] TeamNames =
    {
        "Los Andes", "Compañía de Indias", "Guardia Vieja", "Hermandad del Sur",
    };

    /// <summary>The same invented player names, for the preview's server to sign people up with.</summary>
    internal static IReadOnlyList<string> PlayerNamePool => SoloNames;

    /// <summary>The same invented team names, for the same reason.</summary>
    internal static IReadOnlyList<string> TeamNamePool => TeamNames;

    // ---------------------------------------------------------------- the list

    /// <summary>Every scenario, as the subtab's list shows them. The list IS the picker.</summary>
    internal static TournamentListResponse List() => new()
    {
        Tournaments = new List<TournamentSummary>
        {
            Summary(Big32()),
            Summary(Running()),
            Summary(Teams()),
            Summary(MyRoom()),
            Summary(Waiting()),
            Summary(Organiser()),
            Summary(Registration()),
            Summary(Finished()),
        },
        Drafts = new List<TournamentSummary>(),
    };

    /// <summary>Find one by id, so clicking a card in the demo list works with no network.</summary>
    internal static TournamentDetail? ById(string? id) => id switch
    {
        Big32Id => Big32(),
        RunningId => Running(),
        TeamsId => Teams(),
        MyRoomId => MyRoom(),
        WaitingId => Waiting(),
        OrganiserId => Organiser(),
        RegistrationId => Registration(),
        FinishedId => Finished(),
        _ => null,
    };

    /// <summary>
    /// The string-table key of a sample's name, or null for anything that is not a sample.
    ///
    /// <para>The preview's server keeps the tournaments it was handed, names and all, and a
    /// language switch afterwards would leave every sample named in the old language. It asks
    /// for the name again through this, as a fresh fixture would.</para>
    /// </summary>
    internal static string? NameKeyOf(string? id) => id switch
    {
        Big32Id => "MpTournamentDemoBig32Name",
        RunningId => "MpTournamentDemoRunningName",
        TeamsId => "MpTournamentDemoTeamsName",
        RegistrationId => "MpTournamentDemoRegistrationName",
        FinishedId => "MpTournamentDemoFinishedName",
        MyRoomId => "MpTournamentDemoMyRoomName",
        WaitingId => "MpTournamentDemoWaitingName",
        OrganiserId => "MpTournamentDemoOrganiserName",
        _ => null,
    };

    /// <summary>
    /// Write the next-round links the server stores on every match.
    ///
    /// <para>The renderer never reads them — it derives the same thing from round and position —
    /// but the preview's server walks them to move a winner on, exactly as the real one does. A
    /// sample without them could be looked at and not played.</para>
    /// </summary>
    private static TournamentDetail Linked(TournamentDetail t)
    {
        if (t.Matches is { Count: > 0 } matches && t.RoundsTotal is int rounds)
        {
            TournamentRules.EnsureLinks(matches, rounds);
        }
        return t;
    }

    private static TournamentSummary Summary(TournamentDetail t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        ModId = t.ModId,
        OwnerUserId = t.OwnerUserId,
        Format = t.Format,
        TeamSource = t.TeamSource,
        EntryMode = t.EntryMode,
        Status = t.Status,
        Capacity = t.Capacity,
        ConfirmedCount = t.ConfirmedCount,
        EntrantCount = t.Entrants?.Count ?? 0,
        // The list row says "2 requests" from this, and the approval scenario is the only
        // one that has any. Counted here rather than written, so it cannot disagree with the
        // entrant list the detail pane draws from.
        PendingCount = t.Entrants?.Count(e => e.Status == "pending") ?? 0,
    };

    // ---------------------------------------------------------------- scenarios

    /// <summary>
    /// The big one: sixteen entrants, four rounds, two rounds played.
    ///
    /// <para>Built so that every busy card state appears at once — a bye, decided matches, MY
    /// playable match, somebody else's match with a room already open, and plenty that are none
    /// of my business. <c>TournamentDemoDataTests</c> asserts that rather than trusting it.</para>
    ///
    /// <para>What it deliberately cannot also show is a match of mine waiting for an opponent:
    /// in single elimination, waiting means you have already won your last one, so one viewer
    /// can have a playable match or a waiting one and never both. The team sample carries the
    /// other side of that.</para>
    /// </summary>
    internal static TournamentDetail Running()
    {
        // Fifteen real entrants in a sixteen-slot bracket, so slot 16 is empty and the top seed
        // walks through — which is how a real bye arises and the only way to see one drawn.
        var entrants = new List<TournamentEntrant>();
        for (int i = 0; i < 15; i++)
        {
            entrants.Add(new TournamentEntrant
            {
                Id = $"r{i + 1}",
                Kind = "solo",
                DisplayName = SoloNames[i],
                CaptainUserId = $"u{i + 1}",
                Seed = i + 1,
                Status = "confirmed",
                MemberIds = new List<string> { $"u{i + 1}" },
            });
        }
        // I am seed 4, so my matches sit in the middle of the bracket rather than at an edge.
        entrants[3].MemberIds = new List<string> { MeUserId };
        entrants[3].CaptainUserId = MeUserId;
        entrants[3].DisplayName = SoloNames[3];

        var matches = new List<TournamentMatch>();

        // Round 1 — eight pairings in the standard seed order, with seed 1 unopposed.
        var pairs = new (int a, int b)[] { (1, 16), (8, 9), (4, 13), (5, 12), (2, 15), (7, 10), (3, 14), (6, 11) };
        for (int p = 0; p < pairs.Length; p++)
        {
            var (a, b) = pairs[p];
            string? e1 = SeedId(a);
            string? e2 = SeedId(b);
            bool bye = e2 == null;
            matches.Add(new TournamentMatch
            {
                Id = $"r1m{p}",
                Round = 1,
                Position = p,
                Entrant1Id = e1,
                Entrant2Id = e2,
                Status = bye ? "bye" : "done",
                Outcome = bye ? "bye" : "played",
                // The higher seed wins every first-round game, which keeps the later rounds
                // predictable enough to read at a glance.
                WinnerEntrantId = e1,
            });
        }

        // Round 2 — four matches. Mine is playable; one has a room the other side opened; one
        // is somebody else's ordinary pending match; one is already decided.
        matches.Add(new TournamentMatch
        {
            Id = "r2m0", Round = 2, Position = 0,
            Entrant1Id = SeedId(1), Entrant2Id = SeedId(8),
            Status = "done", Outcome = "played", WinnerEntrantId = SeedId(1),
        });
        matches.Add(new TournamentMatch
        {
            // MY match. Both sides known, no room yet: the "Jugar mi partida" card.
            Id = "r2m1", Round = 2, Position = 1,
            Entrant1Id = SeedId(4), Entrant2Id = SeedId(5),
            Status = "pending",
        });
        matches.Add(new TournamentMatch
        {
            // Somebody else already opened the room for theirs — the "in progress" card.
            Id = "r2m2", Round = 2, Position = 2,
            Entrant1Id = SeedId(2), Entrant2Id = SeedId(7),
            Status = "pending",
            Lobby = new TournamentMatchLobby { Id = "DEMOROOM", HostUserId = "u2", Status = "open" },
        });
        matches.Add(new TournamentMatch
        {
            Id = "r2m3", Round = 2, Position = 3,
            Entrant1Id = SeedId(3), Entrant2Id = SeedId(6),
            Status = "done", Outcome = "played", WinnerEntrantId = SeedId(3),
        });

        // Round 3 — one side known on each, so both show "waiting for an opponent".
        matches.Add(new TournamentMatch
        {
            Id = "r3m0", Round = 3, Position = 0, Entrant1Id = SeedId(1), Status = "pending",
        });
        matches.Add(new TournamentMatch
        {
            Id = "r3m1", Round = 3, Position = 1, Entrant2Id = SeedId(3), Status = "pending",
        });

        // The final, empty.
        matches.Add(new TournamentMatch { Id = "r4m0", Round = 4, Position = 0, Status = "pending" });

        return Linked(new TournamentDetail
        {
            Id = RunningId,
            Name = Strings.Get("MpTournamentDemoRunningName"),
            // Null on purpose: OpenTournamentMatchAsync early-returns without a mod id, which is
            // a second lock on the door after the demo guard itself.
            ModId = null,
            OwnerUserId = "u1",          // NOT me: this one shows the entrant's view, not the owner's
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "running",
            Capacity = 16,
            ConfirmedCount = 15,
            BracketSize = 16,
            RoundsTotal = 4,             // or the labels read "ROUND 4" instead of "FINAL"
            Entrants = entrants,
            Matches = matches,
        });

        string? SeedId(int seed) => seed <= 15 ? $"r{seed}" : null;
    }

    /// <summary>
    /// A draw of thirty-two with twenty-nine entered, the size where the bracket has to scroll both
    /// ways: three byes, a walkover and a disqualification in the first round, and in the second my
    /// match, two being played and some still to come.
    /// </summary>
    internal static TournamentDetail Big32()
    {
        var names = new[]
        {
            "Mariscal", "Aluclown", "Rioplatense", "TercioViejo", "Vandalia", "ElNorte", "Sombrerete", "Cuzco",
            "Malinche", "Gorgo", "Bicorne", "Zanjón", "PatagonBravo", "Almirante", "Chasqui", "Filibustero",
            "Kaiser", "Geaf_Argento", "UnstoppableStreletsy", "El Taita", "NathanR06", "alexari2040", "Jenízaro",
            "Granadero", "Corsario", "Lancero", "Mosquetero", "Húsar", "Coracero",
        };
        var entrants = names.Select((name, i) => new TournamentEntrant
        {
            Id = $"b{i + 1}", Kind = "solo", DisplayName = name, CaptainUserId = i == 9 ? MeUserId : $"b-u{i + 1}",
            Seed = i + 1, Status = "confirmed",
            MemberIds = new List<string> { i == 9 ? MeUserId : $"b-u{i + 1}" },
        }).ToList();

        static int[] Order(int n) => n == 1 ? new[] { 1 } : Order(n / 2).SelectMany(x => new[] { x, n + 1 - x }).ToArray();
        static int WinnerOf(int a, int b) => (a * 7 + b * 3) % 5 == 0 ? Math.Max(a, b) : Math.Min(a, b);
        const int entered = 29;

        var order = Order(32);
        var matches = new List<TournamentMatch>();
        var winners = new int[16];
        for (int p = 0; p < 16; p++)
        {
            int a = order[2 * p], b = order[2 * p + 1];
            bool bye = a > entered || b > entered;
            int win = bye ? Math.Min(a, b) : p is 3 or 6 ? Math.Min(a, b) : WinnerOf(a, b);
            winners[p] = win;
            matches.Add(new TournamentMatch
            {
                Id = $"b1m{p}", Round = 1, Position = p,
                Entrant1Id = a <= entered ? $"b{a}" : null, Entrant2Id = b <= entered ? $"b{b}" : null,
                Status = bye ? "bye" : "done",
                Outcome = bye ? "bye" : p == 3 ? "walkover" : p == 6 ? "dq" : "played",
                WinnerEntrantId = $"b{win}",
            });
        }

        var second = new int[8];
        for (int p = 0; p < 8; p++)
        {
            int a = winners[2 * p], b = winners[2 * p + 1];
            bool done = p is 0 or 1 or 4;
            second[p] = done ? WinnerOf(a, b) : 0;
            matches.Add(new TournamentMatch
            {
                Id = $"b2m{p}", Round = 2, Position = p, Entrant1Id = $"b{a}", Entrant2Id = $"b{b}",
                Status = done ? "done" : "pending",
                Outcome = done ? "played" : null,
                WinnerEntrantId = done ? $"b{second[p]}" : null,
                Lobby = p is 2 or 6 ? new TournamentMatchLobby { Id = $"DEMOB{p}", HostUserId = $"b-u{a}", Status = "open" } : null,
            });
        }
        for (int p = 0; p < 4; p++)
        {
            int a = second[2 * p], b = second[2 * p + 1];
            matches.Add(new TournamentMatch
            {
                Id = $"b3m{p}", Round = 3, Position = p,
                Entrant1Id = a > 0 ? $"b{a}" : null, Entrant2Id = b > 0 ? $"b{b}" : null, Status = "pending",
            });
        }
        matches.Add(new TournamentMatch { Id = "b4m0", Round = 4, Position = 0, Status = "pending" });
        matches.Add(new TournamentMatch { Id = "b4m1", Round = 4, Position = 1, Status = "pending" });
        matches.Add(new TournamentMatch { Id = "b5m0", Round = 5, Position = 0, Status = "pending" });

        return Linked(new TournamentDetail
        {
            Id = Big32Id,
            Name = Strings.Get("MpTournamentDemoBig32Name"),
            ModId = "wol",
            OwnerUserId = "b-u17",
            OwnerName = "Kaiser",
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "running",
            Capacity = 32,
            ConfirmedCount = entered,
            BracketSize = 32,
            RoundsTotal = 5,
            Entrants = entrants,
            Matches = matches,
        });
    }

    /// <summary>
    /// A 3v3 in progress, for the one thing only a team tournament shows.
    ///
    /// <para>Team names are long and the card is 220 px wide, so this is where the trimming gets
    /// tested. It is also the only place the sides warning appears — the sentence telling people
    /// to pick these same teams inside AoE3, which matters more than it looks: get the sides wrong
    /// and the game does not rate AND the bracket does not move.</para>
    /// </summary>
    internal static TournamentDetail Teams()
    {
        var entrants = new List<TournamentEntrant>();
        for (int i = 0; i < 4; i++)
        {
            entrants.Add(TeamEntrant(i, captainIsMe: i == 0));
        }

        return Linked(new TournamentDetail
        {
            Id = TeamsId,
            Name = Strings.Get("MpTournamentDemoTeamsName"),
            ModId = null,
            OwnerUserId = "c4",
            Format = "3v3",
            TeamSource = "registered",
            EntryMode = "open",
            Status = "running",
            Capacity = 4,
            ConfirmedCount = 4,
            BracketSize = 4,
            RoundsTotal = 2,
            Entrants = entrants,
            Matches = new List<TournamentMatch>
            {
                new()
                {
                    // My team's match, with the room already opened by the OTHER side. That
                    // makes this the "join" card — and, because the sides warning renders on
                    // any card you can act on, it is where that sentence gets looked at.
                    Id = "tm0", Round = 1, Position = 0,
                    Entrant1Id = "t1", Entrant2Id = "t4", Status = "pending",
                    Lobby = new TournamentMatchLobby { Id = "DEMOTEAM", HostUserId = "c4", Status = "open" },
                },
                new()
                {
                    Id = "tm1", Round = 1, Position = 1,
                    Entrant1Id = "t2", Entrant2Id = "t3",
                    Status = "done", Outcome = "played", WinnerEntrantId = "t2",
                },
                new() { Id = "tm2", Round = 2, Position = 0, Entrant2Id = "t2", Status = "pending" },
            },
        });
    }

    /// <summary>
    /// Registration still open, and I own it.
    ///
    /// <para>No bracket at all, so the detail pane falls through to the entrant list — the other
    /// half of the screen, which the three bracket scenarios never show. Owned by the fake "me"
    /// so the owner's row of buttons is visible; in the preview they act on the simulated server
    /// (<see cref="TournamentSimulator"/>), never on a real one — the contract
    /// <c>PreviewNotificationToasts</c> set for a preview whose buttons cannot really act.</para>
    /// </summary>
    internal static TournamentDetail Registration()
    {
        var entrants = new List<TournamentEntrant>
        {
            // Four seeded and ONE not. All five unseeded was the first version and it taught
            // nothing: a column where every row is amber reads as the normal state of the
            // screen rather than as the one row holding the tournament up.
            Entrant("g1", SoloNames[0], 1, "confirmed"),
            Entrant("g2", SoloNames[1], 2, "confirmed"),
            Entrant("g3", SoloNames[2], 3, "confirmed"),
            Entrant("g4", SoloNames[4], 4, "confirmed"),
            Entrant("g5", SoloNames[5], 0, "confirmed"),
            // Past the capacity: the waiting list, in the order they asked.
            Entrant("g6", SoloNames[6], 0, "waitlist"),
            Entrant("g7", SoloNames[7], 0, "waitlist"),
            // Approval mode: these two are asking, and the owner sees Accept / Reject on them.
            Entrant("g8", SoloNames[8], 0, "pending"),
            Entrant("g9", SoloNames[9], 0, "pending"),
            // Somebody who changed their mind - dimmed, and it frees their place.
            Entrant("g10", SoloNames[10], 0, "withdrawn"),
        };
        // I am in it as well as owning it, so the row highlight, the TU tag and the way out
        // are all on screen. Owning a tournament you are not playing in hides three things
        // at once.
        entrants[0].MemberIds = new List<string> { MeUserId };
        entrants[0].CaptainUserId = MeUserId;

        return new TournamentDetail
        {
            Id = RegistrationId,
            Name = Strings.Get("MpTournamentDemoRegistrationName"),
            ModId = null,
            OwnerUserId = MeUserId,      // mine, so the owner strip shows
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "approval",      // so pending applications make sense
            Status = "registration",
            Capacity = 8,
            ConfirmedCount = 5,
            Entrants = entrants,
            Matches = new List<TournamentMatch>(),
        };
    }

    /// <summary>Over, with a champion — the one state that renders the winner line.</summary>
    internal static TournamentDetail Finished()
    {
        var entrants = new List<TournamentEntrant>
        {
            Entrant("f1", SoloNames[0], 1, "confirmed"),
            Entrant("f2", SoloNames[1], 2, "confirmed"),
            Entrant("f3", SoloNames[2], 3, "confirmed"),
            Entrant("f4", SoloNames[3], 4, "confirmed"),
        };
        // I lost the final, which is a more useful thing to look at than winning it: it is the
        // state where the loser's dimmed styling has to still be readable.
        entrants[1].MemberIds = new List<string> { MeUserId };
        entrants[1].CaptainUserId = MeUserId;

        return Linked(new TournamentDetail
        {
            Id = FinishedId,
            Name = Strings.Get("MpTournamentDemoFinishedName"),
            ModId = null,
            OwnerUserId = "u9",
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "finished",
            Capacity = 4,
            ConfirmedCount = 4,
            BracketSize = 4,
            RoundsTotal = 2,
            WinnerEntrantId = "f1",
            Entrants = entrants,
            Matches = new List<TournamentMatch>
            {
                new()
                {
                    Id = "fm0", Round = 1, Position = 0, Entrant1Id = "f1", Entrant2Id = "f4",
                    Status = "done", Outcome = "played", WinnerEntrantId = "f1",
                },
                new()
                {
                    // Decided without a game: the outcome tag is the only thing that says so.
                    Id = "fm1", Round = 1, Position = 1, Entrant1Id = "f2", Entrant2Id = "f3",
                    Status = "done", Outcome = "walkover", WinnerEntrantId = "f2",
                },
                new()
                {
                    Id = "fm2", Round = 2, Position = 0, Entrant1Id = "f1", Entrant2Id = "f2",
                    Status = "done", Outcome = "played", WinnerEntrantId = "f1",
                },
            },
        });
    }

    /// <summary>
    /// One team entrant, with the three people it registered.
    ///
    /// <para>Both shapes are filled: <c>MemberIds</c>, which is what decides whether a match
    /// is mine, and <c>Members</c>, which is what the card draws. They are built from ONE
    /// list here so the preview cannot show a line-up that disagrees with the match it
    /// belongs to - a fixture where those two drifted apart would render a team the viewer
    /// can see themselves in on a card that says the match is somebody else's.</para>
    /// </summary>
    private static TournamentEntrant TeamEntrant(int index, bool captainIsMe)
    {
        // Three real-looking names per team, taken in blocks so no name appears twice across
        // the tournament - a duplicate would make the frozen-roster rule impossible to read.
        var names = new[]
        {
            SoloNames[index * 3 % SoloNames.Length],
            SoloNames[(index * 3 + 1) % SoloNames.Length],
            SoloNames[(index * 3 + 2) % SoloNames.Length],
        };
        string captainId = captainIsMe ? MeUserId : $"c{index + 1}";
        var ids = new[] { captainId, $"m{index + 1}a", $"m{index + 1}b" };

        return new TournamentEntrant
        {
            Id = $"t{index + 1}",
            Kind = "team",
            DisplayName = TeamNames[index],
            CaptainUserId = captainId,
            Seed = index + 1,
            Status = "confirmed",
            MemberIds = ids.ToList(),
            Members = ids.Select((id, i) => new TournamentEntrantMember
            {
                UserId = id,
                DisplayName = names[i],
            }).ToList(),
        };
    }

    /// <summary>
    /// I run it and I play in none of it — and one of the matches is being played right now.
    ///
    /// <para><b>The first sample with no my-match in it, which is the whole reason it exists.</b>
    /// The other six are written from inside the bracket, because <c>Playable</c>,
    /// <c>JoinRoom</c>, <c>ReturnToRoom</c> and <c>WaitingOpponent</c> are four answers to one
    /// question about one match of mine. An organiser has none of that: every card is somebody
    /// else's, and the only one that is doing anything says two people are playing and offers
    /// nothing to do about it. That is the state this exists to show.</para>
    ///
    /// <para>Deliberately built so the new card sits BESIDE cards that still offer nothing —
    /// a decided match and one waiting on a rival — rather than alone. A screenshot where the
    /// only card visible is the new one cannot show that the rest were left alone.</para>
    ///
    /// <para><see cref="Entrant"/> gives every entrant a <c>u-</c> id of its own and this method
    /// overrides none of them, so <see cref="MeUserId"/> appears here exactly once: as
    /// <c>OwnerUserId</c>. <c>TournamentDemoDataTests</c> asserts that rather than trusting
    /// it.</para>
    /// </summary>
    internal static TournamentDetail Organiser()
    {
        var entrants = new List<TournamentEntrant>();
        for (int i = 0; i < 8; i++)
        {
            entrants.Add(Entrant($"g{i + 1}", SoloNames[i + 4], i + 1, "confirmed"));
        }

        return Linked(new TournamentDetail
        {
            Id = OrganiserId,
            Name = Strings.Get("MpTournamentDemoOrganiserName"),
            ModId = null,
            // Mine to run. NOT mine to play - no entrant below carries MeUserId.
            OwnerUserId = MeUserId,
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "running",
            Capacity = 8,
            ConfirmedCount = 8,
            BracketSize = 8,
            RoundsTotal = 3,
            Entrants = entrants,
            Matches = new List<TournamentMatch>
            {
                Played("gm0", 1, 0, "g1", "g8", "g1"),
                Played("gm1", 1, 1, "g4", "g5", "g5"),
                Played("gm2", 1, 2, "g2", "g7", "g2"),
                Played("gm3", 1, 3, "g3", "g6", "g3"),

                // THE ONE. Two other people, a room on it, and the game already started.
                // Today this renders a green dot and nothing else.
                new()
                {
                    Id = "gm4", Round = 2, Position = 0,
                    Entrant1Id = "g1", Entrant2Id = "g5", Status = "pending",
                    Lobby = new TournamentMatchLobby
                    {
                        Id = "DEMOLIVE", HostUserId = "u-g1", Status = "in_game",
                    },
                },

                // Beside it: one that has not been opened, and the final waiting on both.
                new() { Id = "gm5", Round = 2, Position = 1, Entrant1Id = "g2", Entrant2Id = "g3", Status = "pending" },
                new() { Id = "gm6", Round = 3, Position = 0, Status = "pending" },
            },
        });
    }

    private static TournamentEntrant Entrant(string id, string name, int seed, string status) => new()
    {
        Id = id,
        Kind = "solo",
        DisplayName = name,
        CaptainUserId = $"u-{id}",
        Seed = seed > 0 ? seed : null,
        Status = status,
        MemberIds = new List<string> { $"u-{id}" },
    };

    /// <summary>
    /// A room I opened myself, still standing.
    ///
    /// <para>Its own scenario because it has to be. A person is one entrant in a tournament
    /// and an entrant has one live match, so a single viewer can never see two of
    /// <c>Playable</c>, <c>JoinRoom</c>, <c>ReturnToRoom</c> and <c>WaitingOpponent</c> in
    /// the same bracket - they are four answers to the same question about the same match.
    /// Covering all four takes four tournaments; this is the third.</para>
    ///
    /// <para>The card it produces is the weaker offer of the set: a ghost button rather than
    /// a filled one, because walking back into your own room is not the same invitation as
    /// opening one or joining the one your opponent is sitting in.</para>
    /// </summary>
    internal static TournamentDetail MyRoom()
    {
        var entrants = new List<TournamentEntrant>
        {
            Entrant("o1", SoloNames[0], 1, "confirmed"),
            Entrant("o2", SoloNames[2], 2, "confirmed"),
            Entrant("o3", SoloNames[4], 3, "confirmed"),
            Entrant("o4", SoloNames[7], 4, "confirmed"),
        };
        entrants[0].MemberIds = new List<string> { MeUserId };
        entrants[0].CaptainUserId = MeUserId;

        return Linked(new TournamentDetail
        {
            Id = MyRoomId,
            Name = Strings.Get("MpTournamentDemoMyRoomName"),
            ModId = null,
            OwnerUserId = MeUserId,      // mine, so the owner's strip shows beside a live bracket
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "running",
            Capacity = 4,
            ConfirmedCount = 4,
            BracketSize = 4,
            RoundsTotal = 2,
            Entrants = entrants,
            Matches = new List<TournamentMatch>
            {
                new()
                {
                    // Mine, and the room's host is me.
                    Id = "om0", Round = 1, Position = 0,
                    Entrant1Id = "o1", Entrant2Id = "o4", Status = "pending",
                    Lobby = new TournamentMatchLobby
                    {
                        Id = "DEMOMINE", HostUserId = MeUserId, Status = "open",
                    },
                },
                new()
                {
                    Id = "om1", Round = 1, Position = 1,
                    Entrant1Id = "o2", Entrant2Id = "o3",
                    Status = "done", Outcome = "played", WinnerEntrantId = "o2",
                },
                new() { Id = "om2", Round = 2, Position = 0, Entrant2Id = "o2", Status = "pending" },
            },
        });
    }

    /// <summary>
    /// I won my match and the match that decides my next opponent has not been played.
    ///
    /// <para>The fourth of the four exclusive states, and the one the card answers with a
    /// sentence rather than a button: there is nothing to open yet. It is also where the
    /// "Winner of X and Y" slot label earns its keep - the empty half of my next match names
    /// the two people it is waiting on instead of reading "to be decided", which is the
    /// difference between a bracket you can follow and a column of shrugs.</para>
    /// </summary>
    internal static TournamentDetail Waiting()
    {
        var entrants = new List<TournamentEntrant>();
        for (int i = 0; i < 8; i++)
        {
            entrants.Add(Entrant($"w{i + 1}", SoloNames[i], i + 1, "confirmed"));
        }
        entrants[0].MemberIds = new List<string> { MeUserId };
        entrants[0].CaptainUserId = MeUserId;

        var matches = new List<TournamentMatch>
        {
            // I am through, and so is seed 2. My next opponent is still being decided in wm1,
            // which is what I am waiting on; the bottom half has a match left too.
            //
            // wm1 USED to be decided (w4) while my next match still had its second slot empty,
            // which the server can never produce: it seats a winner in the same write that
            // decides the match. Harmless while the preview only drew it; wrong the moment the
            // preview started PLAYING the bracket, because the next result would have advanced
            // into a slot that should already have been taken.
            Played("wm0", 1, 0, "w1", "w8", "w1"),
            new() { Id = "wm1", Round = 1, Position = 1, Entrant1Id = "w4", Entrant2Id = "w5", Status = "pending" },
            Played("wm2", 1, 2, "w2", "w7", "w2"),
            new() { Id = "wm3", Round = 1, Position = 3, Entrant1Id = "w3", Entrant2Id = "w6", Status = "pending" },
            // MY next match: one side known, the other still coming. No button, and the
            // empty side says which match it is waiting on.
            new() { Id = "wm4", Round = 2, Position = 0, Entrant1Id = "w1", Status = "pending" },
            new() { Id = "wm5", Round = 2, Position = 1, Entrant1Id = "w2", Status = "pending" },
            new() { Id = "wm6", Round = 3, Position = 0, Status = "pending" },
        };

        return Linked(new TournamentDetail
        {
            Id = WaitingId,
            Name = Strings.Get("MpTournamentDemoWaitingName"),
            ModId = null,
            OwnerUserId = "w5",
            Format = "1v1",
            TeamSource = "solo",
            EntryMode = "open",
            Status = "running",
            Capacity = 8,
            ConfirmedCount = 8,
            BracketSize = 8,
            RoundsTotal = 3,
            Entrants = entrants,
            Matches = matches,
        });
    }

    private static TournamentMatch Played(
        string id, int round, int position, string a, string b, string winner) => new()
    {
        Id = id, Round = round, Position = position,
        Entrant1Id = a, Entrant2Id = b,
        Status = "done", Outcome = "played", WinnerEntrantId = winner,
    };

    /// <summary>
    /// A sample by a name a human would type, for <c>--demo-tournaments=&lt;name&gt;</c>.
    ///
    /// <para>Matched on a prefix rather than exactly, and case-insensitively: this is a
    /// developer argument, and being strict about it would only mean typing it twice. An
    /// unknown name returns null and the caller falls back to the first sample rather than
    /// opening on nothing.</para>
    /// </summary>
    internal static TournamentDetail? ScenarioByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var wanted = name.Trim();

        var byName = new (string Name, Func<TournamentDetail> Make)[]
        {
            ("big32", Big32),
            ("running", Running),
            ("teams", Teams),
            ("myroom", MyRoom),
            ("waiting", Waiting),
            ("organiser", Organiser),
            ("registration", Registration),
            ("finished", Finished),
        };

        foreach (var (candidate, make) in byName)
        {
            if (candidate.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)) return make();
        }
        // An id works too, so a log line naming DEMOCUP4 can be reopened straight from it.
        return ById(wanted);
    }

    /// <summary>One line of the watched room's chat.</summary>
    internal sealed record WatchChatLine(string Author, string Text, string Time = "", bool IsSystem = false);

    /// <summary>The three moments an organiser can open a match in.</summary>
    internal enum MatchWatchStage { Playing, Waiting, Finished }

    /// <summary>
    /// The contents of a room being watched from the bracket, minus its roster.
    ///
    /// <para>No player names here. <c>MatchWatchWindow</c> reads those off the bracket slot,
    /// because that is the fact an organiser came to check and two copies of it could
    /// disagree.</para>
    /// </summary>
    internal sealed record MatchWatchSample(
        string RoomTitle,
        string ModName,
        string ModVersion,
        int StartedMinutesAgo,
        IReadOnlyList<WatchChatLine> Chat,
        MatchWatchStage Stage = MatchWatchStage.Playing,
        IReadOnlyList<string>? Sides = null,
        string ResultAt = "")
    {
        /// <summary>The second line of each side in a one-person match, in bracket order.</summary>
        public IReadOnlyList<string> SideLines => Sides ?? Array.Empty<string>();
    }

    /// <summary>
    /// The sample room behind <see cref="Organiser"/>'s live match.
    ///
    /// <para>The chat is written to be the reason somebody would open this at all: not
    /// small talk, but the shape of a dispute an organiser might have to settle — which is
    /// what makes it possible to judge whether watching a room is worth building.</para>
    /// </summary>
    internal static MatchWatchSample WatchSample(MatchWatchStage stage = MatchWatchStage.Playing) => new(
        RoomTitle: Strings.Get("MpTournamentDemoWatchRoomTitle"),
        ModName: Strings.Get("MpTournamentDemoWatchMod"),
        ModVersion: "1.6.2",
        StartedMinutesAgo: stage == MatchWatchStage.Finished ? 4 : 12,
        Chat: stage switch
        {
            MatchWatchStage.Waiting => new List<WatchChatLine>
            {
                new("", Strings.Get("MpTournamentDemoWatchOpened"), IsSystem: true),
                new(SoloNames[4], Strings.Get("MpTournamentDemoWatchReady"), "20:58"),
                new("", Strings.Get("MpTournamentDemoWatchOnlyEntrants"), IsSystem: true),
            },
            MatchWatchStage.Finished => new List<WatchChatLine>
            {
                new("", Strings.Get("MpTournamentDemoWatchStarted"), IsSystem: true),
                new(SoloNames[8], "gg", "21:34"),
                new(SoloNames[4], "gg wp", "21:34"),
                new("", Strings.Get("MpTournamentDemoWatchEnded"), IsSystem: true),
            },
            _ => new List<WatchChatLine>
            {
                new("", Strings.Get("MpTournamentDemoWatchStarted"), IsSystem: true),
                new(SoloNames[4], Strings.Get("MpTournamentDemoWatchChat1"), "21:19"),
                new(SoloNames[8], Strings.Get("MpTournamentDemoWatchChat2"), "21:19"),
                new(SoloNames[4], Strings.Get("MpTournamentDemoWatchChat3"), "21:20"),
                new(SoloNames[8], Strings.Get("MpTournamentDemoWatchChat4"), "21:20"),
            },
        },
        Stage: stage,
        Sides: new[] { "Industrial · 1630 ELO", "Colonial · 1244 ELO" },
        ResultAt: "21:35");

    /// <summary>Every scenario, for tests and for anything that wants to walk them all.</summary>
    internal static IReadOnlyList<TournamentDetail> All() =>
        new[]
        {
            Big32(), Running(), Teams(), MyRoom(), Waiting(), Organiser(), Registration(), Finished(),
        }.ToList();
}
