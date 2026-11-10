using System;
using System.Collections.Generic;

namespace WarsOfLibertyLauncher.Services.Multiplayer;

/// <summary>Where a room is, as its footer sees it (design 87).</summary>
public enum RoomStage
{
    Room,
    Countdown,
    InGame,
    GameClosed,
    ResultWaiting,
    ResultRated,
    ResultPlacement,
    ResultUnrated,

    /// <summary>A tournament room: the game was played and the recording decided nothing.</summary>
    ResultUndecided,

    /// <summary>A tournament team match: sent, and the other side has yet to confirm it.</summary>
    ResultAwaiting,
}

/// <summary>The dot beside the footer's title.</summary>
public enum RoomDot { Neutral, Green, Orange, Blue, Accent }

/// <summary>The figure beside the footer's title.</summary>
public enum RoomClock { None, Waiting, Match, Countdown }

/// <summary>The footer's one primary button.</summary>
public enum RoomPrimary { None, Start, Ready, Reopen, Rematch, CancelCountdown, ViewBracket }

/// <summary>The button beside it, when the state has one.</summary>
public enum RoomSecondary { None, Unready, Assistant, FindAnother }

/// <summary>What the way out says, and whether it is the red one.</summary>
public enum RoomLeave { Room, Match, Rooms, Abandon, None }

/// <summary>A string key and its arguments, formatted by the window.</summary>
public sealed record RoomText(string Key, params object[] Args);

/// <summary>Everything the footer depends on. No WPF, no session, so every state can be tested.</summary>
public sealed record RoomFacts
{
    public RoomStage Stage { get; init; }
    public bool Competitive { get; init; }
    public bool HasTeams { get; init; }
    public bool IsHost { get; init; }

    /// <summary>Players in the room, observers aside.</summary>
    public int Players { get; init; }

    /// <summary>Seats to play in; 0 when not known.</summary>
    public int Seats { get; init; }

    /// <summary>Players not marked ready, not counting the host of a casual room, who starts it.</summary>
    public int NotReady { get; init; }

    public bool IAmReady { get; init; }
    public StartGate.Verdict? Gate { get; init; }
    public string HostName { get; init; } = "";

    /// <summary>The other player in a two-seat room; null while the seat is free.</summary>
    public string? RivalName { get; init; }

    public bool RivalReady { get; init; }

    /// <summary>Who left and made this player the host; null otherwise.</summary>
    public string? HostLeftName { get; init; }

    public bool VpnMissing { get; init; }

    /// <summary>The rival of a rematch not yet joined; null otherwise.</summary>
    public string? RematchWaitingFor { get; init; }

    public bool OffersRematch { get; init; }
    public bool OffersFindAnother { get; init; }

    /// <summary>Why a match did not count, already in the player's language.</summary>
    public string? UnratedReason { get; init; }

    /// <summary>The players on a team, for the footer's count.</summary>
    public int OnTeams { get; init; }

    /// <summary>A bracket match (design 85).</summary>
    public bool Tournament { get; init; }

    /// <summary>The first of the other side, or everyone they wait for, still out of the room.</summary>
    public string? Absent { get; init; }

    /// <summary>Who runs the tournament; null when the server did not say.</summary>
    public string? Organizer { get; init; }

    /// <summary>Everybody who plays, across both sides.</summary>
    public int MatchPlayers { get; init; }

    /// <summary>The line under "the bracket has moved on".</summary>
    public RoomText? AfterResult { get; init; }

    /// <summary>The bracket match was won: the only ending the footer marks in green.</summary>
    public bool ResultWon { get; init; }

    /// <summary>The viewer sits in an observer's seat: he neither plays nor marks ready.</summary>
    public bool Watching { get; init; }
}

/// <summary>What the footer shows for some facts.</summary>
public sealed record RoomFooterView(
    RoomDot Dot,
    bool WarmTitle,
    RoomText Title,
    RoomText? Sub,
    RoomPrimary Primary,
    bool PrimaryEnabled,
    string? PrimaryTipKey,
    RoomSecondary Secondary,
    RoomLeave Leave,
    RoomClock Clock = RoomClock.None);

