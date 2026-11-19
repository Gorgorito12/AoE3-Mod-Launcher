using System;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// Leaving a competitive room while its result is still being settled: the window goes at once,
/// the launcher leaves the room behind it a few seconds later.
///
/// <para>It used to answer Leave and ✕ with "One moment" and keep the window up for as long as
/// half a minute, because leaving is what costs the match its result: the server refuses a report
/// from a host who is no longer the host, and a guest who leaves never sees his. Neither needs the
/// WINDOW, only the membership, so the window is hidden and the membership is kept until
/// <see cref="RoomMatchState.FinishDetachedLeave"/> says it can go. The result then arrives as a
/// card (<see cref="AnnounceResultWithoutAWindow"/>), like any result whose window is gone.</para>
///
/// <para>While detached, everything that writes to the room window still writes to it; what the
/// player must see goes through <see cref="RoomWindowOnScreen"/>. "View room" on the Rooms page
/// brings the window back and cancels the leave.</para>
/// </summary>
public partial class MultiplayerTab
{
    private bool _detachedLeave;
    private bool _detachedAsHost;
    private bool _detachedResultKnown;
    private DispatcherTimer? _detachTimer;

    /// <summary>Whether the room window is open AND on screen; hidden during a detached leave.</summary>
    private bool RoomWindowOnScreen => _lobbyWindow != null && !_detachedLeave;

    private void DetachRoomForResult()
    {
        if (_lobbyWindow == null || _detachedLeave) return;
        _detachedLeave = true;
        _detachedAsHost = ResultContext()?.IsHost == true;
        _detachedResultKnown = _roomResult != null;
        DiagnosticLog.Write($"MultiplayerTab: room window hidden while the result is settled " +
            $"({(_detachedAsHost ? "host" : "guest")}, phase {_resultPhase}); the room is left when it is done.");
        _lobbyWindow.Hide();
        _bringMultiplayerToFront?.Invoke();

        try
        {
            _showAppToast?.Invoke(new AppToast.ToastOptions(
                AppToast.ToastTone.Info, AppToast.ToastIcon.Info,
                Strings.Get("MpDetachedTitle"),
                Strings.Get(_detachedAsHost ? "MpDetachedBodyHost" : "MpDetachedBodyGuest"),
                Array.Empty<AppToast.ToastAction>(), AutoDismissMs: 7000, Cause: ToastCause.Answer));
        }
        catch (Exception ex) { DiagnosticLog.Write($"Detached-leave toast failed: {ex.Message}"); }

        _detachTimer?.Stop();
        _detachTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _detachTimer.Tick += (_, _) => MaybeFinishDetachedLeave();
        _detachTimer.Start();
        MaybeFinishDetachedLeave();
    }

    /// <summary>Leaves the room once the result is settled, or at the hold's ceiling. Safe to call any time.</summary>
    private void MaybeFinishDetachedLeave()
    {
        if (!_detachedLeave) return;
        var seconds = (DateTime.UtcNow - _resultPhaseSinceUtc).TotalSeconds;
        if (!RoomMatchState.FinishDetachedLeave(_detachedAsHost, _resultPhase, _detachedResultKnown, seconds)) return;
        DiagnosticLog.Write($"MultiplayerTab: the result is settled ({seconds:0} s) - leaving the room.");
        EndDetachedLeave();
        CloseLobbyWindow();
    }

    /// <summary>The player opened the room again: it stays, on screen.</summary>
    private void CancelDetachedLeave()
    {
        if (!_detachedLeave || _lobbyWindow == null) return;
        DiagnosticLog.Write("MultiplayerTab: room window shown again; the leave is cancelled.");
        EndDetachedLeave();
        _lobbyWindow.Show();
    }

    private void EndDetachedLeave()
    {
        _detachedLeave = false;
        _detachTimer?.Stop();
        _detachTimer = null;
    }

    /// <summary>The result of a detached room has arrived (or will not): a guest may go now.</summary>
    private void NoteDetachedResult()
    {
        if (!_detachedLeave) return;
        _detachedResultKnown = true;
        MaybeFinishDetachedLeave();
    }
}
