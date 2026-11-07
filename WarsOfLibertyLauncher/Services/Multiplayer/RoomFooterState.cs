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
}

/// <summary>The dot beside the footer's title.</summary>
public enum RoomDot { Neutral, Green, Orange, Blue }

/// <summary>The footer's one primary button.</summary>
public enum RoomPrimary { None, Start, Ready, Reopen, Rematch, CancelCountdown }

/// <summary>The button beside it, when the state has one.</summary>
public enum RoomSecondary { None, Unready, Assistant, FindAnother }

/// <summary>What the way out says, and whether it is the red one.</summary>
public enum RoomLeave { Room, Match, Rooms }

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
    RoomLeave Leave);

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
        switch (f.Stage)
        {
            case RoomStage.Countdown:
                return View(RoomDot.Green, new("RoomFtStarting"), new("RoomFtStartingSub"),
                    RoomPrimary.CancelCountdown);
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
                        ? new("RoomFtTeamsAutoYou", seats)
                        : new("RoomFtTeamsAutoGuest", seats, f.HostName);
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
