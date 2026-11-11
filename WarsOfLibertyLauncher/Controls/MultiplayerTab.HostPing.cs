using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The rooms list's PING column (design 86): the ICMP round trip to each host's Radmin address,
/// which is the link a match is played over. The addresses come from <c>GET /lobbies/radmin</c>;
/// a server without that route keeps the list exactly as it was, with no column — a column of
/// dashes would be worse than none. The rules of the cell are in <see cref="HostPing"/>.
///
/// <para>The work runs only while the Rooms page is in front (the same gate as the other cells),
/// every <see cref="HostPingInterval"/>, off the UI thread, at most <see cref="HostPingMaxRows"/>
/// hosts at a time. A minute's summary goes to the log, because whether hosts usually answer is
/// the fact that decides whether this column earns its place.</para>
/// </summary>
public partial class MultiplayerTab
{
    private static readonly TimeSpan HostPingInterval = TimeSpan.FromSeconds(10);
    private const int HostPingMaxRows = 20;

    /// <summary>Null until the server has been asked; then whether it publishes hosts' addresses.</summary>
    private bool? _hostPingSupported;

    /// <summary>The last addresses the server gave, by lobby id.</summary>
    private Dictionary<string, string> _hostRadminIps = new(StringComparer.Ordinal);

    /// <summary>One host's readings: the last answer and how many probes in a row went unanswered.</summary>
    private sealed class HostPingState
    {
        public int? LastMs;
        public int Fails;
    }

    private readonly Dictionary<string, HostPingState> _hostPings = new(StringComparer.Ordinal);

    /// <summary>The cells on screen, updated in place; emptied whenever the rows are rebuilt.</summary>
    private readonly Dictionary<string, TextBlock> _hostPingCells = new(StringComparer.Ordinal);

    private bool _myRadminKnown;
    private bool _iHaveRadmin;
    private DateTime _hostPingAt = DateTime.MinValue;
    private bool _hostPingInFlight;
    private DateTime _hostPingLogAt = DateTime.MinValue;
    private int _hostPingLogAnswered, _hostPingLogAsked, _hostPingLogNoIp;

    /// <summary>Whether the rows carry a PING column: the sample list always, a real one only when
    /// the server publishes the hosts' addresses.</summary>
    private bool HostPingColumnShown => _demoRooms || _hostPingSupported == true;

    /// <summary>The facts one row's cell is decided from.</summary>
    private HostPingFacts HostPingFactsFor(LobbySummary lobby)
    {
        var published = !lobby.IsPrivate && string.IsNullOrEmpty(lobby.TournamentMatchId);
        if (_demoRooms)
        {
            var ms = RoomListDemoData.HostPingMs(lobby.Id);
            return new HostPingFacts(IsMyRoom(lobby), IHaveRadmin: true, published, HostHasIp: ms.HasValue,
                LastMs: ms is >= 0 ? ms : null, Fails: ms is < 0 ? HostPing.FailsBeforeNoAnswer : 0);
        }
        // Before the first round nothing is known yet: a published room reads as being measured,
        // never as "the host is not on Radmin", which would be a guess.
        if (!_myRadminKnown)
            return new HostPingFacts(IsMyRoom(lobby), IHaveRadmin: true, published, HostHasIp: true, LastMs: null, Fails: 0);
        _hostPings.TryGetValue(lobby.Id, out var st);
        return new HostPingFacts(IsMyRoom(lobby), _iHaveRadmin, published,
            _hostRadminIps.ContainsKey(lobby.Id), st?.LastMs, st?.Fails ?? 0);
    }

