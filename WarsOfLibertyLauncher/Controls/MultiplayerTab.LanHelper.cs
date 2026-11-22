using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher.Controls;

/// <summary>
/// The room's side of the optional LAN helper (<see cref="LanHelperService"/>): on entering a room
/// whose game can use it and does not have it, ask once, and with a yes put it in place. Asked in
/// the room and never at launch: the launch happens when a countdown ends, and a question there
/// would sit over a game that is opening.
///
/// <para>The room also SAYS how the player's game will enter: a card above PLAYERS with a switch
/// (<see cref="RoomLanEntryView"/>, <c>LauncherConfig.LanAutoEntry</c>) that stops the automatic
/// entry without touching a file, or a button that opens <see cref="LanHelperSetupDialog"/>; and a
/// word on every row of the roster, from the <c>set_lan_auto</c> each launcher sends about itself.
/// When the automatic copy fails, the same window offers the copy as administrator and the steps
/// to do it by hand.</para>
/// </summary>
public partial class MultiplayerTab
{
    /// <summary>The room this was last looked at for, so a repaint of the window does not ask again.</summary>
    private string? _lanHelperCheckedLobby;

    private RoomLanAvailability _roomLanEntry;

    /// <summary>Whether the player has a home city for the room's mod (HomeCityPresence).</summary>
    private HomeCityState _roomHomeCity;

    private bool LanAutoEntryOn => _config?.LanAutoEntry ?? true;
    private ModProfile? _roomLanProfile;
    private string? _roomLanExe;

    /// <summary>What this launcher last told the room, so it is said once per change.</summary>
    private bool? _lanAutoSent;

    /// <summary>The one-time question, naming the folder and the file. Also what
    /// <c>--preview-dialog=lanhelper</c> shows.</summary>
    internal static bool AskLanHelper(System.Windows.Window? owner, ModProfile profile, string folder) =>
        ThemedConfirmDialog.Ask(
            owner,
            Strings.Get("LanHelperAskTitle"),
            Strings.Format("LanHelperAskBody", profile.DisplayName),
            Strings.Get("LanHelperAskYes"),
            Strings.Get("LanHelperAskNo"),
            detailLabel: Strings.Get("LanHelperAskDetailLabel"),
            detail: $"{folder}\n  {LanHelperService.DllName}",
            mod: profile);

    /// <summary>The executable the room's game launches from, as the launch resolves it.</summary>
    private (ModProfile? Profile, string? Exe) RoomGameExe()
    {
        if (_config == null) return (null, null);
        var profile = ModRegistry.Find(_currentLobbyModId ?? "");
        if (profile == null) return (null, null);
        var installPath = _config.GetState(profile.Id).InstallPath;
        if (string.IsNullOrEmpty(installPath) && profile.IsStockGame)
            installPath = GameLauncher.FindAoe3InstallRoot(_config);
        return (profile, GameLauncher.Find(_config, installPath, profile, trustConfigCache: false));
    }

    private static RoomLanAvailability EntryFor(LanHelperState state, string? exe) => state switch
    {
        LanHelperState.Installed or LanHelperState.EnabledByMod or LanHelperState.SupersededByMod => RoomLanAvailability.Available,
        // Our file is there, an older build or beside the earlier helper: it enters; the next room tidies up.
        LanHelperState.Outdated => RoomLanAvailability.Available,
        // Only the earlier helper (dinput8.dll and its cfg line), whole: it still enters until it is replaced.
        LanHelperState.Missing when exe != null && LanHelperService.LegacyStillEnters(Path.GetDirectoryName(exe))
            => RoomLanAvailability.Available,
        LanHelperState.Missing => RoomLanAvailability.NotSetUp,
        _ => exe == null ? RoomLanAvailability.Unknown : RoomLanAvailability.CannotSetUp,
    };

    /// <summary>Reads the game's folder again, repaints the card and tells the room if it changed.</summary>
    private void RefreshRoomLanEntry()
    {
        if (_demoRoomSample != null)
        {
            _roomLanEntry = RoomLanAvailability.Available;
            _roomHomeCity = _demoRoomSample.NoHomeCity ? HomeCityState.None : HomeCityState.Present;
            RefreshRoomLanCard();
            return;
        }
        var (profile, exe) = RoomGameExe();
        _roomLanProfile = profile;
        _roomLanExe = exe;
        _roomLanEntry = EntryFor(LanHelperService.Probe(exe), exe);
        _roomHomeCity = HomeCityPresence.For(profile, _config);
        RefreshRoomLanCard();
        TellRoomLanEntry();
        // Our own row carries the word too.
        if (_lobbyWindow != null) RenderRoomMembers();
    }