/// <summary>
/// The footer of the room window: one line of state and one primary button (design 87).
///
/// <para>Before this the room had four large buttons whose visibility six methods each decided on
/// their own. The rule lives here now, once, and the window only paints the answer.</para>
///
/// <para><b>A casual host cannot start until everyone else is ready</b>, which is what the design
/// asks for and a change from before, when Start was always open. In a competitive room the match
/// starts itself when the room is full and everyone is ready, so the host marks ready like anybody
/// else; Start only comes back when that did not happen, because the auto-start fires once and a
/// cancelled confirmation would otherwise leave the room with no way to begin.</para>
/// </summary>
public static class RoomFooterState
{
    public static RoomFooterView Evaluate(RoomFacts f)
    {
        if (f.Tournament) return Tournament(f);
        switch (f.Stage)
        {
            case RoomStage.Countdown:
                return View(RoomDot.Green, new("RoomFtStarting"), new("RoomFtStartingSub"),
                    f.Watching ? RoomPrimary.None : RoomPrimary.CancelCountdown);
            case RoomStage.InGame:
                return View(RoomDot.Green, new("RoomFtInGame"), new("RoomFtInGameSub"), leave: RoomLeave.Match);
            case RoomStage.GameClosed:
                return View(RoomDot.Orange, new("RoomFtClosed"),
                    new(f.IsHost ? "RoomFtClosedHostSub" : "RoomFtClosedSub"),
                    RoomPrimary.Reopen, leave: RoomLeave.Match, warm: true);
            case RoomStage.ResultWaiting:
                return View(RoomDot.Neutral, new("RoomFtResultWait"), new("RoomFtResultWaitSub"), leave: RoomLeave.Rooms);
            case RoomStage.ResultRated:
                return Result(f, RoomDot.Green, new("RoomFtRegistered"),
                    new(f.HasTeams ? "RoomFtCountsAll" : "RoomFtCountsBoth"));
            case RoomStage.ResultPlacement:
                return Result(f, RoomDot.Green, new("RoomFtRegistered"), new("RoomFtPlacementSub"));
            case RoomStage.ResultUnrated:
                return Result(f, RoomDot.Neutral, new("RoomFtNoElo"),
                    string.IsNullOrEmpty(f.UnratedReason) ? null : new("RoomFtPlain", f.UnratedReason!));
        }

        var view = f.Competitive ? Competitive(f) : Casual(f);
        // An observer reads the same state of the room and never marks ready: the server starts on
        // the players alone. He can still be the HOST (the server hands the room on by join order),
        // and only the host may start, so a watching host keeps Start for when the players are ready.
        if (f.Watching)
        {
            var canStart = f.Players >= 2 && f.NotReady == 0 && (!f.Competitive || (f.Gate?.CanStart ?? true));
            view = f.IsHost
                ? View(view.Dot, view.Title, new("RoomFtWatchingHostSub"), RoomPrimary.Start, enabled: canStart,
                    tip: canStart ? null : f.Players < 2 ? "RoomTipNeedsPlayer" : "RoomTipNeedsReady")
                : View(view.Dot, view.Title, new("RoomFtWatchingSub", f.HostName));
        }
        if (f.RematchWaitingFor is { } rival)
        {
            view = view with
            {
                Dot = RoomDot.Blue,
                WarmTitle = false,
                Title = new("RoomFtRivalWait", rival),
                Sub = new("RoomFtRematchSub"),
            };
        }
        if (f.IsHost && f.HostLeftName is { } gone)
        {
            view = view with
            {
                Dot = RoomDot.Blue,
                WarmTitle = false,
                Title = new("RoomFtNowHost"),
                Sub = new("RoomFtNowHostSub", gone),
            };
        }
        // Last, because it is the one a player can do something about. It has its own button.
        if (f.VpnMissing)
        {
            view = view with
            {
                Dot = RoomDot.Orange,
                WarmTitle = true,
                Title = new("RoomFtVpn"),
                Sub = new("RoomFtVpnSub"),
                Secondary = RoomSecondary.Assistant,
            };
        }
        return view;
    }

