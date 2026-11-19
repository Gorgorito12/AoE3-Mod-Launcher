using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

public enum MatchWatchState { Loading, Waiting, Playing, Result, Gone }

public sealed record WatchPerson(string Name, string Sub, bool Missing, string? AvatarUrl, string? UserId);

public sealed record WatchSide(string? Heading, IReadOnlyList<WatchPerson> People);

public sealed record WatchChatEntry(string Author, string Time, string Text);

public sealed record WatchResultView(string Headline, string Score, string Sub, string When);

/// <summary>Everything the read-only window draws, decided once (design 99, live).</summary>
public sealed record MatchWatchModel(
    MatchWatchState State,
    string Header,
    string Title,
    string FormatText,
    string ModName,
    string StateText,
    string Foot,
    IReadOnlyList<WatchSide> Sides,
    int InRoom,
    int Total,
    IReadOnlyList<WatchChatEntry>? Chat,
    WatchResultView? Result);

/// <summary>
/// The read-only "See match" window, as a pure function of what <c>GET /lobbies/:id/live</c>
/// answered. Pinned by <c>MatchWatchViewTests</c>: a player being placed (or keeping his rating
/// private) never shows a number even if one is sent, no stored result never names a winner, and
/// a window that is gone never asks again.
/// </summary>
public static class MatchWatchView
{
    /// <summary>How often an open, visible window asks while the room is waiting or playing.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    /// <summary>Whether this state may still change (a result is final; a gone room is gone).</summary>
    public static bool ShouldPoll(MatchWatchState state)
        => state is MatchWatchState.Loading or MatchWatchState.Waiting or MatchWatchState.Playing;

    /// <param name="live">The server's answer; null while loading.</param>
    /// <param name="gone">The server answered 404: closed more than a day ago, or not ours to see.</param>
    /// <param name="chat">The room's chat for an organiser; null for everybody else.</param>
    public static MatchWatchModel Build(LobbyLiveResponse? live, bool gone, string modName,
        DateTime nowUtc, IReadOnlyList<LobbyLiveChatLine>? chat = null, string? fallbackTitle = null)
    {
        var title = string.IsNullOrWhiteSpace(live?.Title) ? fallbackTitle ?? "" : live!.Title!;
        // The small line above the title says whose game it is; the title already says the room.
        var header = (string.IsNullOrWhiteSpace(modName) ? title : modName).ToUpper(Strings.Culture);
        if (gone)
        {
            return new MatchWatchModel(MatchWatchState.Gone, header, title, "", modName,
                Strings.Get("MpWatchGone"), Strings.Get("MpWatchGoneFoot"),
                Array.Empty<WatchSide>(), 0, 0, null, null);
        }
        if (live == null)
        {
            return new MatchWatchModel(MatchWatchState.Loading, header, title, "", modName,
                Strings.Get("MpWatchLoading"), "", Array.Empty<WatchSide>(), 0, 0, null, null);
        }

        int seats = Math.Max(0, (live.MaxPlayers ?? 0) - (live.SpectatorSlots ?? 0));
        bool competitive = live.Competitive == true;
        var format = RoomFormats.LabelKey(RoomFormats.Resolve(competitive, live.MaxPlayers ?? 0, live.SpectatorSlots ?? 0));
        var formatText = !competitive ? Strings.Get("MpWatchCasual")
            : format != null ? Strings.Format("MpWatchCompetitive", Strings.Get(format))
            : Strings.Get("MpWatchRanked");
        bool teamLadder = seats >= 4;

        var chatEntries = chat?.Select(l => new WatchChatEntry(
            l.Login ?? "", l.At is long at ? DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) : "",
            l.Body ?? "")).ToList();