    private void TellRoomLanEntry()
    {
        if (RoomLanEntryView.EntersByItself(_roomLanEntry, LanAutoEntryOn, _roomHomeCity) is not bool auto
            || _session?.RoomSocket == null) return;
        if (_lanAutoSent == auto) return;
        _lanAutoSent = auto;
        _ = SendLanAutoAsync(auto);
    }

    /// <summary>The card's switch: kept for every room, and told to the room at once.</summary>
    private void SetLanAutoEntry(bool on)
    {
        if (_config == null) return;
        if (_config.LanAutoEntry != on)
        {
            _config.LanAutoEntry = on;
            _config.Save();
            DiagnosticLog.Write($"LanHelper: automatic entry switched {(on ? "on" : "off")} in the room");
        }
        RefreshRoomLanCard();
        TellRoomLanEntry();
        if (_lobbyWindow != null) RenderRoomMembers();
    }

    private async System.Threading.Tasks.Task SendLanAutoAsync(bool auto)
    {
        try { await _session!.RoomSocket!.SendSetLanAutoAsync(auto); }
        catch (Exception ex) { DiagnosticLog.Write($"LanHelper: set_lan_auto failed: {ex.Message}"); _lanAutoSent = null; }
    }

    /// <summary>
    /// The card above PLAYERS; hidden while nothing is known and once there is a result. The
    /// colours are the state's (green on, neutral off, amber when it cannot enter), never the mod's.
    /// </summary>
    private void RefreshRoomLanCard()
    {
        if (_lobbyWindow is not { } w) return;
        // A sample room shows the card as it reads when everything works.
        if (_demoRoomSample != null && _roomLanEntry == RoomLanAvailability.Unknown) _roomLanEntry = RoomLanAvailability.Available;
        if (_demoRoomSample != null) _roomHomeCity = _demoRoomSample.NoHomeCity ? HomeCityState.None : HomeCityState.Present;
        var card = RoomLanEntryView.For(_roomLanEntry, LanAutoEntryOn, _matchPhase == MatchPhase.Lobby, _roomHomeCity);
        if (card.Kind == RoomLanCardKind.Hidden || _roomResult != null)
        {
            w.RoomLanCard.Visibility = Visibility.Collapsed;
            return;
        }
        w.RoomLanCard.Visibility = Visibility.Visible;
        var tone = card.Kind switch
        {
            RoomLanCardKind.On => StateTone.Open,
            RoomLanCardKind.Off => StateTone.Neutral,
            _ => StateTone.Caution,
        };
        var mark = StateColors.Mark(tone);
        var text = StateColors.Text(tone);
        var on = card.Kind == RoomLanCardKind.On;
        static System.Windows.Media.SolidColorBrush Alpha(System.Windows.Media.Color c, byte a) =>
            new(System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B));
        // Design 116: a strip. The state's colour washes it from the left (12 % to 4 %) over the
        // panel's own glass, with a 32 % rim; the tile is 18 %.
        var tint = new System.Windows.Media.LinearGradientBrush(
            System.Windows.Media.Color.FromArgb(0x1F, mark.R, mark.G, mark.B),
            System.Windows.Media.Color.FromArgb(0x0A, mark.R, mark.G, mark.B), 0);
        tint.Freeze();
        w.RoomLanTint.Background = tint;
        w.RoomLanCard.BorderBrush = Alpha(mark, 0x52);
        w.RoomLanIconTile.Background = Alpha(mark, 0x2E);
        w.RoomLanBolt.Fill = new System.Windows.Media.SolidColorBrush(text);
        w.RoomLanTitle.Text = Strings.Get("LanCardTitle");
        w.RoomLanTitle.Foreground = new System.Windows.Media.SolidColorBrush(text);
        // The short sentence on the strip; the full one is a hover away.
        w.RoomLanBody.Text = card.ShortKey == null ? "" : Strings.Get(card.ShortKey);
        w.RoomLanCard.ToolTip = card.BodyKey == null ? null : TooltipHelper.Wrap(Strings.Get(card.BodyKey));