    /// <summary>
    /// A bracket match (85). What differs from an ordinary room is what comes next: nothing here
    /// is rematched, and the way on after any result is the bracket. <b>An undecided game offers
    /// the bracket too, against the prototype's "Estoy listo": the server closes every room after a
    /// report, whatever it says, so the replay is a new room opened from Tournaments.</b>
    /// </summary>
    private static RoomFooterView Tournament(RoomFacts f)
    {
        var rival = f.RivalName ?? f.Absent ?? "";
        RoomFooterView view;
        switch (f.Stage)
        {
            case RoomStage.Countdown:
                return View(RoomDot.Accent, new("RtFtCountdown"),
                    f.IsHost ? new("RtFtCountdownHost") : new("RtFtCountdownGuest", f.HostName),
                    RoomPrimary.CancelCountdown, leave: RoomLeave.None) with { Clock = RoomClock.Countdown };
            case RoomStage.InGame:
                return View(RoomDot.Green, new("RoomFtInGame"),
                    f.IsHost ? new("RtFtGameHost") : new("RtFtGameGuest", f.HostName),
                    leave: RoomLeave.Abandon) with { Clock = RoomClock.Match };
            case RoomStage.GameClosed:
                return View(RoomDot.Orange, new("RoomFtClosed"),
                    new(f.IsHost ? "RoomFtClosedHostSub" : "RoomFtClosedSub"),
                    RoomPrimary.Reopen, leave: RoomLeave.Abandon, warm: true);
            case RoomStage.ResultWaiting:
                return View(RoomDot.Neutral, new("RoomFtResultWait"), new("RoomFtResultWaitSub"),
                    RoomPrimary.ViewBracket);
            case RoomStage.ResultRated:
            case RoomStage.ResultPlacement:
            case RoomStage.ResultUnrated:
                return View(f.ResultWon ? RoomDot.Green : RoomDot.Neutral, new("RtFtMoved"),
                    f.AfterResult ?? new("RtFtFollow"), RoomPrimary.ViewBracket);
            case RoomStage.ResultUndecided:
                return View(RoomDot.Orange, new("RtFtNotYet"), new("MpTournamentReportUndecided"),
                    RoomPrimary.ViewBracket, warm: true);
            case RoomStage.ResultAwaiting:
                return View(RoomDot.Blue, new("RtFtSentWaiting"), new("MpTournamentReportAwaiting"),
                    RoomPrimary.ViewBracket);
        }

        if (f.Absent is { } absent)
        {
            var two = f.MatchPlayers <= 2;
            view = Guest(f, RoomDot.Neutral,
                f.IAmReady && two ? new("RtFtReadyNoRival", absent) : new("RoomFtRivalWait", absent),
                !two ? new("RtFtAutoAll", new RoomKey("RtCount" + f.MatchPlayers))
                    : f.Organizer is { } who ? new("RtFtSoloSub", absent, who) : new("RtFtSoloSubNoName", absent));
            if (two) view = view with { Clock = RoomClock.Waiting };
        }
        else
        {
            var two = f.MatchPlayers <= 2;
            RoomText title = !two ? new("RoomFtAllReady")
                : f.IAmReady && !f.RivalReady ? new("RtFtReadyWaiting", rival)
                : f.RivalReady && !f.IAmReady ? new("RoomFtRivalReady", rival)
                : f.IsHost ? new("RtFtRivalJoined", rival) : new("RtFtInRivalRoom", rival);
            view = Guest(f, f.IAmReady || f.RivalReady ? RoomDot.Green : RoomDot.Neutral, title,
                two ? new("RtFtAutoTwo") : new("RtFtAutoAll", new RoomKey("RtCount" + f.MatchPlayers)));
            if (f.IsHost && f.IAmReady && f.NotReady == 0) view = View(view.Dot, view.Title, view.Sub, RoomPrimary.Start);
        }
        if (f.VpnMissing)
        {
            view = view with
            {
                Dot = RoomDot.Orange,
                WarmTitle = true,
                Title = new("RoomFtVpn"),
                Sub = new("RtFtVpnSub"),
                Secondary = RoomSecondary.Assistant,
                Clock = RoomClock.None,
            };
        }
        return view;
    }

