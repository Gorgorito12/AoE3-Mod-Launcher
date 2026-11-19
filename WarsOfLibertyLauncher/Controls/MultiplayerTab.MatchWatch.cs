using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// "See match": a real room watched from outside, through the server's read model
/// (<c>GET /lobbies/:id/live</c>, 2026-11-18). Offered only when the list said <c>live_view</c> —
/// without it the button opens the players peek, as before — and never as a join: none of the
/// server's doors that keep a stranger out of a room opens for it. One window per room; a second
/// click focuses it.
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The rooms list's server answers the read-only view.</summary>
    private bool _roomsLiveView;

    private readonly Dictionary<string, MatchWatchWindow> _watchWindows = new(StringComparer.Ordinal);

    /// <summary>The room's "See match" (or a bracket's Supervise on a real tournament).</summary>
    /// <param name="organiserChat">Ask for the room's chat too (owner and co-organisers of its
    /// tournament; the server refuses anybody else with a 404).</param>
    /// <param name="tournament">With <paramref name="match"/>: a bracket match, so an organiser who
    /// may decide it gets "Decide this match" here too, behind the bracket's own confirmation.</param>
    private void OpenLiveMatchWatch(string lobbyId, string? title, string? modId, bool organiserChat = false,
        TournamentDetail? tournament = null, TournamentMatch? match = null)
    {
        if (string.IsNullOrEmpty(lobbyId) || _session?.Api == null) return;
        if (_watchWindows.TryGetValue(lobbyId, out var open))
        {
            open.Activate();
            return;
        }

        var modName = string.IsNullOrEmpty(modId) ? "" : ResolveModDisplayName(modId!);
        var api = _session.Api;
        async Task<MatchWatchModel> Refresh()
        {
            try
            {
                var live = await api.GetLobbyLiveAsync(lobbyId);
                IReadOnlyList<LobbyLiveChatLine>? chat = null;
                if (organiserChat)
                {
                    try { chat = (await api.GetLobbyLiveChatAsync(lobbyId)).Chat; }
                    catch (LobbyApiException ex) when (ex.Status == 404) { chat = null; }
                }
                return MatchWatchView.Build(live, gone: false, modName, DateTime.UtcNow, chat, title);
            }
            catch (LobbyApiException ex) when (ex.Status == 404)
            {
                return MatchWatchView.Build(null, gone: true, modName, DateTime.UtcNow, null, title);
            }
        }

        Action<string, string>? decide = null;
        if (tournament != null && match != null
            && TournamentPermissions.CanAwardMatch(tournament, TournamentViewerId(tournament), match))
            decide = (winner, name) => { _ = ConfirmAwardAsync(tournament, match, winner, name); };
        var w = new MatchWatchWindow(MatchWatchView.Build(null, false, modName, DateTime.UtcNow, null, title), Refresh,
            tournament, match, decide)
        {
            LobbyId = lobbyId,
        };
        try { w.Owner = Window.GetWindow(this); } catch { /* off-tree */ }
        _watchWindows[lobbyId] = w;
        w.Closed += (_, _) => _watchWindows.Remove(lobbyId);
        DiagnosticLog.Write($"Match watch opened for room {lobbyId}{(organiserChat ? " (organiser)" : "")}.");
        w.Show();
    }
}