        var result = live.Result;
        if (result != null || string.Equals(live.Phase, "ended", StringComparison.Ordinal))
        {
            var players = result?.Players ?? new List<LobbyLiveResultPlayer>();
            var winners = players.Where(p => p.Result >= 0.999).Select(NameOf).ToList();
            var losers = players.Where(p => p.Result <= 0.001).ToList();
            bool decided = winners.Count > 0 && losers.Count > 0;
            var minutesAgo = MinutesSince(result?.EndedAt, nowUtc);
            var civs = string.Join(Strings.Get("MpWatchVersusJoin"), players
                .GroupBy(p => p.Team ?? 0).OrderBy(g => g.Key)
                .Select(g => string.Join(", ", g.Select(p => string.IsNullOrEmpty(p.Civ) ? "?" : p.Civ))));
            var duration = result?.DurationSeconds is int d && d > 0 ? Strings.Format("MpWatchMinutes", d / 60) : "";
            var sub = string.Join(" · ", new[] { civs, duration }.Where(s => s.Length > 0 && s != "?"));
            var view = decided
                ? new WatchResultView(Strings.Format("MpWatchWon", string.Join(", ", winners)), "1–0", sub,
                    minutesAgo is int m ? Strings.Format("MpWatchFinishedAgo", m) : "")
                : new WatchResultView(Strings.Get("MpWatchNoWinner"), "", sub,
                    minutesAgo is int m2 ? Strings.Format("MpWatchFinishedAgo", m2) : "");
            IReadOnlyList<WatchSide> sides = players.Count == 0 ? Array.Empty<WatchSide>()
                : players.GroupBy(p => p.Team ?? 0).OrderBy(g => g.Key).Select(g => new WatchSide(
                    players.Select(p => p.Team ?? 0).Distinct().Count() > 1 && g.Count() > 1 ? Strings.Format("MpWatchTeam", g.Key) : null,
                    g.Select(p => new WatchPerson(NameOf(p), CivLine(p), false, p.AvatarUrl, p.Id)).ToList())).ToList();
            return new MatchWatchModel(MatchWatchState.Result, header, title, formatText, modName,
                view.When.Length > 0 ? view.When : Strings.Get("MpWatchEnded"),
                Strings.Get("MpWatchFootResult"), sides, players.Count, players.Count,
                chatEntries, view);
        }

        var members = (live.Members ?? new List<LobbyLiveMember>())
            .Where(m => !string.Equals(m.Role, "spectator", StringComparison.Ordinal)).ToList();
        bool hasTeams = members.Any(m => m.Team is 1 or 2);
        var people = members.Select(m => (Team: m.Team ?? 0, Person: new WatchPerson(
            Display(m.DisplayName, m.DiscordUsername), RatingLine(m, teamLadder), false, m.AvatarUrl, m.Id))).ToList();
        var liveSides = hasTeams
            ? people.GroupBy(p => p.Team).OrderBy(g => g.Key == 0 ? 99 : g.Key).Select(g => new WatchSide(
                g.Key == 0 ? Strings.Get("MpWatchNoTeam") : Strings.Format("MpWatchTeam", g.Key),
                g.Select(x => x.Person).ToList())).ToList()
            : new List<WatchSide> { new(null, people.Select(x => x.Person).ToList()) };

        bool playing = string.Equals(live.Phase, "playing", StringComparison.Ordinal)
                       || string.Equals(live.Status, "in_game", StringComparison.Ordinal);
        var state = playing ? MatchWatchState.Playing : MatchWatchState.Waiting;
        var stateText = playing
            ? (MinutesSince(live.StartedAt, nowUtc) is int min ? Strings.Format("MpWatchInGameFor", min) : Strings.Get("MpWatchInGame"))
            : Strings.Format("MpWatchWaitingSeats", members.Count, seats);
        var foot = chatEntries == null ? Strings.Get("MpWatchFootLiveNoChat") : Strings.Get("MpWatchFootLiveChat");
        return new MatchWatchModel(state, header, title, formatText, modName, stateText, foot,
            liveSides, members.Count, seats, chatEntries, null);
    }

    private static string Display(string? display, string? login)
        => string.IsNullOrWhiteSpace(display) ? login ?? "—" : display!;

    private static string NameOf(LobbyLiveResultPlayer p) => Display(p.DisplayName, null);

    private static string CivLine(LobbyLiveResultPlayer p)
    {
        var verdict = p.Result >= 0.999 ? Strings.Get("MpWatchWinnerTag") : p.Result <= 0.001 ? Strings.Get("MpWatchLoserTag") : "";
        var civ = string.IsNullOrEmpty(p.Civ) ? "" : p.Civ!;
        return string.Join(" · ", new[] { civ, verdict }.Where(s => s.Length > 0));
    }

    /// <summary>The number under a name: never for somebody being placed or keeping it private,
    /// even when one is sent.</summary>
    internal static string RatingLine(LobbyLiveMember m, bool team)
    {
        var rating = team ? m.RatingTeam : m.Rating;
        var games = team ? m.GamesPlayedTeam : m.GamesPlayed;
        var required = team ? StatsDemoData.PlacementTeam : StatsDemoData.PlacementDefault;
        if (games is int g && g < required)
            return g == 0 ? Strings.Get("MpWatchUnrated") : RatingDisplay.PlacementLabel(g, required, shortForm: false);
        if (rating is double r) return $"{PlacementView.RatingText(r)} ELO";
        if (RatingDisplay.IsPrivateRating(rating, games, required)) return Strings.Get("MpEloHiddenPrivate");
        return "";
    }

    private static int? MinutesSince(string? sqliteOrIso, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(sqliteOrIso)) return null;
        var text = sqliteOrIso!.Contains('T') ? sqliteOrIso : sqliteOrIso.Replace(' ', 'T') + "Z";
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)) return null;
        return Math.Max(0, (int)Math.Floor((nowUtc - at).TotalMinutes));
    }
}