    /// <summary>The PING cell of a new row, registered so the measurement can repaint it in place.</summary>
    private FrameworkElement BuildHostPingCell(LobbySummary lobby)
    {
        var tb = new TextBlock
        {
            FontFamily = MonoFont,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBlock.FontSizeProperty, "RmButtonSize");
        var bars = BuildSignalBars();
        tb.Tag = bars;
        var cell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent };
        cell.Children.Add(bars);
        cell.Children.Add(tb);
        FillHostPingCell(tb, HostPingFactsFor(lobby));
        _hostPingCells[lobby.Id] = tb;
        return cell;
    }

    /// <summary>Three bars, 4 / 7 / 10 px, bottom-aligned, in front of the milliseconds.</summary>
    private static StackPanel BuildSignalBars()
    {
        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        foreach (var height in new[] { 4.0, 7.0, 10.0 })
            bars.Children.Add(new Border
            {
                Width = 3,
                Height = height,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        bars.Height = 10;
        return bars;
    }

    /// <summary>How many of the three bars a ping lights: under 100 ms all three, up to 150 two,
    /// slower one; unmeasured none (the bars stay grey beside the dash).</summary>
    internal static int LitBars(HostPingKind kind, int? ms)
        => kind != HostPingKind.Answered || ms is not int v || v < 0 ? 0 : v < 100 ? 3 : v <= 150 ? 2 : 1;

    internal static void FillHostPingCell(TextBlock tb, HostPingFacts facts)
    {
        var kind = HostPing.Classify(facts);
        var colourKey = kind == HostPingKind.Answered ? RoomActionRule.PingBrushKey(facts.LastMs) : "RmPingUnknown";
        tb.Text = HostPing.Text(kind, facts.LastMs);
        tb.Foreground = Rm(colourKey);
        var tip = HostPing.TipKey(kind) is { } key ? TooltipHelper.Wrap(Strings.Get(key)) : null;
        tb.ToolTip = tip;
        if (tb.Tag is StackPanel bars)
        {
            var lit = LitBars(kind, facts.LastMs);
            for (var i = 0; i < bars.Children.Count; i++)
                if (bars.Children[i] is Border bar)
                {
                    bar.Background = Rm(i < lit ? colourKey : "RmPingUnknown");
                    bar.Opacity = i < lit ? 1 : .45;
                }
            if (bars.Parent is FrameworkElement cell) cell.ToolTip = tip;
        }
    }

    /// <summary>Repaint every PING cell on screen from the latest readings, without rebuilding a row.</summary>
    private void RefreshHostPingCells()
    {
        if (_hostPingCells.Count == 0 || _lastBrowserList == null) return;
        foreach (var lobby in _lastBrowserList)
            if (_hostPingCells.TryGetValue(lobby.Id, out var tb))
                FillHostPingCell(tb, HostPingFactsFor(lobby));
    }

    /// <summary>
    /// One round: ask the server for the hosts' addresses, check that this PC is on Radmin, ping the
    /// listed hosts, repaint. Called from the rooms tick, which already runs only while the page is
    /// in front; this keeps its own pace and never overlaps itself.
    /// </summary>
    private async void MaybeMeasureHostPings()
    {
        if (_demoRooms || _hostPingInFlight) return;
        if (_session?.Status != MultiplayerSession.SessionStatus.SignedIn || _offlineMode) return;
        if (DateTime.UtcNow - _hostPingAt < HostPingInterval) return;
        _hostPingInFlight = true;
        _hostPingAt = DateTime.UtcNow;
        try
        {
            Dictionary<string, string>? ips;
            try { ips = await _session.Api.GetHostRadminIpsAsync(); }
            catch (Exception ex)
            {
                // A failed round keeps the last answer: one bad request must not empty the column.
                DiagnosticLog.Write($"Host ping: could not read the hosts' addresses — {ex.Message}");
                return;
            }

            var supported = ips != null;
            if (supported != _hostPingSupported)
            {
                _hostPingSupported = supported;
                DiagnosticLog.Write(supported
                        ? "Host ping: the server publishes the hosts' Radmin addresses; the PING column is on."
                        : "Host ping: this server does not publish the hosts' addresses; no PING column.");
                _roomColumnsApplied = false;
                ApplyRoomColumns();
            }
            if (ips == null) return;
            _hostRadminIps = new Dictionary<string, string>(ips, StringComparer.Ordinal);

            var mine = await Task.Run(RadminVpnService.TryGetAdapterIp);
            _myRadminKnown = true;
            _iHaveRadmin = !string.IsNullOrEmpty(mine);

            var rows = (_lastBrowserList ?? new List<LobbySummary>())
                .Where(l => !IsMyRoom(l))
                .ToList();
            var targets = rows
                .Where(l => _hostRadminIps.ContainsKey(l.Id))
                .Take(HostPingMaxRows)
                .Select(l => (l.Id, Ip: _hostRadminIps[l.Id]))
                .ToList();

            if (_iHaveRadmin && targets.Count > 0)
            {
                var results = await Task.WhenAll(targets.Select(async t => (t.Id, Ms: await PingPeerAsync(t.Ip))));
                foreach (var (id, ms) in results)
                {
                    if (!_hostPings.TryGetValue(id, out var st)) _hostPings[id] = st = new HostPingState();
                    if (ms >= 0) { st.LastMs = ms; st.Fails = 0; _hostPingLogAnswered++; }
                    else { st.LastMs = null; st.Fails++; }
                    _hostPingLogAsked++;
                }
            }
            _hostPingLogNoIp += rows.Count(l => !l.IsPrivate && string.IsNullOrEmpty(l.TournamentMatchId)
                                                && !_hostRadminIps.ContainsKey(l.Id));

            // Rooms that left the list take their readings with them.
            foreach (var gone in _hostPings.Keys.Where(k => !_hostRadminIps.ContainsKey(k)).ToList())
                _hostPings.Remove(gone);

            RefreshHostPingCells();
            LogHostPingMinute();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"Host ping: round failed — {ex.Message}");
        }
        finally
        {
            _hostPingInFlight = false;
        }
    }

    /// <summary>Once a minute while measuring: how many hosts answered. This decides whether the
    /// column is worth keeping, so it is written even when nothing changed.</summary>
    private void LogHostPingMinute()
    {
        if (DateTime.UtcNow - _hostPingLogAt < TimeSpan.FromMinutes(1)) return;
        if (_hostPingLogAt != DateTime.MinValue || _hostPingLogAsked > 0)
            DiagnosticLog.Write($"Host ping: {_hostPingLogAnswered} of {_hostPingLogAsked} probes answered"
                                + $" ({_hostPingLogNoIp} row-rounds without a host address; this PC on Radmin: {_iHaveRadmin}).");
        _hostPingLogAt = DateTime.UtcNow;
        _hostPingLogAnswered = _hostPingLogAsked = _hostPingLogNoIp = 0;
    }
}
