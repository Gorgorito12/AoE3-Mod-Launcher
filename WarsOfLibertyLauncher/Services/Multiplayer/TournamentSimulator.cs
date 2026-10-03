using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Who the tournament preview is looking AS.</summary>
internal enum TournamentPreviewViewer
{
    /// <summary>Somebody who plays in it and does not run it.</summary>
    Player,

    /// <summary>Whoever created it — who may also be playing in it.</summary>
    Organiser,

    /// <summary>Somebody with nothing to do with it.</summary>
    Spectator,
}

/// <summary>
/// What one preview operation would have pushed to the person looking, and whether a "play on"
/// request found nothing it was allowed to play.
/// </summary>
internal sealed record PreviewOutcome(IReadOnlyList<TournamentUpdateNotice> ToViewer, bool Stuck = false)
{
    internal static PreviewOutcome Nothing { get; } = new(Array.Empty<TournamentUpdateNotice>());
}

/// <summary>
/// The tournament SERVER the preview talks to: the seven fabricated tournaments of
/// <see cref="TournamentDemoData"/>, kept in memory and changed by the same routes, with the same
/// refusals in the same words, as <c>wol-launcher-lobby-node/src/tournaments/rest.ts</c>.
///
/// <para><b>A server and not a set of buttons, on purpose.</b> The tab talks to this through
/// <see cref="ITournamentApi"/> exactly as it talks to the real one, so the confirmations, the
/// error notices, the refresh after every action and the new-tournament dialog that appear in the
/// preview are the production code paths. A preview that reimplemented the buttons would show a
/// screen that resembles the feature; this one shows the feature.</para>
///
/// <para><b>Nothing leaves the launcher.</b> No request, no file, no setting. Everything lives
/// until <see cref="Reset"/> or until the launcher closes.</para>
///
/// <para><b>What it copies from the server, including what looks wrong.</b> The bracket rules are
/// <see cref="TournamentRules"/>. Two route behaviours a fix would hide are copied deliberately,
/// because the preview's job is to show what WOULD happen: a disqualification never finishes the
/// tournament even when its cascade decides the final, and never gives the seat back. Pinned by
/// <c>TournamentSimulatorTests</c> so nobody "fixes" them here alone.</para>
///
/// <para><b>Where it differs, and why each is harmless:</b> there is no cap on tournaments
/// created (the samples already give the viewer more than the server's two); <c>ModId</c> stays
/// null, which is a second lock on the real room flow; ratings are invented, deterministically,
/// from the user id; the list keeps a stable order rather than the server's most-recent-first,
/// which would reshuffle on every click; and results are decided by a console instead of by a
/// recording.</para>
///
/// <para><b>Who is looking.</b> One person at a time, chosen by <see cref="ViewerChoice"/> and
/// resolved per tournament by <see cref="ViewerOf"/>. Left unchosen, each sample is seen by the
/// person it was written for, so opening the preview shows exactly what it always showed.</para>
/// </summary>
internal sealed class TournamentSimulator
{
    /// <summary>The outsider, for the spectator's view.</summary>
    internal const string SpectatorUserId = "demo-spectator";

    /// <summary>A player who has not entered yet, for the player's view of a tournament nobody
    /// outside its organisers has joined.</summary>
    internal const string NewPlayerUserId = "demo-player";

    // The names the three viewers enter under. Invented, like every other name here.
    private const string MeName = "Gorgo";
    private const string NewPlayerName = "Corsario";
    private const string SpectatorName = "Observador";

    /// <summary>More invented names for the people the console signs up, after the samples' own.</summary>
    private static readonly string[] ExtraPlayerNames =
    {
        "Granadero", "Montonero", "Caudillo", "Bandeirante", "Cimarrón", "Corregidor",
        "Lancero", "Coracero", "Húsar", "Dragón", "Jenízaro", "Mameluco",
        "Tlatoani", "Virrey", "Guerrillero", "Chinaco",
    };

    private static readonly string[] ExtraTeamNames =
    {
        "Tercio del Plata", "Legión Andina", "Compañía Franca", "Batallón del Alba",
        "Escuadra Austral", "Real Armada",
    };

    /// <summary>The longest room title the server composes.</summary>
    private const int TitleMax = 80;

    private readonly List<TournamentDetail> _store = new();

    /// <summary>When each entrant registered, as a counter. Drives the waiting list's first-come
    /// order and seeding's tie-break, which the server reads from <c>registered_at</c>. Keyed by
    /// tournament AND entrant: two samples reuse the same entrant ids.</summary>
    private readonly Dictionary<string, long> _registeredAt = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _userNames = new(StringComparer.Ordinal);

    /// <summary>The player each tournament's "player" view settled on, kept so that pressing a
    /// button as that player does not quietly turn the view into somebody else.</summary>
    private readonly Dictionary<string, string> _playerOf = new(StringComparer.Ordinal);

    private long _clock;
    private int _seq;
    private int _created;

    internal TournamentSimulator() => Reset();

    /// <summary>Who is looking, or null for "whoever each sample was written for".</summary>
    internal TournamentPreviewViewer? ViewerChoice { get; set; }

    /// <summary>Every sample as it was written, and nothing created. The fixtures build fresh
    /// objects on every call, so this store never shares an object with anybody.</summary>
    internal void Reset()
    {
        _store.Clear();
        _registeredAt.Clear();
        _userNames.Clear();
        _playerOf.Clear();
        _clock = 0;
        _seq = 0;
        _created = 0;
        ViewerChoice = null;

        _userNames[TournamentDemoData.MeUserId] = MeName;
        _userNames[NewPlayerUserId] = NewPlayerName;
        _userNames[SpectatorUserId] = SpectatorName;

        foreach (var t in TournamentDemoData.All())
        {
            Normalise(t);
            _store.Add(t);
        }
    }

    /// <summary>Whether this server holds a tournament with that id.</summary>
    internal bool Contains(string? id) => Find(id) != null;

    // ---------------------------------------------------------------- reads

