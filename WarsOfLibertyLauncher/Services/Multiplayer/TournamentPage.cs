using System;
using System.Collections.Generic;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

internal enum TournamentRowGroup { Playing, Organising, Others }

/// <summary>One row of the tournament list.</summary>
internal sealed record TournamentListRow(
    TournamentSummary Summary, TournamentRowGroup Group, string Sub, string? Alert, bool AlertIsRequests, bool Mine);

internal enum TournamentStepState { Done, Now, Next }

internal sealed record TournamentStep(string Text, TournamentStepState State);

internal enum TournamentTab { Bracket, People, Organise }

internal enum TournamentPrimary { None, OpenRoom, CloseRegistration, OpenRegistration, Seed, Start, Enter, MyEntry }

/// <summary>What the card above the bracket says, and its one primary button.</summary>
internal sealed record TournamentCall(
    string? Kicker = null,
    string? Title = null,
    string? Sub = null,
    TournamentPrimary Primary = TournamentPrimary.None,
    string? PrimaryText = null,
    TournamentPrimary Secondary = TournamentPrimary.None,
    TournamentMatch? Match = null,
    string? Note = null,
    string? Next = null,
    string? Warn = null,
    bool Champion = false);

/// <summary>Where one match sits in the bracket, in unscaled pixels.</summary>
internal sealed record MatchBox(TournamentMatch Match, double X, double Y, double Width, double Height);

internal sealed record BracketLine(double X, double Y, double Width, double Height);

internal sealed record BracketSheet(
    IReadOnlyList<MatchBox> Cards, IReadOnlyList<BracketLine> Lines,
    double Width, double Height, double ColumnWidth, double CardHeight, double Gutter, double Pad);

/// <summary>What became of an entrant, for the Participants tab.</summary>
internal sealed record EntrantFate(string Text, string Tone);

/// <summary>
/// The rules of the Tournaments page, apart from the page. Nothing here decides anything about a
/// tournament: who won, who may act and what is playable come from the server, and
/// <see cref="TournamentPermissions"/> and <see cref="MatchCards"/> read them. This decides what to
/// say about it, and where things go.
/// </summary>
internal static class TournamentPage
{
    internal const double ColumnWidthSolo = 220, ColumnWidthTeam = 320, Gutter = 40, Pad = 16;
    internal const double CardHeightSolo = 60, CardHeightTeam = 74, Top = 8;

    /// <summary>The name of a round, with the real count when the server gave none.</summary>
    internal static string RoundName(TournamentDetail t, int round)
    {
        // The table shouts them, for headings; in a sentence they are ordinary words.
        var name = Strings.Format(BracketLayout.RoundLabelKey(round, t.RoundsTotal), round).ToLower(Strings.Culture);
        return name.Length == 0 ? name : char.ToUpper(name[0], Strings.Culture) + name[1..];
    }

    internal static bool IsTeam(TournamentSummary t) => !string.Equals(t.Format, "1v1", StringComparison.Ordinal);

    /// <summary>The round being played: the first with something pending.</summary>
    internal static int? CurrentRound(TournamentDetail t)
    {
        var matches = t.Matches;
        if (matches == null || matches.Count == 0) return null;
        var live = matches.Where(m => m.Status == "pending").Select(m => m.Round).ToList();
        return live.Count > 0 ? live.Min() : matches.Max(m => m.Round);
    }

    // ---------------------------------------------------------------- the list