    private static RoomFooterView Casual(RoomFacts f)
    {
        var alone = f.Players <= 1;
        var ready = f.NotReady == 0;
        RoomDot dot;
        RoomText title;
        RoomText sub;
        if (alone)
        {
            (dot, title, sub) = (RoomDot.Neutral, new("RoomFtWaiting"), new("RoomFtWaitingSub"));
        }
        else if (ready)
        {
            (dot, title) = (RoomDot.Green, new("RoomFtAllReady"));
            sub = f.IsHost ? new("RoomFtYouHost") : new("RoomFtGuestHost", f.HostName);
        }
        else
        {
            (dot, title) = (RoomDot.Neutral, new("RoomFtCount", f.Players - f.NotReady, f.Players));
            sub = !f.IsHost ? new("RoomFtGuestHost", f.HostName)
                : f.NotReady == 1 ? new("RoomFtOneMissing")
                : new("RoomFtManyMissing", f.NotReady);
        }

        if (f.IsHost)
            return View(dot, title, sub, RoomPrimary.Start, enabled: !alone && ready,
                tip: alone ? "RoomTipNeedsPlayer" : ready ? null : "RoomTipNeedsReady");
        return Guest(f, dot, title, sub);
    }

    private static RoomFooterView Competitive(RoomFacts f)
    {
        var gate = f.Gate ?? new StartGate.Verdict(StartGate.Reason.Ready);
        RoomDot dot;
        RoomText title;
        RoomText? sub;
        if (f.HasTeams)
        {
            var seats = f.Seats > 0 ? f.Seats : f.Players;
            switch (gate.Reason)
            {
                case StartGate.Reason.PlayerWithoutTeam:
                    var names = gate.WithoutTeam ?? Array.Empty<string>();
                    (dot, title) = (RoomDot.Orange, new("RoomFtInTeams", f.OnTeams, seats));
                    sub = new(names.Count == 1 ? "RoomFtNoTeam" : "RoomFtNoTeamPl", NameList.Join(names));
                    break;
                case StartGate.Reason.UnevenTeams:
                    (dot, title) = (RoomDot.Orange, new("RoomFtUneven"));
                    sub = new("RoomFtUnevenSub", gate.Team1, gate.Team2);
                    break;
                case StartGate.Reason.MissingPlayers:
                    (dot, title) = (RoomDot.Neutral, new("RoomFtInTeams", f.OnTeams, seats));
                    sub = new("RoomFtAutoSeats");
                    break;
                default:
                    (dot, title) = (RoomDot.Green, new("RoomFtTeamsFull"));
                    sub = f.IsHost
                        ? new("RoomFtTeamsAutoYou", new RoomKey("RtCount" + seats))
                        : new("RoomFtTeamsAutoGuest", new RoomKey("RtCount" + seats), f.HostName);
                    break;
            }
        }
        else if (f.RivalName is not { } rival)
        {
            (dot, title, sub) = (RoomDot.Neutral, new("RoomFtRivalMissing"), new("RoomFtAutoSeats"));
        }
        else
        {
            (dot, title) = f.RivalReady
                ? (RoomDot.Green, new RoomText("RoomFtRivalReady", rival))
                : (RoomDot.Neutral, new RoomText("RoomFtRivalWait", rival));
            sub = f.IsHost ? new("RoomFtAutoTwoYou") : new("RoomFtAutoTwoGuest", f.HostName);
        }

        // Everyone ready and the gate open, and yet the room is still here: the automatic start
        // already fired, or its confirmation was cancelled. Start is the way on.
        if (f.IsHost && gate.CanStart && f.IAmReady && f.NotReady == 0)
            return View(dot, title, sub, RoomPrimary.Start);
        return Guest(f, dot, title, sub);
    }

    /// <summary>Somebody who waits for the host: ready is the primary until it is pressed.</summary>
    private static RoomFooterView Guest(RoomFacts f, RoomDot dot, RoomText title, RoomText? sub)
        => f.IAmReady
            ? View(dot, title, sub, secondary: RoomSecondary.Unready)
            : View(dot, title, sub, RoomPrimary.Ready);

    private static RoomFooterView Result(RoomFacts f, RoomDot dot, RoomText title, RoomText? sub)
        => View(dot, title, sub,
            f.OffersRematch ? RoomPrimary.Rematch : RoomPrimary.None,
            secondary: f.OffersFindAnother ? RoomSecondary.FindAnother : RoomSecondary.None,
            leave: RoomLeave.Rooms);

    private static RoomFooterView View(
        RoomDot dot, RoomText title, RoomText? sub,
        RoomPrimary primary = RoomPrimary.None, bool enabled = true, string? tip = null,
        RoomSecondary secondary = RoomSecondary.None, RoomLeave leave = RoomLeave.Room, bool warm = false)
        => new(dot, warm, title, sub, primary, enabled, tip, secondary, leave);
}