    /// <summary>
    /// The list, shaped like <c>GET /tournaments</c>: every tournament that is not a draft, plus
    /// the drafts of whoever is looking — who is decided by the selected tournament, since that
    /// is where the "view as" choice is made.
    /// </summary>
    internal TournamentListResponse List(string? selectedId)
    {
        var viewer = ViewerOf(selectedId);
        return new TournamentListResponse
        {
            Tournaments = _store.Where(t => t.Status != "draft").Select(Summary).ToList(),
            Drafts = _store
                .Where(t => t.Status == "draft"
                            && string.Equals(t.OwnerUserId, viewer, StringComparison.Ordinal))
                .Select(Summary)
                .ToList(),
        };
    }

    /// <summary>
    /// One tournament, shaped like <c>GET /tournaments/:id</c>, as a COPY.
    ///
    /// <para>A copy for the reason a server answer is one: the tab captures what it rendered in
    /// its click handlers, and a handler acting on an object this store later changed would be
    /// acting on something that no longer describes the screen. Everything here is done by id.</para>
    /// </summary>
    internal TournamentDetail? Detail(string? id)
    {
        var t = Find(id);
        if (t == null) return null;

        var copy = Clone(t);
        copy.Name = DisplayNameOf(t);
        // The server's own order: seeds first, then the order people signed up in.
        copy.Entrants = (copy.Entrants ?? new List<TournamentEntrant>())
            .OrderBy(e => e.Seed ?? 9999)
            .ThenBy(e => RegisteredAt(t, e.Id))
            .ToList();
        copy.Matches = (copy.Matches ?? new List<TournamentMatch>())
            .OrderBy(m => m.Round)
            .ThenBy(m => m.Position)
            .ToList();
        return copy;
    }

    // ---------------------------------------------------------------- who is looking

    /// <summary>The role being looked through for this tournament.</summary>
    internal TournamentPreviewViewer RoleOf(string? tournamentId)
    {
        if (ViewerChoice is { } chosen) return chosen;
        var t = Find(tournamentId);
        if (t == null) return TournamentPreviewViewer.Player;

        // The person each sample was written for, so the preview opens exactly as it used to.
        var me = TournamentDemoData.MeUserId;
        if (string.Equals(t.OwnerUserId, me, StringComparison.Ordinal)) return TournamentPreviewViewer.Organiser;
        if (IsMember(t, me)) return TournamentPreviewViewer.Player;
        return TournamentPreviewViewer.Spectator;
    }

    /// <summary>
    /// The user id the tournament is being looked at AS.
    ///
    /// <para><b>Player</b>: the preview's own player when they play in it without running it;
    /// otherwise the first entrant who does not run it; otherwise somebody who has not entered yet.
    /// <b>Organiser</b>: whoever created it. <b>Spectator</b>: nobody involved.</para>
    /// </summary>
    internal string ViewerOf(string? tournamentId)
    {
        var t = Find(tournamentId);
        if (t == null) return TournamentDemoData.MeUserId;

        return RoleOf(tournamentId) switch
        {
            TournamentPreviewViewer.Organiser => t.OwnerUserId ?? SpectatorUserId,
            TournamentPreviewViewer.Spectator => SpectatorUserId,
            _ => PlayerOf(t),
        };
    }

    private string PlayerOf(TournamentDetail t)
    {
        if (_playerOf.TryGetValue(t.Id, out var sticky) && !Runs(t, sticky)) return sticky;

        var me = TournamentDemoData.MeUserId;
        string? pick = null;
        if (IsMember(t, me) && !Runs(t, me)) pick = me;
        else if (IsMember(t, NewPlayerUserId)) pick = NewPlayerUserId;
        else
        {
            pick = Entrants(t)
                .Where(e => !MatchCards.EntrantIsOut(e)
                            && !string.IsNullOrEmpty(e.CaptainUserId)
                            && !Runs(t, e.CaptainUserId!))
                .OrderBy(e => e.Seed ?? 9999)
                .ThenBy(e => RegisteredAt(t, e.Id))
                .Select(e => e.CaptainUserId)
                .FirstOrDefault();
        }

        if (string.IsNullOrEmpty(pick)) return NewPlayerUserId;
        _playerOf[t.Id] = pick!;
        return pick!;
    }

    // ---------------------------------------------------------------- the routes

    /// <summary><c>POST /tournaments</c>, from the body the tab sends. Owned by the preview's own
    /// player, whatever view is selected: the button is "you, creating a tournament".</summary>
    internal TournamentSummary Create(JsonElement body)
    {
        string? Str(string property)
            => body.ValueKind == JsonValueKind.Object
               && body.TryGetProperty(property, out var v)
               && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        var name = (Str("name") ?? "").Trim();
        if (name.Length > TitleMax) name = name[..TitleMax];
        if (name.Length < 3) throw BadRequest("name too short");

        var modId = (Str("mod_id") ?? "").Trim();
        if (modId.Length == 0) throw BadRequest("mod_id is required");

        var format = OneOf(Str("format") ?? "1v1", new[] { "1v1", "2v2", "3v3" }, "format");
        var teamSource = OneOf(Str("team_source") ?? "solo",
                               new[] { "solo", "registered", "adhoc", "draft" }, "team_source");
        var entryMode = OneOf(Str("entry_mode") ?? "open", new[] { "open", "approval" }, "entry_mode");
        if (!TournamentRules.TeamSourceAllowed(format, teamSource))
            throw BadRequest($"team_source {teamSource} is not valid for a {format}");

        int capacity = 8;
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("capacity", out var cap)
            && cap.ValueKind == JsonValueKind.Number)
        {
            capacity = (int)Math.Truncate(cap.GetDouble());
        }
        capacity = Math.Clamp(capacity, 2, 16);