    /// <summary>JUEGAS, ORGANIZAS and OTROS, the ones asking for something first.</summary>
    /// <param name="open">The tournament on screen: the one row that knows its bracket even with a
    /// server that does not say what is mine.</param>
    internal static IReadOnlyList<TournamentListRow> Rows(
        TournamentListResponse? list, string? me, TournamentDetail? open, string filter)
    {
        var entries = (list?.MyEntries ?? new List<MyTournamentEntry>())
            .Where(e => !string.IsNullOrEmpty(e.TournamentId))
            .ToDictionary(e => e.TournamentId, StringComparer.Ordinal);
        var all = (list?.Drafts ?? new()).Concat(list?.Tournaments ?? new());
        var rows = new List<TournamentListRow>();
        foreach (var t in all)
        {
            if (filter != "all" && t.Status != filter) continue;
            bool mine = TournamentPermissions.IsOwner(t, me);
            var detail = open != null && open.Id == t.Id ? open : null;
            bool manages = mine || (detail != null && TournamentPermissions.IsOwnerOrManager(detail, me));

            entries.TryGetValue(t.Id, out var entry);
            var entrant = detail == null ? null : TournamentPermissions.MyEntrant(detail, me);
            bool playing = entry != null || (entrant != null && !MatchCards.EntrantIsOut(entrant));

            var alert = entry?.Alert;
            if (detail != null)
            {
                var state = detail.Matches?.Select(m => MatchCards.For(m, me, detail.Entrants))
                    .FirstOrDefault(s => s is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom)
                    ?? MatchCardState.NotMine;
                alert = state switch
                {
                    MatchCardState.Playable or MatchCardState.ReturnToRoom => "play",
                    MatchCardState.JoinRoom => "join",
                    _ => null,
                };
            }
            string? text = alert switch
            {
                "play" => Strings.Get("TpAlertPlay"),
                "join" => Strings.Get("TpAlertJoin"),
                _ => null,
            };
            bool requests = false;
            if (text == null && manages && t.PendingCount is > 0 and var pending)
            {
                text = Strings.Format(pending == 1 ? "TpAlertRequest" : "TpAlertRequests", pending);
                requests = true;
            }

            var group = playing ? TournamentRowGroup.Playing
                : manages ? TournamentRowGroup.Organising
                : TournamentRowGroup.Others;
            rows.Add(new TournamentListRow(t, group, SubLine(t, detail), text, requests, mine));
        }
        return rows.OrderBy(r => r.Group).ThenBy(r => r.Alert == null).ToList();
    }

    private static string SubLine(TournamentSummary t, TournamentDetail? detail)
    {
        string state = t.Status switch
        {
            "running" when detail != null && CurrentRound(detail) is int round
                => Strings.Format("TpSubRunningRound", RoundName(detail, round)),
            "running" => Strings.Get("TpSubRunning"),
            "registration" when t.Capacity is int cap
                => Strings.Format("TpSubRegistration", t.ConfirmedCount ?? 0, cap),
            "registration" => Strings.Get("TpSubRegistrationBare"),
            "ready" => Strings.Get("TpStepSeeds"),
            "finished" when detail?.WinnerEntrantId is { Length: > 0 } winner
                => Strings.Format("TpSubFinishedWinner", NameOf(detail, winner)),
            "finished" => Strings.Get("TpStepDone"),
            "draft" => Strings.Get("TpSubDraft"),
            _ => "",
        };
        return string.IsNullOrEmpty(t.Format) ? state : state + " · " + t.Format;
    }

    /// <summary>The five steps of a tournament, or none for one that was cancelled.</summary>
    internal static IReadOnlyList<TournamentStep> Steps(string? status)
    {
        int now = status switch
        {
            "draft" => 0,
            "registration" => 1,
            "ready" => 2,
            "running" => 3,
            "finished" => 4,
            _ => -1,
        };
        if (now < 0) return Array.Empty<TournamentStep>();
        var keys = new[] { "TpStepCreated", "TpStepRegistration", "TpStepSeeds", "TpStepRunning", "TpStepDone" };
        return keys.Select((key, i) => new TournamentStep(Strings.Get(key),
            i < now || (now == 4 && i == 4) ? TournamentStepState.Done
            : i == now ? TournamentStepState.Now : TournamentStepState.Next)).ToList();
    }