        // The switch's "on" track in the card's own green, not the settings blue.
        w.RoomLanSwitch.Resources["UiToggleOn"] = new System.Windows.Media.SolidColorBrush(StateColors.Mark(StateTone.Open));
        w.RoomLanSwitch.Visibility = card.ShowSwitch ? Visibility.Visible : Visibility.Collapsed;
        w.RoomLanSwitch.IsChecked = on;
        w.RoomLanSwitch.IsEnabled = card.SwitchEnabled;
        w.RoomLanSwitch.ToolTip = TooltipHelper.Wrap(Strings.Get(on ? "LanCardSwitchOffTip" : "LanCardSwitchOnTip"));
        w.RoomLanAction.Visibility = card.ActionKey != null ? Visibility.Visible : Visibility.Collapsed;
        if (card.ActionKey != null) w.RoomLanAction.Content = Strings.Get(card.ActionKey);
    }

    /// <summary>The card's button: the setup window, which also says why when nothing can be done.</summary>
    private void OpenLanSetup()
    {
        if (_demoRoomSample != null || _roomLanProfile == null || _roomLanExe == null) return;
        OpenLanSetup(null);
    }

    private void OpenLanSetup(string? failure)
    {
        if (_roomLanProfile is not { } profile || _roomLanExe is not { } exe) return;
        var active = LanHelperSetupDialog.ShowFor(_lobbyWindow, profile, exe, failure);
        if (active && _config != null && LanHelperService.Probe(exe) == LanHelperState.Installed)
        {
            // Done through the window: the player wants it, and it is ours to remove later. Setting
            // it up is also asking for it, so the room's switch comes on with it.
            _config.LanHelperChoice = true;
            _config.LanAutoEntry = true;
            var folder = Path.GetDirectoryName(exe)!;
            if (!_config.LanHelperFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
                _config.LanHelperFolders.Add(folder);
            _config.Save();
        }
        RefreshRoomLanEntry();
    }

    /// <summary>A word for the roster row: how this member enters the LAN room; null when unknown.</summary>
    private string? LanMark(RoomMemberEntry m, bool isMe)
    {
        bool? auto = isMe ? RoomLanEntryView.EntersByItself(_roomLanEntry, LanAutoEntryOn, _roomHomeCity) : m.LanAuto;
        return auto switch { true => Strings.Get("RoomLanAuto"), false => Strings.Get("RoomLanManual"), _ => null };
    }

    private void HandleMemberLanAuto(JsonElement json)
    {
        var userId = json.TryGetProperty("user_id", out var u) ? u.GetString() : null;
        if (string.IsNullOrEmpty(userId) || !json.TryGetProperty("lan_auto", out var a)
            || a.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        if (_roomMembers.TryGetValue(userId, out var entry) && entry.LanAuto != a.GetBoolean())
        {
            entry.LanAuto = a.GetBoolean();
            RenderRoomMembers();
        }
    }

    private void MaybeOfferLanHelper()
    {
        try
        {
            var lobbyId = _session?.CurrentLobbyId;
            if (_config == null || _lobbyWindow == null || lobbyId == null || _demoRoomSample != null) return;
            if (_matchPhase != MatchPhase.Lobby || _lanHelperCheckedLobby == lobbyId) return;
            var profile = ModRegistry.Find(_currentLobbyModId ?? "");
            if (profile == null) return;
            _lanHelperCheckedLobby = lobbyId;
            _lanAutoSent = null;

            var (_, exe) = RoomGameExe();
            var state = LanHelperService.Probe(exe);
            DiagnosticLog.Write($"LanHelper: room mod '{profile.Id}', exe '{exe}', state {state}, choice {_config.LanHelperChoice?.ToString() ?? "never asked"}");
            if (exe == null) { RefreshRoomLanEntry(); return; }
            var folder = Path.GetDirectoryName(exe)!;

            // The mod now ships the key itself (its patch brought things-new's DLL): ours steps
            // aside whatever the player chose, or both would run.
            if (state == LanHelperState.SupersededByMod)
            {
                if (LanHelperService.Remove(folder))
                {
                    _config.LanHelperFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                    _config.Save();
                }
                RefreshRoomLanEntry();
                return;
            }
            // Switched off in a room: no question and no copy; the card offers to set it up.
            if (_config.LanHelperChoice == false || !_config.LanAutoEntry
                || state is not (LanHelperState.Missing or LanHelperState.Outdated))
            {
                RefreshRoomLanEntry();
                return;
            }
            if (_config.LanHelperChoice == null)
            {
                var yes = AskLanHelper(_lobbyWindow, profile, folder);
                _config.LanHelperChoice = yes;
                _config.Save();
                if (!yes) { RefreshRoomLanEntry(); return; }
            }

            // Not writable, or the copy failed: the window with the administrator copy and the
            // steps by hand, instead of a notice that left the player nothing to do.
            RefreshRoomLanEntry();
            if (!ElevationService.CanWriteTo(folder))
            {
                OpenLanSetup(null);
                return;
            }
            var problem = LanHelperService.Install(exe);
            if (problem != null)
            {
                OpenLanSetup(problem);
                return;
            }
            if (!_config.LanHelperFolders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
            {
                _config.LanHelperFolders.Add(folder);
                _config.Save();
            }
            AppendChatSystem(Strings.Format("LanHelperInstalled", profile.DisplayName));
            RefreshRoomLanEntry();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"LanHelper: offer failed: {ex.Message}");
        }
    }
}