        var t = new TournamentDetail
        {
            Id = $"SIMCUP{++_created}",
            Name = name,
            // Null whatever was picked: OpenTournamentMatchAsync stops on a null mod, which keeps
            // the real room flow shut behind a second lock.
            ModId = null,
            OwnerUserId = TournamentDemoData.MeUserId,
            Format = format,
            TeamSource = teamSource,
            EntryMode = entryMode,
            Status = "draft",
            Capacity = capacity,
            ConfirmedCount = 0,
            Entrants = new List<TournamentEntrant>(),
            Matches = new List<TournamentMatch>(),
            ManagerUserIds = new List<string>(),
            Managers = new List<TournamentEntrantMember>(),
        };
        // First, not last: the person who just made it is looking for it.
        _store.Insert(0, t);
        return Summary(t);
    }

    internal PreviewOutcome OpenRegistration(string id)
    {
        var t = AsManager(id);
        if (t.Status is not ("draft" or "ready"))
            throw Conflict("Registration cannot be opened from this state.");
        t.Status = "registration";
        return PreviewOutcome.Nothing;
    }

    internal PreviewOutcome CloseRegistration(string id)
    {
        var t = AsManager(id);
        if (t.Status != "registration") throw Conflict("Registration is not open.");
        t.Status = "ready";
        return PreviewOutcome.Nothing;
    }

    /// <summary>Seeds every confirmed entrant 1..N, strongest first, on invented ratings.</summary>
    internal PreviewOutcome Seed(string id)
    {
        var t = AsManager(id);
        if (t.Status != "ready") throw Conflict("Close registration before seeding.");

        var playing = Entrants(t).Where(e => TournamentRules.PlaysInBracket(e.Status)).ToList();
        if (playing.Count < 2) throw Conflict("At least two entrants are needed.");

        var seeds = TournamentRules.SeedByRating(
            playing.Select(e => new TournamentRules.SeedableEntrant(
                e.Id, e.MemberIds ?? new List<string>(), RegisteredAt(t, e.Id))),
            PseudoRating);
        foreach (var (entrantId, seed) in seeds)
        {
            Entrants(t).First(e => e.Id == entrantId).Seed = seed;
        }
        return PreviewOutcome.Nothing;
    }

    internal PreviewOutcome Start(string id)
    {
        var t = AsManager(id);
        if (t.Status != "ready") throw Conflict("Close registration before starting.");
        if (Matches(t).Count > 0) throw Conflict("The bracket has already been drawn.");

        var playing = Entrants(t).Where(e => TournamentRules.PlaysInBracket(e.Status)).ToList();
        if (playing.Count < 2) throw Conflict("At least two entrants are needed.");
        if (playing.Any(e => e.Seed == null)) throw Conflict("Seed the entrants first.");
        var sorted = playing.Select(e => e.Seed!.Value).OrderBy(s => s).ToList();
        if (sorted.Where((s, i) => s != i + 1).Any())
            throw Conflict("Seeding is incomplete; seed the entrants again.");

        t.Matches = TournamentRules.Generate(
            playing.Select(e => (e.Id, e.Seed!.Value)).ToList(),
            (round, position) => $"{t.Id}-r{round}m{position}");
        t.BracketSize = TournamentRules.BracketSize(playing.Count);
        t.RoundsTotal = TournamentRules.RoundsFor(playing.Count);
        t.Status = "running";
        return PreviewOutcome.Nothing;
    }

    /// <summary>The owner's alone, like the server's.</summary>
    internal PreviewOutcome Cancel(string id)
    {
        var t = AsOwner(id);
        if (t.Status is not ("draft" or "registration" or "ready" or "running"))
            throw Conflict("This tournament is already over.");
        t.Status = "cancelled";
        foreach (var m in Matches(t)) m.Lobby = null;
        return PreviewOutcome.Nothing;
    }

    /// <summary>
    /// <c>POST /tournaments/:id/entrants</c> with the empty body the tab sends.
    ///
    /// <para>So a TEAM tournament refuses exactly as the server would: a <c>registered</c> one
    /// wants a team id and an <c>adhoc</c> one a line-up, and the launcher has no way to give
    /// either yet. That is a real gap, shown rather than papered over.</para>
    /// </summary>
    internal TournamentEntryResponse Enter(string id)
    {
        var t = Get(id);
        var actor = ViewerOf(id);
        if (t.Status != "registration") throw TournamentClosed();

        List<string> members;
        if (t.Format == "1v1") members = new List<string> { actor };
        else if (t.TeamSource == "registered") throw BadRequest("team_id required for this tournament");
        else if (t.TeamSource == "adhoc") members = new List<string>();
        else members = new List<string> { actor };

        if (t.Format != "1v1" && t.TeamSource != "draft" && !members.Contains(actor))
            throw BadRequest("the captain must be in the line-up");

        var already = new HashSet<string>(
            Entrants(t)
                .Where(e => TournamentRules.OccupiesTournament(e.Status))
                .SelectMany(e => e.MemberIds ?? new List<string>()),
            StringComparer.Ordinal);
        var refusal = TournamentRules.ValidateRoster(
            t.TeamSource == "draft" ? "1v1" : t.Format, members, already);
        if (refusal == "already_entered")
            throw Error(409, "already_entered", "You are already entered in this tournament.");
        if (refusal != null)
        {
            throw Error(400, "roster_invalid", "That line-up cannot enter this tournament.",
                        new Dictionary<string, object?> { ["reason"] = refusal });
        }

        bool seat = t.EntryMode != "approval" && ClaimSeat(t);
        var status = TournamentRules.EntryStatusFor(t.EntryMode, seat);
        bool solo = t.Format == "1v1" || t.TeamSource == "draft";
        var e = AddEntrant(t, solo ? NameOf(actor) : "Team", members, actor, status, solo);
        return new TournamentEntryResponse { EntrantId = e.Id, Status = status };
    }

    /// <summary>The captain's, and refused once the bracket exists.</summary>
    internal PreviewOutcome Withdraw(string id, string entrantId)
    {
        var t = Get(id);
        var actor = ViewerOf(id);
        var e = EntrantOf(t, entrantId) ?? throw NotFound("Entrant");
        if (!string.Equals(e.CaptainUserId, actor, StringComparison.Ordinal)) throw Forbidden();
        if (t.Status is "running" or "finished") throw Conflict("The bracket has already been drawn.");
        WithdrawCore(t, e, "withdrawn");
        return PreviewOutcome.Nothing;
    }

    /// <summary>Accepting takes a seat if there is one and leaves them waiting if not. Tells the
    /// entrant either way, with the server's two kinds.</summary>
    internal PreviewOutcome Accept(string id, string entrantId)
    {
        var t = AsManager(id);
        var e = EntrantOf(t, entrantId) ?? throw NotFound("Entrant");
        return AcceptCore(t, e);
    }

    internal PreviewOutcome Reject(string id, string entrantId)
    {
        var t = AsManager(id);
        var e = EntrantOf(t, entrantId) ?? throw NotFound("Entrant");
        WithdrawCore(t, e, "rejected");
        return PreviewOutcome.Nothing;
    }

    /// <summary>
    /// Out, and every match of theirs with a known opponent goes to that opponent. Like the
    /// server's route: no seat is released and the tournament is never finished here, even when
    /// the cascade decided the final.
    /// </summary>
    internal PreviewOutcome Disqualify(string id, string entrantId)
    {
        var t = AsManager(id);
        var e = EntrantOf(t, entrantId) ?? throw NotFound("Entrant");
        if (TournamentRules.OccupiesTournament(e.Status)) e.Status = "disqualified";
        TournamentRules.Disqualify(Matches(t), e.Id);
        CloseDecidedRooms(t);
        return PreviewOutcome.Nothing;
    }

    /// <summary>Hand a match to one side. Pushes nothing, as the server does not.</summary>
    internal PreviewOutcome Walkover(string id, string matchId, string winnerEntrantId)
    {
        var t = AsManager(id);
        if (string.IsNullOrEmpty(winnerEntrantId)) throw BadRequest("winner_entrant_id required");

        var result = TournamentRules.Advance(Matches(t), matchId, winnerEntrantId, "walkover", DisqualifiedOf(t));
        if (!result.Ok) throw Conflict($"Cannot award this match: {result.Refusal}");
        CloseDecidedRooms(t);
        if (result.TournamentDone) Finish(t, result.ChampionEntrantId);
        return PreviewOutcome.Nothing;
    }

    /// <summary>Close the room on an undecided tie and tell BOTH sides, whoever asked.</summary>
    internal PreviewOutcome Replay(string id, string matchId)
    {
        var t = AsManager(id);
        if (t.Status != "running") throw Conflict("That tournament is not running.");
        var m = MatchOf(t, matchId) ?? throw NotFound("Match");
        if (m.Status != "pending") throw Conflict("That match has already been decided.");
        if (string.IsNullOrEmpty(m.Entrant1Id) || string.IsNullOrEmpty(m.Entrant2Id))
            throw Conflict("That match does not have both sides yet.");

        m.Lobby = null;
        return Tell(t, SidesOf(t, m), _ => Notice(t, m, "match_replay"));
    }

    /// <summary>
    /// <c>POST /tournaments/:id/matches/:mid/lobby</c>: the existing room when there is one, a new
    /// one with the person looking as its host when there is not. Everybody else on the match is
    /// told — which never includes the person who opened it.
    /// </summary>
    internal (TournamentLobbyResponse Response, PreviewOutcome Outcome) OpenRoom(string id, string matchId)
    {
        var t = Get(id);
        var actor = ViewerOf(id);
        if (t.Status != "running") throw MatchNotReady();
        var m = MatchOf(t, matchId) ?? throw NotFound("Match");
        if (!TournamentRules.Playable(m)) throw MatchNotReady();
        if (!SidesOf(t, m).Contains(actor))
        {
            throw Error(403, "tournament_not_participant",
                        "This room belongs to a tournament match between two other players.");
        }

        if (m.Lobby != null)
        {
            return (new TournamentLobbyResponse
            {
                Id = m.Lobby.Id,
                Status = m.Lobby.Status ?? "open",
                HostUserId = m.Lobby.HostUserId,
                TournamentMatchId = m.Id,
                Existing = true,
                Competitive = true,
            }, PreviewOutcome.Nothing);
        }

        m.Lobby = new TournamentMatchLobby { Id = LobbyIdFor(t, m), HostUserId = actor, Status = "open" };
        var outcome = Tell(t, SidesOf(t, m).Where(u => u != actor),
                           _ => Notice(t, m, "room_opened", lobbyId: m.Lobby.Id));
        return (new TournamentLobbyResponse
        {
            Id = m.Lobby.Id,
            Status = "open",
            HostUserId = actor,
            TournamentMatchId = m.Id,
            Existing = false,
            Competitive = true,
        }, outcome);
    }

    /// <summary>Appoint a co-organiser. The owner's alone, and never the owner themselves.</summary>
    internal PreviewOutcome AddManager(string id, string userId)
    {
        var t = AsOwner(id);
        var uid = (userId ?? "").Trim();
        if (uid.Length == 0) throw BadRequest("user_id is required");
        if (string.Equals(uid, t.OwnerUserId, StringComparison.Ordinal))
            throw BadRequest("The owner already runs it.");
        if (!KnowsUser(uid)) throw BadRequest("No such player.");

        t.ManagerUserIds ??= new List<string>();
        t.Managers ??= new List<TournamentEntrantMember>();
        if (!t.ManagerUserIds.Contains(uid))
        {
            t.ManagerUserIds.Add(uid);
            t.Managers.Add(new TournamentEntrantMember { UserId = uid, DisplayName = NameIn(t, uid) });
        }
        return PreviewOutcome.Nothing;
    }

    internal PreviewOutcome RemoveManager(string id, string userId)
    {
        var t = AsOwner(id);
        if (t.ManagerUserIds == null || !t.ManagerUserIds.Remove(userId)) throw NotFound("Co-organiser");
        t.Managers?.RemoveAll(m => m.UserId == userId);
        return PreviewOutcome.Nothing;
    }

    // ---------------------------------------------------------------- what everybody else does

    /// <summary>
    /// The other side of a match opens its room. With the person looking on neither side, the
    /// first side does. Tells whoever is on the match and did not open it.
    /// </summary>
    internal PreviewOutcome SomebodyOpensRoom(string id, string matchId)
    {
        var t = Get(id);
        var m = MatchOf(t, matchId) ?? throw NotFound("Match");
        if (t.Status != "running" || !TournamentRules.Playable(m)) throw MatchNotReady();
        if (m.Lobby != null) return PreviewOutcome.Nothing;

        var opener = OpenerOf(t, m);
        var host = opener.CaptainUserId ?? opener.MemberIds?.FirstOrDefault() ?? opener.Id;
        m.Lobby = new TournamentMatchLobby { Id = LobbyIdFor(t, m), HostUserId = host, Status = "open" };
        return Tell(t, SidesOf(t, m).Where(u => u != host),
                    _ => Notice(t, m, "room_opened", lobbyId: m.Lobby.Id));
    }

    /// <summary>The entrant that would open the room: the side the person looking is NOT on.</summary>
    internal TournamentEntrant OpenerOf(TournamentDetail t, TournamentMatch m)
    {
        var viewer = ViewerOf(t.Id);
        var one = EntrantOf(t, m.Entrant1Id!)!;
        var two = EntrantOf(t, m.Entrant2Id!)!;
        return IsMemberOf(one, viewer) ? two : one;
    }

    /// <summary>
    /// A game played and reported: decided, the winner moved on, the room closed, the tournament
    /// finished if that was the final, and both sides told who won — what the server's report
    /// hook does once a recording names a winner.
    /// </summary>
    internal PreviewOutcome ReportPlayed(string id, string matchId, string winnerEntrantId)
    {
        var t = Get(id);
        var m = MatchOf(t, matchId) ?? throw NotFound("Match");
        if (t.Status != "running" || !TournamentRules.Playable(m)) throw MatchNotReady();
        return Decide(t, m, winnerEntrantId);
    }

    /// <summary>A game whose recording names nobody: the room closes and the tie stays open.</summary>
    internal PreviewOutcome ReportUnreadable(string id, string matchId)
    {
        var t = Get(id);
        var m = MatchOf(t, matchId) ?? throw NotFound("Match");
        m.Lobby = null;
        return PreviewOutcome.Nothing;
    }

    /// <summary>
    /// Decide every playable match of the earliest round that has one — except the person
    /// looking's own, which is theirs to play. <see cref="PreviewOutcome.Stuck"/> when there was
    /// nothing else to decide.
    /// </summary>
    internal PreviewOutcome PlayRound(string id)
    {
        var t = Get(id);
        if (t.Status != "running") return Stuck();
        var viewer = ViewerOf(id);

        var candidates = Matches(t)
            .Where(m => Decidable(t, m) && !SidesOf(t, m).Contains(viewer))
            .ToList();
        if (candidates.Count == 0) return Stuck();

        int round = candidates.Min(m => m.Round);
        var notices = new List<TournamentUpdateNotice>();
        foreach (var m in candidates.Where(m => m.Round == round).OrderBy(m => m.Position).ToList())
        {
            // An earlier decision in this same pass can settle one by disqualification.
            if (!Decidable(t, m)) continue;
            notices.AddRange(Decide(t, m, SimulatedWinner(t, m)).ToViewer);
        }
        return new PreviewOutcome(notices);
    }

    /// <summary>
    /// Play everything that can be played, the person looking's matches included, until there is
    /// a champion. <see cref="PreviewOutcome.Stuck"/> when it stopped short — two disqualified
    /// entrants left facing each other, which the server leaves for a person to settle.
    /// </summary>
    internal PreviewOutcome PlayToEnd(string id)
    {
        var t = Get(id);
        if (t.Status != "running") return Stuck();

        var notices = new List<TournamentUpdateNotice>();
        int guard = Matches(t).Count + 1;
        while (t.Status == "running" && guard-- > 0)
        {
            var next = Matches(t)
                .Where(m => Decidable(t, m))
                .OrderBy(m => m.Round)
                .ThenBy(m => m.Position)
                .FirstOrDefault();
            if (next == null) break;
            notices.AddRange(Decide(t, next, SimulatedWinner(t, next)).ToViewer);
        }
        return new PreviewOutcome(notices, Stuck: t.Status == "running");
    }

    /// <summary>One more person signs up, the way the server would place them.</summary>
    internal PreviewOutcome SignUp(string id, int count = 1)
    {
        var t = Get(id);
        if (t.Status != "registration") throw TournamentClosed();
        for (int i = 0; i < count; i++) SignUpOne(t);
        return PreviewOutcome.Nothing;
    }

    /// <summary>Sign people up until every place is taken or asked for.</summary>
    internal PreviewOutcome FillPlaces(string id)
    {
        var t = Get(id);
        if (t.Status != "registration") throw TournamentClosed();
        int room = (t.Capacity ?? 0) - Entrants(t).Count(e => e.Status is "confirmed" or "pending");
        for (int i = 0; i < room; i++) SignUpOne(t);
        return PreviewOutcome.Nothing;
    }

    /// <summary>The organiser answers the application of the person looking.</summary>
    internal PreviewOutcome OrganiserAcceptsViewer(string id)
    {
        var t = Get(id);
        var viewer = ViewerOf(id);
        var e = Entrants(t).FirstOrDefault(x => x.Status is "pending" or "waitlist" && IsMemberOf(x, viewer))
                ?? throw Conflict("That entrant is not awaiting a decision.");
        return AcceptCore(t, e);
    }

    // ---------------------------------------------------------------- the room behind a match

    /// <summary>
    /// The room a bracket match opened, as the room preview draws it: the title the server would
    /// compose, two sides' worth of seats, competitive, the host's side inside and the person
    /// looking with them when they joined it.
    /// </summary>
    internal RoomDemoData.Sample? RoomSample(string id, string matchId)
    {
        var t = Find(id);
        var m = t == null ? null : MatchOf(t, matchId);
        if (t == null || m?.Lobby == null) return null;

        var viewer = ViewerOf(id);
        var host = m.Lobby.HostUserId ?? "";
        var seats = new List<RoomDemoData.Seat>();

        void Add(string uid)
        {
            if (string.IsNullOrEmpty(uid) || seats.Any(s => s.UserId == uid)) return;
            seats.Add(new RoomDemoData.Seat
            {
                UserId = uid,
                Login = NameIn(t, uid),
                IsHost = uid == host,
                Rating = Math.Round(PseudoRating(uid)!.Value.Rating),
            });
        }

        Add(host);
        var hostSide = Entrants(t).FirstOrDefault(e => IsMemberOf(e, host));
        if (hostSide != null)
        {
            foreach (var u in hostSide.MemberIds ?? new List<string>()) Add(u);
        }
        if (SidesOf(t, m).Contains(viewer)) Add(viewer);

        return new RoomDemoData.Sample
        {
            Name = "tournament",
            Code = m.Lobby.Id,
            RoomName = RoomTitle(t, m),
            Seats = TournamentRules.RosterSizeFor(t.Format) * 2,
            Competitive = true,
            Players = seats,
        };
    }

    /// <summary>The server's composition: tournament, <c>Final</c> or <c>R{n}</c>, both sides —
    /// in that order, untranslated, cut at 80.</summary>
    internal string RoomTitle(TournamentDetail t, TournamentMatch m)
    {
        int rounds = t.RoundsTotal is int total && total > 0
            ? total
            : TournamentRules.RoundsFor(Entrants(t).Count);
        var label = m.Round >= rounds ? "Final" : $"R{m.Round}";
        var title = $"{DisplayNameOf(t)} · {label} · {EntrantName(t, m.Entrant1Id)} · {EntrantName(t, m.Entrant2Id)}";
        return title.Length > TitleMax ? title[..TitleMax] : title;
    }

    // ---------------------------------------------------------------- deciding

    /// <summary>
    /// Who the console says won: the better seed, with an upset one time in four — decided by a
    /// hash of the match and its two sides, so the same click gives the same result every run and
    /// a screenshot can be taken twice.
    /// </summary>
    internal string SimulatedWinner(TournamentDetail t, TournamentMatch m)
    {
        var a = m.Entrant1Id!;
        var b = m.Entrant2Id!;
        int seedA = EntrantOf(t, a)?.Seed ?? 9999;
        int seedB = EntrantOf(t, b)?.Seed ?? 9999;
        var better = seedA <= seedB ? a : b;
        var worse = better == a ? b : a;
        bool upset = TournamentRules.StableHash($"{m.Id}|{a}|{b}") % 4 == 0;
        return upset ? worse : better;
    }

    private PreviewOutcome Decide(TournamentDetail t, TournamentMatch m, string winnerEntrantId)
    {
        // Read before deciding: the sides are what the notice goes to.
        var sides = SidesOf(t, m).ToList();
        var winners = new HashSet<string>(
            EntrantOf(t, winnerEntrantId)?.MemberIds ?? new List<string>(), StringComparer.Ordinal);

        var result = TournamentRules.Advance(Matches(t), m.Id, winnerEntrantId, "played", DisqualifiedOf(t));
        if (!result.Ok) throw Conflict($"Cannot record this result: {result.Refusal}");

        m.Lobby = null;
        CloseDecidedRooms(t);
        if (result.TournamentDone) Finish(t, result.ChampionEntrantId);
        return Tell(t, sides, viewer => Notice(t, m, "match_done", youWon: winners.Contains(viewer)));
    }

    private bool Decidable(TournamentDetail t, TournamentMatch m)
    {
        if (!TournamentRules.Playable(m)) return false;
        var outSet = DisqualifiedOf(t);
        return !(outSet.Contains(m.Entrant1Id!) && outSet.Contains(m.Entrant2Id!));
    }

    private static void Finish(TournamentDetail t, string? champion)
    {
        if (t.Status != "running") return;
        t.Status = "finished";
        t.WinnerEntrantId = champion;
    }

    /// <summary>The detail only attaches a room to a PENDING match, so a decided one shows none.</summary>
    private static void CloseDecidedRooms(TournamentDetail t)
    {
        foreach (var m in t.Matches ?? new List<TournamentMatch>())
        {
            if (m.Status != "pending") m.Lobby = null;
        }
    }

    // ---------------------------------------------------------------- entrants and seats

    private PreviewOutcome AcceptCore(TournamentDetail t, TournamentEntrant e)
    {
        bool seat = ClaimSeat(t);
        var next = seat ? "confirmed" : "waitlist";
        if (e.Status is not ("pending" or "waitlist"))
        {
            if (seat) ReleaseSeat(t);
            throw Conflict("That entrant is not awaiting a decision.");
        }
        e.Status = next;
        // entry_promoted when it lands on the waiting list: the server's choice of kind, copied.
        return Tell(t, e.MemberIds ?? new List<string>(), _ => new TournamentUpdateNotice
        {
            Kind = next == "confirmed" ? "entry_accepted" : "entry_promoted",
            TournamentId = t.Id,
            TournamentName = DisplayNameOf(t),
        });
    }

    /// <summary>Take an entrant out; if they held a seat, give it to whoever has waited longest.
    /// Silent, as the server's promotions are.</summary>
    private void WithdrawCore(TournamentDetail t, TournamentEntrant e, string next)
    {
        if (!TournamentRules.OccupiesTournament(e.Status)) return;
        var was = e.Status;
        e.Status = next;
        if (was != "confirmed") return;

        ReleaseSeat(t);
        int free = Math.Max(0, (t.Capacity ?? 0) - (t.ConfirmedCount ?? 0));
        if (free <= 0) return;

        var promote = TournamentRules.PromoteFromWaitlist(
            Entrants(t).Select(x => (x.Id, x.Status, RegisteredAt(t, x.Id))), free);
        foreach (var pid in promote)
        {
            if (!ClaimSeat(t)) break;
            EntrantOf(t, pid)!.Status = "confirmed";
        }
    }

    /// <summary>A seat is CLAIMED, and only while registration is open — the server's
    /// conditional UPDATE, which is why accepting outside registration lands on the waiting
    /// list.</summary>
    private static bool ClaimSeat(TournamentDetail t)
    {
        int confirmed = t.ConfirmedCount ?? 0;
        if (t.Status != "registration" || confirmed >= (t.Capacity ?? 0)) return false;
        t.ConfirmedCount = confirmed + 1;
        return true;
    }

    private static void ReleaseSeat(TournamentDetail t)
        => t.ConfirmedCount = Math.Max(0, (t.ConfirmedCount ?? 0) - 1);

    private void SignUpOne(TournamentDetail t)
    {
        bool team = t.Format != "1v1" && t.TeamSource != "draft";
        int size = team ? TournamentRules.RosterSizeFor(t.Format) : 1;

        var members = new List<string>();
        for (int k = 0; k < size; k++)
        {
            var uid = $"sim-u{++_seq}";
            _userNames[uid] = FreshName(t, ExtraPlayerNames, TournamentDemoData.PlayerNamePool, members);
            members.Add(uid);
        }

        bool seat = t.EntryMode != "approval" && ClaimSeat(t);
        var status = TournamentRules.EntryStatusFor(t.EntryMode, seat);
        var name = team
            ? FreshTeamName(t)
            : _userNames[members[0]];
        AddEntrant(t, name, members, members[0], status, solo: !team);
    }

    private TournamentEntrant AddEntrant(
        TournamentDetail t, string displayName, List<string> members, string captain, string status, bool solo)
    {
        var e = new TournamentEntrant
        {
            Id = $"sim-e{++_seq}",
            Kind = solo ? "solo" : "team",
            DisplayName = displayName,
            CaptainUserId = captain,
            Status = status,
            MemberIds = members.ToList(),
            Members = members
                .Select(u => new TournamentEntrantMember { UserId = u, DisplayName = NameOf(u) })
                .ToList(),
        };
        Entrants(t).Add(e);
        _registeredAt[Key(t, e.Id)] = ++_clock;
        return e;
    }

    private string FreshName(
        TournamentDetail t, IReadOnlyList<string> extra, IReadOnlyList<string> samples, List<string> alsoTaken)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in Entrants(t))
        {
            if (!string.IsNullOrEmpty(e.DisplayName)) used.Add(e.DisplayName!);
            foreach (var u in e.MemberIds ?? new List<string>()) used.Add(NameIn(t, u));
        }
        foreach (var u in alsoTaken) used.Add(NameOf(u));

        foreach (var candidate in samples.Concat(extra))
        {
            if (!used.Contains(candidate)) return candidate;
        }
        // Past the pools: the first name again, numbered.
        for (int n = 2; ; n++)
        {
            var candidate = $"{extra[0]} {n}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private string FreshTeamName(TournamentDetail t)
    {
        var used = new HashSet<string>(
            Entrants(t).Select(e => e.DisplayName ?? ""), StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in TournamentDemoData.TeamNamePool.Concat(ExtraTeamNames))
        {
            if (!used.Contains(candidate)) return candidate;
        }
        for (int n = 2; ; n++)
        {
            var candidate = $"{ExtraTeamNames[0]} {n}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// An invented rating for anybody, from their id: the same person always gets the same
    /// number, so seeding is repeatable. Wide enough apart that seeding visibly orders people.
    /// </summary>
    internal static (double Rating, double Rd)? PseudoRating(string userId)
    {
        uint h = TournamentRules.StableHash(userId);
        uint h2 = TournamentRules.StableHash(userId + "#rd");
        return (1150d + h % 700, 50d + h2 % 150);
    }

    private void Normalise(TournamentDetail t)
    {
        t.Entrants ??= new List<TournamentEntrant>();
        t.Matches ??= new List<TournamentMatch>();
        t.ManagerUserIds ??= new List<string>();
        t.Managers ??= new List<TournamentEntrantMember>();
        t.ConfirmedCount ??= t.Entrants.Count(e => e.Status == "confirmed");

        // The order the samples list people in is the order they signed up in.
        foreach (var e in t.Entrants)
        {
            _registeredAt[Key(t, e.Id)] = ++_clock;
        }

        if (t.Matches.Count > 0 && t.RoundsTotal is int rounds)
        {
            TournamentRules.EnsureLinks(t.Matches, rounds);
        }
        CloseDecidedRooms(t);
    }

    private TournamentSummary Summary(TournamentDetail t) => new()
    {
        Id = t.Id,
        Name = DisplayNameOf(t),
        ModId = t.ModId,
        OwnerUserId = t.OwnerUserId,
        Format = t.Format,
        TeamSource = t.TeamSource,
        EntryMode = t.EntryMode,
        Status = t.Status,
        Capacity = t.Capacity,
        ConfirmedCount = t.ConfirmedCount,
        // The server's entrant_count counts who still holds a place, not everybody who ever asked.
        EntrantCount = Entrants(t).Count(e => TournamentRules.OccupiesTournament(e.Status)),
        PendingCount = Entrants(t).Count(e => e.Status == "pending"),
        CreatedAt = t.CreatedAt,
        LastActivityAt = t.LastActivityAt,
    };

    /// <summary>A sample is named in the language of the moment; a created one keeps its name.</summary>
    private static string DisplayNameOf(TournamentDetail t)
        => TournamentDemoData.NameKeyOf(t.Id) is { } key ? Strings.Get(key) : t.Name;

    private PreviewOutcome Tell(
        TournamentDetail t, IEnumerable<string> recipients, Func<string, TournamentUpdateNotice> noticeFor)
    {
        var viewer = ViewerOf(t.Id);
        return recipients.Contains(viewer, StringComparer.Ordinal)
            ? new PreviewOutcome(new[] { noticeFor(viewer) })
            : PreviewOutcome.Nothing;
    }

    private TournamentUpdateNotice Notice(
        TournamentDetail t, TournamentMatch m, string kind, string? lobbyId = null, bool? youWon = null) => new()
    {
        Kind = kind,
        TournamentId = t.Id,
        TournamentName = DisplayNameOf(t),
        TournamentMatchId = m.Id,
        Round = m.Round,
        RoundsTotal = t.RoundsTotal,
        LobbyId = lobbyId,
        YouWon = youWon,
    };

    private string LobbyIdFor(TournamentDetail t, TournamentMatch m)
    {
        // A fresh code every time a room opens, so a room opened again after a replay is a
        // different room — which it is, on the server.
        // Crockford base32, the alphabet the server's shortId uses.
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        int n = ++_seq;
        ulong h = ((ulong)TournamentRules.StableHash($"{t.Id}|{m.Id}|{n}") << 32)
                  | TournamentRules.StableHash($"{n}|{m.Id}|{t.Id}");
        var chars = new char[8];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[(int)(h % 32)];
            h /= 32;
        }
        return new string(chars);
    }

    private TournamentDetail? Find(string? id)
        => string.IsNullOrEmpty(id)
            ? null
            : _store.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));

    private TournamentDetail Get(string id) => Find(id) ?? throw NotFound("Tournament");

    private TournamentDetail AsManager(string id)
    {
        var t = Get(id);
        if (!Runs(t, ViewerOf(id))) throw Forbidden();
        return t;
    }

    private TournamentDetail AsOwner(string id)
    {
        var t = Get(id);
        if (!string.Equals(t.OwnerUserId, ViewerOf(id), StringComparison.Ordinal)) throw Forbidden();
        return t;
    }

    private static bool Runs(TournamentDetail t, string userId)
        => string.Equals(t.OwnerUserId, userId, StringComparison.Ordinal)
           || (t.ManagerUserIds?.Contains(userId) ?? false);

    private static bool IsMember(TournamentDetail t, string userId)
        => (t.Entrants ?? new List<TournamentEntrant>()).Any(e => IsMemberOf(e, userId));

    private static bool IsMemberOf(TournamentEntrant e, string userId)
        => e.MemberIds?.Contains(userId) ?? false;

    private bool KnowsUser(string userId)
        => _userNames.ContainsKey(userId)
           || _store.Any(t => IsMember(t, userId));

    private static List<TournamentEntrant> Entrants(TournamentDetail t) => t.Entrants ??= new();

    private static List<TournamentMatch> Matches(TournamentDetail t) => t.Matches ??= new();

    private static TournamentEntrant? EntrantOf(TournamentDetail t, string? entrantId)
        => string.IsNullOrEmpty(entrantId)
            ? null
            : Entrants(t).FirstOrDefault(e => string.Equals(e.Id, entrantId, StringComparison.Ordinal));

    private static TournamentMatch? MatchOf(TournamentDetail t, string matchId)
        => Matches(t).FirstOrDefault(m => string.Equals(m.Id, matchId, StringComparison.Ordinal));

    private static IEnumerable<string> SidesOf(TournamentDetail t, TournamentMatch m)
        => (EntrantOf(t, m.Entrant1Id)?.MemberIds ?? new List<string>())
            .Concat(EntrantOf(t, m.Entrant2Id)?.MemberIds ?? new List<string>());

    private static HashSet<string> DisqualifiedOf(TournamentDetail t)
        => new(Entrants(t).Where(e => e.Status == "disqualified").Select(e => e.Id), StringComparer.Ordinal);

    private static string EntrantName(TournamentDetail t, string? entrantId)
        => EntrantOf(t, entrantId)?.DisplayName ?? "?";

    private string NameOf(string userId)
        => _userNames.TryGetValue(userId, out var name) ? name : userId;

    /// <summary>A person's name as THIS tournament knows them; two samples reuse user ids.</summary>
    private string NameIn(TournamentDetail t, string userId)
    {
        foreach (var e in Entrants(t))
        {
            var member = e.Members?.FirstOrDefault(x => x.UserId == userId);
            if (!string.IsNullOrEmpty(member?.DisplayName)) return member!.DisplayName!;
            if (e.MemberIds is { Count: 1 } ids && ids[0] == userId && !string.IsNullOrEmpty(e.DisplayName))
                return e.DisplayName!;
        }
        return NameOf(userId);
    }

    private long RegisteredAt(TournamentDetail t, string entrantId)
        => _registeredAt.TryGetValue(Key(t, entrantId), out var at) ? at : long.MaxValue;

    private static string Key(TournamentDetail t, string entrantId) => t.Id + "/" + entrantId;

    private static TournamentDetail Clone(TournamentDetail t)
        => JsonSerializer.Deserialize<TournamentDetail>(JsonSerializer.Serialize(t))!;

    private static string OneOf(string value, string[] allowed, string field)
        => allowed.Contains(value) ? value : throw BadRequest($"{field} must be one of: {string.Join(", ", allowed)}");

    private static PreviewOutcome Stuck() => new(Array.Empty<TournamentUpdateNotice>(), Stuck: true);

    // ---------------------------------------------------------------- the server's refusals

    private static LobbyApiException Error(
        int status, string code, string message, Dictionary<string, object?>? details = null)
        => new(status, code, message, details);

    private static LobbyApiException NotFound(string what) => Error(404, "not_found", $"{what} not found.");

    private static LobbyApiException Forbidden() => Error(403, "forbidden", "Action not allowed for this user.");

    private static LobbyApiException Conflict(string message) => Error(409, "conflict", message);

    private static LobbyApiException BadRequest(string message) => Error(400, "bad_request", message);

    private static LobbyApiException TournamentClosed()
        => Error(409, "tournament_closed", "Registration for this tournament is not open.");

    private static LobbyApiException MatchNotReady()
        => Error(409, "tournament_match_not_ready", "This match is not ready to be played.");
}