    /// <summary>"1v1 · 32 plazas · 29 inscritos · ronda 2 de 5 · organiza Kaiser".</summary>
    internal static string Meta(TournamentDetail t, string? me)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(t.Format)) parts.Add(t.Format!);
        if (t.Capacity is int cap) parts.Add(Strings.Format(IsTeam(t) ? "TpMetaTeams" : "TpMetaPlaces", cap));
        if (t.Status == "running" && t.ConfirmedCount is int confirmed && confirmed < t.Capacity)
            parts.Add(Strings.Format("TpMetaEntered", confirmed));
        if (t.Status == "registration" && t.EntryMode == "approval") parts.Add(Strings.Get("TpMetaApproval"));
        if (t.Status == "running" && CurrentRound(t) is int round && t.RoundsTotal is int total)
            parts.Add(Strings.Format("TpMetaRound", round, total));
        if (TournamentPermissions.IsOwner(t, me)) parts.Add(Strings.Get("TpMetaOrganiserYou"));
        else if (!string.IsNullOrEmpty(t.OwnerName))
            parts.Add(Strings.Format(t.Status == "finished" ? "TpMetaOrganisedBy" : "TpMetaOrganiser", t.OwnerName));
        return string.Join(" · ", parts);
    }

    internal static TournamentTab DefaultTab(TournamentDetail t, string? me)
        => t.Matches is { Count: > 0 } ? TournamentTab.Bracket
            : t.Status == "draft" && TournamentPermissions.IsOwnerOrManager(t, me) ? TournamentTab.Organise
            : t.Status is "registration" or "ready" ? TournamentTab.People
            : TournamentTab.Bracket;

    // ---------------------------------------------------------------- the card

    /// <summary>Why the bracket cannot be drawn yet, in words, or null when it can.</summary>
    internal static string? SeedBlocker(TournamentDetail t)
    {
        var playing = (t.Entrants ?? new List<TournamentEntrant>()).Where(e => e.Status == "confirmed").ToList();
        if (playing.Count < 2) return Strings.Get("MpTournamentBlockedTooFew");

        // A draft tournament whose teams were never formed: the server refuses to seed or to
        // start it, so this is the first thing to say.
        int incomplete = playing.Count(e => TournamentPermissions.NeedsTeam(t, e));
        if (incomplete > 0) return Strings.Format("MpTournamentBlockedIncomplete", incomplete);

        int unseeded = playing.Count(e => !e.Seed.HasValue);
        return unseeded > 0 ? Strings.Format("MpTournamentBlockedSeeds", unseeded) : null;
    }

    /// <summary>What the viewer has to do here, furthest move first: reopening a closed list is also
    /// allowed from "ready", so checking it first would hide seeding and starting for ever.</summary>
    internal static TournamentCall Call(TournamentDetail t, string? me)
    {
        if (t.Status == "finished" && !string.IsNullOrEmpty(t.WinnerEntrantId)) return new TournamentCall(Champion: true);

        var matches = t.Matches ?? new List<TournamentMatch>();
        var mine = matches.Select(m => (Match: m, State: MatchCards.For(m, me, t.Entrants)))
            .Where(x => x.State is MatchCardState.Playable or MatchCardState.JoinRoom or MatchCardState.ReturnToRoom)
            .OrderBy(x => x.Match.Round).ThenBy(x => x.Match.Position)
            .Select(x => ((TournamentMatch Match, MatchCardState State)?)x)
            .FirstOrDefault();
        var secondary = TournamentPermissions.CanEnter(t, me) ? TournamentPrimary.Enter
            : TournamentPermissions.MyEntrant(t, me) != null && t.Status is "registration" or "ready" ? TournamentPrimary.MyEntry
            : TournamentPrimary.None;
        if (mine is { } tie) return MatchCall(t, me, tie.Match, tie.State);

        string places = Strings.Format("TpCallRegistrationKicker", t.ConfirmedCount ?? 0, t.Capacity ?? 0);
        if (TournamentPermissions.CanStart(t, me))
            return new TournamentCall(Strings.Get("TpCallSeedsKicker"), Strings.Get("TpCallStartTitle"), Strings.Get("MpTournamentNextStart"),
                TournamentPrimary.Start, Strings.Get("MpTournamentStart"), secondary);
        if (TournamentPermissions.CanSeed(t, me))
            return new TournamentCall(Strings.Get("TpCallSeedsKicker"), Strings.Get("TpCallSeedTitle"), SeedBlocker(t) ?? Strings.Get("MpTournamentNextSeed"),
                TournamentPrimary.Seed, Strings.Get("MpTournamentSeed"), secondary);
        if (TournamentPermissions.CanCloseRegistration(t, me))
        {
            int pending = (t.Entrants ?? new()).Count(e => e.Status == "pending");
            var title = pending == 0 ? Strings.Get("TpCallNoRequests")
                : Strings.Format(pending == 1 ? "TpCallOneRequest" : "TpCallRequests", pending);
            return new TournamentCall(places, title, Strings.Get("TpCallCloseSub"),
                TournamentPrimary.CloseRegistration, Strings.Get("MpTournamentCloseRegistration"), secondary);
        }
        if (TournamentPermissions.CanOpenRegistration(t, me))
            return new TournamentCall(Strings.Get("TpCallDraftKicker"), Strings.Get("TpCallDraftTitle"),
                Strings.Get(IsTeam(t) ? "TpCallDraftSubTeams" : "TpCallDraftSubPlayers"),
                TournamentPrimary.OpenRegistration, Strings.Get("MpTournamentOpenRegistration"), secondary);
        if (t.Status == "running" && TournamentPermissions.IsOwnerOrManager(t, me))
            return new TournamentCall(Strings.Get("TpCallOrganiserKicker"),
                CurrentRound(t) is int round ? Strings.Format("TpCallRoundOn", RoundName(t, round)) : null,
                Strings.Get("TpCallOrganiserSub"));

        var waiting = matches.FirstOrDefault(m => MatchCards.For(m, me, t.Entrants) == MatchCardState.WaitingOpponent);
        if (waiting != null)
            return new TournamentCall(Strings.Format("TpCallWaitingKicker", RoundName(t, waiting.Round).ToUpperInvariant()),
                Strings.Get("TpCallWaitingTitle"));
        if (secondary == TournamentPrimary.Enter)
            return new TournamentCall(places, Strings.Get("TpCallEnterTitle"), null, TournamentPrimary.Enter, Strings.Get("MpTournamentEnter"));
        if (secondary == TournamentPrimary.MyEntry)
            return new TournamentCall(places, Strings.Get("TpCallInTitle"), null, TournamentPrimary.MyEntry, Strings.Get("EnMyEntry"));
        return new TournamentCall();
    }

    private static TournamentCall MatchCall(TournamentDetail t, string? me, TournamentMatch m, MatchCardState state)
    {
        var round = RoundName(t, m.Round).ToUpperInvariant();
        var (kicker, button) = state switch
        {
            MatchCardState.JoinRoom => (Strings.Format("TpCallJoinKicker", round), Strings.Get("MpTournamentJoinRoom")),
            MatchCardState.ReturnToRoom => (Strings.Format("TpCallReturnKicker", round), Strings.Get("MpTournamentReturnToRoom")),
            _ => (Strings.Format("TpCallPlayKicker", round), Strings.Get("MpTournamentPlayMyMatch")),
        };
        bool team = IsTeam(t);
        var entrant = TournamentPermissions.MyEntrant(t, me);
        var rival = m.Entrant1Id == entrant?.Id ? m.Entrant2Id : m.Entrant1Id;
        return new TournamentCall(kicker, null, null, TournamentPrimary.OpenRoom, button, Match: m,
            Note: Strings.Get(team ? "TpCallRoomNoteTeams" : "TpCallRoomNote"),
            Next: NextLine(t, m),
            Warn: team && entrant != null ? Strings.Format("TpCallSides", entrant.DisplayName ?? "", NameOf(t, rival)) : null);
    }

    /// <summary>Who waits for the winner of this match, or null when it is the final.</summary>
    internal static string? NextLine(TournamentDetail t, TournamentMatch m)
    {
        var matches = t.Matches ?? new List<TournamentMatch>();
        if (t.RoundsTotal is int total && m.Round >= total) return null;
        var other = matches.FirstOrDefault(x => x.Round == m.Round && x.Position == (m.Position ^ 1));
        if (other == null) return null;
        string who = !string.IsNullOrEmpty(other.WinnerEntrantId) ? NameOf(t, other.WinnerEntrantId)
            : !string.IsNullOrEmpty(other.Entrant1Id) && !string.IsNullOrEmpty(other.Entrant2Id)
                ? Strings.Format("TpWinnerOfPair", NameOf(t, other.Entrant1Id), NameOf(t, other.Entrant2Id))
                : Strings.Format("TpWinnerOfSlot", RoundName(t, m.Round), other.Position + 1);
        return Strings.Format("TpNextVs", RoundName(t, m.Round + 1), who);
    }

    /// <summary>"Ganador de Cuartos 3", for a side nobody knows yet.</summary>
    internal static string FeederName(TournamentDetail t, TournamentMatch m, int slot)
        => Strings.Format("TpWinnerOfRound", RoundName(t, m.Round - 1), m.Position * 2 + slot);

    internal static string NameOf(TournamentDetail t, string? entrantId)
    {
        var e = t.Entrants?.FirstOrDefault(x => string.Equals(x.Id, entrantId, StringComparison.Ordinal));
        return e?.DisplayName ?? Strings.Get("MpTournamentTbd");
    }

    // ---------------------------------------------------------------- the bracket

    /// <summary>Every card and connector, in unscaled pixels: a card at round r and position p sits
    /// midway between the two that feed it, so a 64 draw is arithmetic and never a measure.</summary>
    internal static BracketSheet Sheet(TournamentDetail t)
    {
        var matches = t.Matches ?? new List<TournamentMatch>();
        bool team = IsTeam(t);
        double col = team ? ColumnWidthTeam : ColumnWidthSolo, card = team ? CardHeightTeam : CardHeightSolo;
        double row = card + 12;
        if (matches.Count == 0) return new BracketSheet(Array.Empty<MatchBox>(), Array.Empty<BracketLine>(), 0, 0, col, card, Gutter, Pad);

        int first = matches.Min(m => m.Round), last = matches.Max(m => m.Round);
        int slots = matches.Count(m => m.Round == first);
        double X(int round) => Pad + (round - first) * (col + Gutter);
        double Cy(int round, int position) => Top + (position + .5) * row * (1 << (round - first));

        var cards = new List<MatchBox>(matches.Count);
        var lines = new List<BracketLine>(matches.Count * 4);
        foreach (var m in matches)
        {
            double h = m.Status == "bye" ? card / 2 + 2 : card, cy = Cy(m.Round, m.Position);
            cards.Add(new MatchBox(m, X(m.Round), cy - h / 2, col, h));
            if (m.Round == first) continue;
            double y1 = Cy(m.Round - 1, 2 * m.Position), y2 = Cy(m.Round - 1, 2 * m.Position + 1);
            double xr = X(m.Round - 1) + col, xm = xr + Gutter / 2;
            lines.Add(new BracketLine(xr, y1, Gutter / 2, 1));
            lines.Add(new BracketLine(xr, y2, Gutter / 2, 1));
            lines.Add(new BracketLine(xm, y1, 1, y2 - y1));
            lines.Add(new BracketLine(xm, cy, Gutter / 2, 1));
        }
        double width = Pad * 2 + (last - first + 1) * (col + Gutter) - Gutter;
        return new BracketSheet(cards, lines, width, Top * 2 + slots * row, col, card, Gutter, Pad);
    }

    /// <summary>The bright line from one match up to the final.</summary>
    internal static IReadOnlyList<BracketLine> PathFrom(TournamentDetail t, TournamentMatch start)
    {
        var sheet = Sheet(t);
        var path = new List<BracketLine>();
        var boxes = sheet.Cards.ToDictionary(b => (b.Match.Round, b.Match.Position));
        var at = start;
        while (true)
        {
            if (!boxes.TryGetValue((at.Round + 1, at.Position / 2), out var next)) break;
            var from = boxes[(at.Round, at.Position)];
            double xr = from.X + from.Width, xm = xr + sheet.Gutter / 2;
            double y1 = from.Y + from.Height / 2, y2 = next.Y + next.Height / 2;
            path.Add(new BracketLine(xr, y1 - .5, sheet.Gutter / 2, 2));
            path.Add(new BracketLine(xm - .5, Math.Min(y1, y2), 2, Math.Abs(y2 - y1)));
            path.Add(new BracketLine(xm, y2 - .5, sheet.Gutter / 2, 2));
            at = next.Match;
        }
        return path;
    }

    /// <summary>The path of whoever won: every match they were in, to the end.</summary>
    internal static IReadOnlyList<BracketLine> ChampionPath(TournamentDetail t)
    {
        var matches = t.Matches ?? new List<TournamentMatch>();
        var path = new List<BracketLine>();
        if (string.IsNullOrEmpty(t.WinnerEntrantId)) return path;
        var sheet = Sheet(t);
        var boxes = sheet.Cards.ToDictionary(b => (b.Match.Round, b.Match.Position));
        foreach (var m in matches.Where(x => x.Entrant1Id == t.WinnerEntrantId || x.Entrant2Id == t.WinnerEntrantId))
        {
            if (!boxes.TryGetValue((m.Round + 1, m.Position / 2), out var next)) continue;
            var from = boxes[(m.Round, m.Position)];
            double xr = from.X + from.Width, xm = xr + sheet.Gutter / 2;
            double y1 = from.Y + from.Height / 2, y2 = next.Y + next.Height / 2;
            path.Add(new BracketLine(xr, y1 - .5, sheet.Gutter / 2, 2));
            path.Add(new BracketLine(xm - .5, Math.Min(y1, y2), 2, Math.Abs(y2 - y1)));
            path.Add(new BracketLine(xm, y2 - .5, sheet.Gutter / 2, 2));
        }
        return path;
    }

    // ---------------------------------------------------------------- participants

    /// <summary>Where an entrant fell, from the bracket alone. Null before there is one.</summary>
    internal static EntrantFate? FateOf(TournamentDetail t, TournamentEntrant e)
    {
        var matches = t.Matches;
        if (matches == null || matches.Count == 0 || e.Status != "confirmed" && e.Status != "disqualified") return null;
        if (t.WinnerEntrantId == e.Id) return new EntrantFate(Strings.Get("TpFateChampion"), "ok");
        var lost = matches.FirstOrDefault(m => m.Status == "done" && !string.IsNullOrEmpty(m.WinnerEntrantId)
            && m.WinnerEntrantId != e.Id && (m.Entrant1Id == e.Id || m.Entrant2Id == e.Id));
        if (lost != null)
        {
            var round = RoundName(t, lost.Round);
            if (t.Status == "finished" && lost.Round == t.RoundsTotal) return new EntrantFate(Strings.Get("TpFateFinalist"), "plain");
            return lost.Outcome switch
            {
                "walkover" => new EntrantFate(Strings.Format("TpFateAbsent", round), "caution"),
                "dq" => new EntrantFate(Strings.Format("TpFateDq", round), "danger"),
                _ => new EntrantFate(Strings.Format("TpFateOut", round), "muted"),
            };
        }
        if (e.Status == "disqualified") return new EntrantFate(Strings.Get("MpTournamentEntrantDisqualified"), "danger");
        return CurrentRound(t) is int now ? new EntrantFate(Strings.Format("TpFateStill", RoundName(t, now)), "ok") : null;
    }

    /// <summary>Whether a search text is in an entrant's name or roster.</summary>
    internal static bool Matches(TournamentEntrant? e, string query)
    {
        if (e == null || string.IsNullOrEmpty(query)) return false;
        return (e.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (e.Members ?? new()).Any(m => (m.DisplayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
