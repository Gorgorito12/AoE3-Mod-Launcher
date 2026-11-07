using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal dialog to create a new multiplayer room (design 88), the twin of the new-tournament
/// window: the window of the chosen mod, the room type first, and one primary button.
///
/// The mod-combined-hash for the picked profile is computed on the fly by an async callback
/// supplied by the caller (so the dialog stays dumb about how mods are hashed on disk). The hash
/// is what the join-side check compares against: if a peer's local files do not match this exact
/// value, the join is rejected with <c>mod_mismatch</c>.
/// </summary>
public partial class CreateLobbyDialog : Window
{
    private enum FingerprintState { Ok, Loading, Failed }

    /// <summary>The narrowest window the two columns fit in; below it they stack, at 560.</summary>
    private const double TwoColumnWidth = 760, OneColumnWidth = 560;

    /// <summary>Room-size bounds the seat row renders (the server caps within it).</summary>
    private const int MinRoomPlayers = 2, MaxRoomPlayers = 8;

    /// <summary>The seats a casual room opens with.</summary>
    private const int DefaultSeats = 4;

    /// <summary>The most seats a room may hand to watchers. Mirrors the server's own cap, which
    /// is what actually enforces it: a caster and a co-caster.</summary>
    private const int MaxObservers = 2;

    /// <summary>The kinds of room, in the order the row renders them.</summary>
    private static readonly RoomFormat[] Kinds =
        { RoomFormat.Casual, RoomFormat.OneVOne, RoomFormat.TwoVTwo, RoomFormat.ThreeVThree };

    private readonly MultiplayerSession _session;
    private readonly Func<ModProfile, Task<string>> _computeHash;
    private readonly IReadOnlyList<ModProfile> _profiles;
    private readonly LauncherConfig? _config;
    private readonly Action? _openRadminHelp;

    /// <summary>
    /// The server's per-room player cap. Nothing passes it today, so it is the default 8, which
    /// is also the backend's own <c>LOBBY_MAX_PLAYERS</c> default.
    /// </summary>
    private readonly int _lobbyMaxPlayers;
    private readonly Func<ModProfile, ModCopyInfo> _resolveCopyInfo;
    private readonly Func<string, Task> _switchActiveCopy;

    /// <summary>
    /// Whether the observer row is offered at all: developer mode, and nothing else.
    ///
    /// <para><b>Shut on purpose.</b> An observer only starts with no town centre on a map whose
    /// script seats them in the extra slot; on any other map they arrive as an ordinary player and
    /// the match is uneven for everybody else, which nobody notices until it is over.</para>
    /// </summary>
    private readonly bool _observersUnlocked;

    private ModProfile? _selectedProfile;
    private string _selectedHash = "";
    private FingerprintState _fingerprint = FingerprintState.Loading;
    private Color _accent = Color.FromRgb(0x2F, 0x7F, 0xE0);
    private Popup? _modPopup;
    private bool _busy;

    /// <summary>The whole decision: casual, or one of the three competitive formats.</summary>
    private RoomFormat _kind = RoomFormat.Casual;

    /// <summary>A casual room's size. Kept while a format owns the size, so going back to Casual
    /// finds the seats the host chose.</summary>
    private int _casualSeats;

    /// <summary>Seats for watching. A casual room takes them out of its seats; a competitive one
    /// grows to fit them, since its format owns the seats that are played in, and the server reads
    /// the format back off <c>max_players - spectator_slots</c>.</summary>
    private int _spectatorSlots;

    /// <summary>True while the password is shown in clear, so the two controls stay in sync.</summary>
    private bool _passwordRevealed;

    /// <summary>Set when the dialog returns DialogResult=true.</summary>
    public CreateLobbyResponse? CreatedLobby { get; private set; }

    /// <summary>The mod selected when the dialog closed with DialogResult=true: the response only
    /// carries the lobby id + status, not the mod.</summary>
    public ModProfile? CreatedLobbyProfile { get; private set; }

    /// <summary>The room title + max-players chosen, which the response does not echo.</summary>
    public string? CreatedLobbyTitle { get; private set; }
    public int CreatedLobbyMaxPlayers { get; private set; }

    /// <summary>Whether the room has a password: the response does not echo it and GET /lobbies
    /// excludes your own room, so a host has no other way to learn it.</summary>
    public bool CreatedLobbyIsPrivate { get; private set; }

    /// <summary>
    /// Whether the room the server actually created is competitive. <b>Taken from the RESPONSE,
    /// never from the selection:</b> the server can still refuse a competitive room (a size no
    /// format names, a mod its operator left off the ranked list) and create a casual one.
    /// </summary>
    public bool CreatedLobbyIsCompetitive { get; private set; }

    /// <summary>The seats the server gave to watchers. It clamps the request, so this is read back
    /// rather than remembered.</summary>
    public int CreatedLobbySpectatorSlots { get; private set; }

    /// <summary>True when the host asked for a competitive room and the server made a casual one.
    /// The caller says so in the room, where they will be looking.</summary>
    public bool CreatedLobbyCompetitiveDowngraded { get; private set; }

    /// <summary>The format the room was created for, or <c>Casual</c> when it is not competitive,
    /// derived from what the SERVER made (see <see cref="RoomFormats"/>).</summary>
    public RoomFormat CreatedLobbyFormat { get; private set; }

    /// <summary>
    /// Set when the server refused the room because this build is too old for multiplayer.
    /// The dialog closes on that answer; the tab shows the notice and offers the update.
    /// </summary>
    public LobbyApiException? RefusedAsTooOld { get; private set; }

    public CreateLobbyDialog(
        MultiplayerSession session,
        IReadOnlyList<ModProfile> profiles,
        ModProfile? initiallySelected,
        Func<ModProfile, Task<string>> computeHash,
        Func<ModProfile, ModCopyInfo> resolveCopyInfo,
        Func<string, Task> switchActiveCopy,
        int lobbyMaxPlayers = 8,
        bool developerMode = false,
        LauncherConfig? config = null,
        Action? openRadminHelp = null)
    {
        InitializeComponent();
        _session = session;
        _computeHash = computeHash;
        _resolveCopyInfo = resolveCopyInfo;
        _switchActiveCopy = switchActiveCopy;
        _profiles = profiles;
        _lobbyMaxPlayers = lobbyMaxPlayers;
        _observersUnlocked = developerMode;
        _config = config;
        _openRadminHelp = openRadminHelp;
        _casualSeats = Math.Min(DefaultSeats, Math.Min(lobbyMaxPlayers, MaxRoomPlayers));

        ApplyStrings();
        ModDialogChrome.SetTightLine(1, KindLabel, ModLabel, CopyLabel, TitleLabel, MaxPlayersLabel, ObserversLabel, TitleCounter);
        ModDialogChrome.SetTightLine(1.45, KindHint, ObserversHint, PrivateHint, NetBody);
        ModDialogChrome.SetTightLine(1.3, PrivateTitleText, NetTitle);
        FitToScreen();
        BuildRows();
        ObserversBlock.Visibility = _observersUnlocked ? Visibility.Visible : Visibility.Collapsed;
        ModChevron.Visibility = profiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ModPickButton.IsHitTestVisible = profiles.Count > 1;

        var last = RoomFormats.FromKindToken(config?.LastRoomKind);
        _kind = RoomFormats.PlayersFor(last) <= lobbyMaxPlayers ? last : RoomFormat.Casual;
        KindRow.Selected = _kind;

        PaintNetwork();
        var first = profiles.FirstOrDefault(p => string.Equals(p.Id, initiallySelected?.Id, StringComparison.OrdinalIgnoreCase))
                    ?? profiles.FirstOrDefault();
        if (first != null) PickMod(first);
        Refresh();
        RoomTitleBox.Focus();

        FrameworkElement[] cards = { KindCard, ModCard, PasswordCard, SeatsCard, NetCard };
        void Frost() => ModDialogChrome.FrostCards(Root, cards);
        foreach (var card in cards) card.SizeChanged += (_, _) => Frost();
        Scroller.ScrollChanged += (_, _) => Frost();
        _refrost = Frost;
    }

    private Action? _refrost;

    private void ApplyStrings()
    {
        Title = Strings.Get("MpCreateDialogTitle");
        TitleBarControl.Title = Title;
        KindLabel.Text = Strings.Get("MpCreateDialogKind");
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        CopyLabel.Text = Strings.Get("MpCreateDialogCopyLabel");
        TitleLabel.Text = Strings.Get("MpCreateDialogTitleLabel");
        TitlePlaceholder.Text = Strings.Get("MpCreateDialogNamePlaceholder");
        MaxPlayersLabel.Text = Strings.Get("MpCreateDialogMaxPlayers");
        ObserversLabel.Text = Strings.Get("MpCreateDialogObservers");
        PrivateTitleText.Text = Strings.Get("MpCreateDialogPrivate");
        PrivateHint.Text = Strings.Get("MpCreateDialogPrivateBody");
        PasswordPlaceholder.Text = Strings.Get("MpCreateDialogPasswordPlaceholder");
        PasswordRevealButton.Content = Strings.Get("MpCreateDialogShowPassword");
        SummaryPlayersWord.Text = Strings.Get("MpCreateDialogToPlay");
        NetHelpButton.Content = Strings.Get("MpCreateDialogNetHelp");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
        CreateButton.Content = Strings.Get("MpCreateDialogCreate");
    }

    /// <summary>
    /// With SizeToContent the window has no ceiling of its own, and on a small screen it would put
    /// the footer past the bottom edge. The body row is a star with a ScrollViewer in it, so
    /// clamping here makes the form scroll instead. Read at construction because it depends on the
    /// player's screen.
    /// </summary>
    private void FitToScreen()
    {
        var work = SystemParameters.WorkArea;
        if (work.Height > 200) MaxHeight = work.Height - 40;
        if (work.Width >= TwoColumnWidth + 40) return;

        Width = OneColumnWidth;
        Grid.SetColumnSpan(LeftColumn, 3);
        Grid.SetColumn(RightColumn, 0);
        Grid.SetColumnSpan(RightColumn, 3);
        Grid.SetRow(RightColumn, 1);
        RightColumn.Margin = new Thickness(0, 14, 0, 0);
    }

    private void BuildRows()
    {
        KindRow.SetOptions(Kinds.Select(kind => kind == RoomFormat.Casual
            ? new SegmentOption(kind, Strings.Get("MpCreateDialogKindCasual"), Strings.Get("MpCreateDialogKindNoElo"))
            : new SegmentOption(kind, Strings.Get(RoomFormats.LabelKey(kind)!), Strings.Get("MpCreateDialogKindRanked"), Gold: true))
            .ToList());
        // A format needing more seats than the server allows cannot be created, so it is shown
        // disabled rather than hidden: the row then says what the ceiling is.
        foreach (var button in KindRow.Buttons.Skip(1).Zip(Kinds.Skip(1)))
            button.First.IsEnabled = RoomFormats.PlayersFor(button.Second) <= _lobbyMaxPlayers;
        KindRow.Picked += key => SelectKind((RoomFormat)key);

        MaxPlayersRow.SetOptions(Enumerable.Range(MinRoomPlayers, MaxRoomPlayers - MinRoomPlayers + 1)
            .Select(n => new SegmentOption(n, n.ToString())).ToList());
        foreach (var button in MaxPlayersRow.Buttons.Select((b, i) => (b, Seats: i + MinRoomPlayers)))
            button.b.IsEnabled = button.Seats <= _lobbyMaxPlayers;
        MaxPlayersRow.Selected = _casualSeats;
        MaxPlayersRow.Picked += key => SelectSeats((int)key);

        ObserversRow.Picked += key => SelectSpectatorSlots((int)key);
    }

    /// <summary>Pick the room type: <see cref="RoomFormat.Casual"/> or a competitive format.</summary>
    internal void SelectKind(RoomFormat kind)
    {
        _kind = kind;
        KindRow.Selected = kind;
        // As in the prototype: observers are counted differently on each side of this choice, so
        // what was picked on one does not carry a meaning across.
        _spectatorSlots = 0;
        Refresh();
    }

    internal void SelectSeats(int seats)
    {
        _casualSeats = seats;
        _spectatorSlots = Math.Min(_spectatorSlots, Math.Max(0, seats - MinRoomPlayers));
        MaxPlayersRow.Selected = seats;
        Refresh();
    }

    internal void SelectSpectatorSlots(int slots)
    {
        _spectatorSlots = slots;
        Refresh();
    }

    private bool Competitive => _kind != RoomFormat.Casual;

    /// <summary>The seats the room asks the server for.</summary>
    private int RoomSize => Competitive ? RoomFormats.PlayersFor(_kind) + _spectatorSlots : _casualSeats;

    /// <summary>Repaint everything derived from the choices, so no two of them can disagree.
    /// The fingerprint stays outside: it reports on an async hash, not on a choice.</summary>
    private void Refresh()
    {
        RefreshKindHint();
        RefreshObservers();
        RefreshSummary();
        ProposeRoomTitle();
        RefreshSuggestions();
        RefreshFooter();
    }

    private void RefreshKindHint()
    {
        KindDot.Visibility = Competitive ? Visibility.Visible : Visibility.Collapsed;
        MaxPlayersBlock.Visibility = Competitive ? Visibility.Collapsed : Visibility.Visible;
        KindHint.Text = Strings.Get(Competitive ? "MpCreateDialogKindHintRanked" : "MpCreateDialogKindHintCasual");

        // The two promises a host accepts by choosing this, too long for the line under the row.
        var fine = new List<string>();
        if (RoomFormats.AbandonmentApplies(_kind)) fine.Add(Strings.Get("MpCreateDialogCompetitiveForfeit"));
        if (Competitive) fine.Add(Strings.Get("MpCreateDialogCompetitiveReplay"));
        KindHint.ToolTip = fine.Count == 0 ? null : TooltipHelper.Wrap(string.Join("\n\n", fine));
    }

    private void RefreshObservers()
    {
        if (!_observersUnlocked) return;

        var seats = RoomFormats.PlayersFor(_kind);
        var options = Enumerable.Range(0, MaxObservers + 1)
            .Where(n => Competitive
                ? seats + n <= _lobbyMaxPlayers && seats + n <= MaxRoomPlayers
                : n == 0 || n < _casualSeats - 1)
            .ToList();
        if (ObserversRow.Children.Count != options.Count)
            ObserversRow.SetOptions(options.Select(n => new SegmentOption(n, n.ToString())).ToList());
        ObserversRow.Selected = _spectatorSlots;

        var playing = Math.Max(0, RoomSize - _spectatorSlots);
        ObserversHint.ToolTip = TooltipHelper.Wrap(Strings.Get("MpCreateDialogObserversMaps"));
        ObserversHint.Text = _spectatorSlots == 0
            ? Strings.Get("MpCreateDialogObserversHint")
            : Competitive
                ? Strings.Format("MpCreateDialogObserversSeats", seats)
                : Strings.Format(_spectatorSlots == 1 ? "MpCreateDialogObserversLeaveOne" : "MpCreateDialogObserversLeave",
                                 _spectatorSlots, playing);
    }

    private void RefreshSummary()
    {
        SummaryPlayers.Text = Math.Max(0, RoomSize - _spectatorSlots).ToString();
        SummaryObservers.Text = _spectatorSlots.ToString();
        SummaryObserversWord.Text = Strings.Get(_spectatorSlots == 1 ? "MpCreateDialogObserverOne" : "MpCreateDialogObserverMany");
        SummaryObserversBlock.Visibility = _spectatorSlots > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Offer a room title for the room as it stands, and refuse to touch one somebody wrote:
    /// <see cref="RoomTitleProposal.IsOurs"/> owns that, and its remarks say why getting it wrong
    /// is invisible. The proposal is not localized because a title is a network value.
    /// </summary>
    private void ProposeRoomTitle()
    {
        if (_selectedProfile == null) return;
        var current = RoomTitleBox.Text?.Trim() ?? "";
        if (!RoomTitleProposal.IsOurs(current, _profiles.Select(p => p.DisplayName), RoomTitleBox.MaxLength))
            return;
        var proposed = RoomTitleProposal.Propose(_selectedProfile.DisplayName, Competitive, _kind, RoomTitleBox.MaxLength);
        // Guarded: assigning Text moves the caret to the start, which would fight the host's typing.
        if (proposed != RoomTitleBox.Text) RoomTitleBox.Text = proposed;
    }

    private void RefreshSuggestions()
    {
        var name = _selectedProfile?.DisplayName ?? "";
        Suggest1.Content = RoomTitleProposal.Propose(name, false, RoomFormat.Casual, RoomTitleBox.MaxLength);
        Suggest2.Content = Competitive
            ? RoomTitleProposal.Propose(name, true, _kind, RoomTitleBox.MaxLength)
            : Strings.Get("MpCreateDialogSuggestNight");
    }

    /// <summary>A suggestion IS the title, unlike the pills of the old dialog, which added to it.</summary>
    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string suggestion }) return;
        RoomTitleBox.Text = suggestion;
        RoomTitleBox.CaretIndex = suggestion.Length;
        RoomTitleBox.Focus();
    }

    /// <summary>What stops the room from being created, first thing first, so the footer says it
    /// before the click rather than after.</summary>
    private string? CurrentError()
    {
        if (_fingerprint == FingerprintState.Failed) return Strings.Get("MpCreateDialogNoFingerprint");
        if ((RoomTitleBox.Text?.Trim().Length ?? 0) < 3) return Strings.Get("MpCreateDialogTitleTooShort");
        if (PrivateRoomCheck.IsChecked == true && CurrentPassword().Length == 0)
            return Strings.Get("MpCreateDialogNoPassword");
        return null;
    }

    private void RefreshFooter()
    {
        var error = CurrentError();
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        CreateButton.IsEnabled = error == null && _fingerprint == FingerprintState.Ok && !_busy;
        CreateButton.ToolTip = error == null ? null : TooltipHelper.Wrap(error);

        var tooShort = (RoomTitleBox.Text?.Trim().Length ?? 0) < 3;
        if (tooShort) TitleField.BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0xF4, 0x90, 0x0C));
        else TitleField.ClearValue(Border.BorderBrushProperty);
    }

    private void RoomTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TitleCounter == null) return;
        var length = RoomTitleBox.Text?.Length ?? 0;
        TitleCounter.Text = $"{length} / {RoomTitleBox.MaxLength}";
        TitlePlaceholder.Visibility = length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsInitialized && ErrorText != null) RefreshFooter();
    }

    // ------------------------------------------------------------------- the mod

    private void ModPickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count < 2) return;
        if (ChromePopups.ConsumeToggleOff(ModPickButton)) return;
        if (_modPopup != null)
        {
            _modPopup.IsOpen = false;
            return;
        }

        var rows = new StackPanel { Width = ModPickButton.ActualWidth - 14 };
        var head = new TextBlock { Text = Strings.Get("MpCreateDialogModsTitle"), FontWeight = FontWeights.Bold, Margin = new Thickness(10, 8, 10, 4) };
        head.SetResourceReference(TextBlock.FontSizeProperty, "SetGroupLabelSize");
        head.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        rows.Children.Add(head);
        foreach (var profile in _profiles) rows.Children.Add(BuildModRow(profile));
        var foot = new TextBlock { Text = Strings.Get("MpCreateDialogModsFoot"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 6, 10, 4) };
        foot.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
        foot.SetResourceReference(TextBlock.ForegroundProperty, "WindowFineText");
        rows.Children.Add(foot);

        var card = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Child = rows,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 36, ShadowDepth = 14, Direction = 270, Opacity = .45, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "LibraryMenuBg");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        var popup = new Popup
        {
            Child = card,
            PlacementTarget = ModPickButton,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            StaysOpen = false,
            AllowsTransparency = true,
        };
        ChromePopups.Track(popup, ModPickButton);
        popup.Closed += (_, _) => _modPopup = null;
        _modPopup = popup;
        popup.IsOpen = true;
    }

    private Button BuildModRow(ModProfile profile)
    {
        var icon = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = (Brush)FindResource("WindowSecondary"),
            Child = new Border { CornerRadius = new CornerRadius(5), Background = IconOf(profile) },
        };
        var name = new TextBlock { Text = profile.DisplayName, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        name.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        name.SetResourceReference(TextBlock.ForegroundProperty, "UiHeadText");
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        line.Children.Add(icon);
        line.Children.Add(name);
        var row = new Button { Content = line, Height = 36 };
        row.SetResourceReference(StyleProperty, "MenuRowButton");
        if (ReferenceEquals(profile, _selectedProfile))
            row.Background = new SolidColorBrush(ModAccent.SelectionFill(_accent));
        row.Click += (_, _) =>
        {
            if (_modPopup != null) _modPopup.IsOpen = false;
            PickMod(profile);
        };
        return row;
    }

    private static Brush? IconOf(ModProfile profile)
        => MainWindow.TryLoadTileImage(profile.ResolveIconSource(), MainWindow.IconDecodeWidth);

    /// <summary>Everything that follows the mod: its window, its copies and its fingerprint.
    /// Create waits for the hash, so a fast click cannot submit a stale value.</summary>
    private async void PickMod(ModProfile profile)
    {
        _selectedProfile = profile;
        ModNameText.Text = profile.DisplayName;
        ModIconHost.Background = IconOf(profile);
        _accent = ModDialogChrome.AttachPicked(this, TitleBarControl, Root, profile, Width);
        _refrost?.Invoke();
        KindRow.Accent = _accent;
        MaxPlayersRow.Accent = _accent;
        ObserversRow.Accent = _accent;
        CreateButton.Foreground = new SolidColorBrush(ModAccent.OnAccent(_accent));
        RefreshCopyRow(profile);
        Refresh();
        await ComputeHashAsync(profile);
    }

    private async Task ComputeHashAsync(ModProfile profile)
    {
        SetFingerprintState(FingerprintState.Loading, null);
        try
        {
            var hash = await _computeHash(profile);
            if (!ReferenceEquals(profile, _selectedProfile)) return;
            _selectedHash = hash;
            SetFingerprintState(FingerprintState.Ok, hash);
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(profile, _selectedProfile)) return;
            _selectedHash = "";
            SetFingerprintState(FingerprintState.Failed, null);
            DiagnosticLog.Write($"CreateLobbyDialog: fingerprint failed: {ex.Message}");
        }
    }

    private void SetFingerprintState(FingerprintState state, string? hash)
    {
        _fingerprint = state;
        var (text, dot, line) = state switch
        {
            FingerprintState.Ok => ("WindowMuted", "UiOk", Strings.Format("MpCreateDialogFingerprintOk", ShortHash(hash))),
            FingerprintState.Loading => ("WindowMuted", "UiInfo", Strings.Get("MpCreateDialogFingerprintLoading")),
            _ => ("UiErrorText", "UiError", Strings.Get("MpCreateDialogFingerprintFailed")),
        };
        FingerprintText.Text = line;
        FingerprintText.SetResourceReference(TextBlock.ForegroundProperty, text);
        FingerprintDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, dot);
        FingerprintText.ToolTip = string.IsNullOrEmpty(hash)
            ? null
            : TooltipHelper.Wrap(Strings.Get("MpCreateDialogHashLabel") + ": " + hash);
        RefreshFooter();
    }

    /// <summary>Enough for two players to compare over voice, which is all anybody does with it.</summary>
    internal static string ShortHash(string? hash)
        => string.IsNullOrEmpty(hash) ? "" : hash!.Length <= 8 ? hash : hash[..4] + "…" + hash[^4..];

    /// <summary>
    /// The copy row, for a mod with 2+ copies. Multiplayer launches and fingerprints the ACTIVE
    /// copy, so choosing one rotates the active copy, which only the dashboard's mod can do: for
    /// any other mod the row only says which copy will be used.
    /// </summary>
    private void RefreshCopyRow(ModProfile profile)
    {
        var info = _resolveCopyInfo?.Invoke(profile);
        if (info == null || !info.HasMultiple)
        {
            CopyRow.Visibility = Visibility.Collapsed;
            return;
        }

        CopyPills.Children.Clear();
        foreach (var copy in info.Copies)
        {
            var label = new TextBlock { Text = copy.Label, Margin = new Thickness(12, 0, 12, 0), FontWeight = FontWeights.SemiBold };
            label.SetResourceReference(TextBlock.FontSizeProperty, "SetRowDescSize");
            label.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont");
            var pill = new Button { Content = label, Height = 34, Margin = new Thickness(0, 0, 6, 6) };
            pill.SetResourceReference(StyleProperty, "SegmentButton");
            SegmentRow.Paint(pill, copy.IsActive, false, _accent);
            label.Foreground = pill.Foreground;
            if (info.CanSwitch)
            {
                var id = copy.InstallId;
                if (!copy.IsActive) pill.Click += async (_, _) => await SwitchCopyAsync(id);
            }
            else
            {
                pill.IsHitTestVisible = false;
                pill.ToolTip = TooltipHelper.Wrap(Strings.Get("MpCreateDialogCopyHintReadonly"));
            }
            CopyPills.Children.Add(pill);
        }
        CopyRow.Visibility = Visibility.Visible;
    }

    /// <summary>Rotates the active copy (the single source of truth) and recomputes the
    /// fingerprint, so the room's required hash matches what will actually launch.</summary>
    private async Task SwitchCopyAsync(string installId)
    {
        if (_selectedProfile is not { } profile) return;
        CopyPills.IsEnabled = false;
        SetFingerprintState(FingerprintState.Loading, null);
        try
        {
            await _switchActiveCopy(installId);
            await ComputeHashAsync(profile);
        }
        catch (Exception ex)
        {
            _selectedHash = "";
            SetFingerprintState(FingerprintState.Failed, null);
            DiagnosticLog.Write($"CreateLobbyDialog: copy switch failed: {ex.Message}");
        }
        CopyPills.IsEnabled = true;
        RefreshCopyRow(profile);
    }

    // ---------------------------------------------------------------- the password

    /// <summary>A PasswordBox cannot show its own value, so the reveal swaps in a plain TextBox;
    /// whichever is visible is copied back into the other.</summary>
    private void PasswordReveal_Click(object sender, RoutedEventArgs e)
    {
        _passwordRevealed = !_passwordRevealed;
        if (_passwordRevealed)
        {
            PasswordPlainBox.Text = PasswordBox.Password;
            PasswordPlainBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlainBox.Focus();
            PasswordPlainBox.CaretIndex = PasswordPlainBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordPlainBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordPlainBox.Visibility = Visibility.Collapsed;
            PasswordBox.Focus();
        }
        PasswordRevealButton.Content = Strings.Get(_passwordRevealed
            ? "MpCreateDialogHidePassword"
            : "MpCreateDialogShowPassword");
    }

    /// <summary>The password as typed, from whichever of the two controls is showing.</summary>
    private string CurrentPassword() => _passwordRevealed ? PasswordPlainBox.Text : PasswordBox.Password;

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => PasswordChanged();

    private void PasswordPlainBox_TextChanged(object sender, TextChangedEventArgs e) => PasswordChanged();

    private void PasswordChanged()
    {
        PasswordPlaceholder.Visibility = CurrentPassword().Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshFooter();
    }

    /// <summary>Unticking clears both boxes, so an old value cannot leak into the request or sit
    /// in the plain box the next time the row appears.</summary>
    private void PrivateRoomCheck_Changed(object sender, RoutedEventArgs e)
    {
        var isPrivate = PrivateRoomCheck.IsChecked == true;
        PasswordRow.Visibility = isPrivate ? Visibility.Visible : Visibility.Collapsed;
        if (!isPrivate)
        {
            PasswordBox.Password = "";
            PasswordPlainBox.Text = "";
        }
        else PasswordBox.Focus();
        RefreshFooter();
    }

    // ----------------------------------------------------------------- the network

    /// <summary>INFORMATIONAL, never a gate: creating and joining do not depend on Radmin, and the
    /// launch injects the 26.x address by itself. It is what actually playing needs. Best-effort:
    /// a probe failure reads as not ready, which costs a sentence.</summary>
    private void PaintNetwork()
    {
        string? ip = null;
        var ready = false;
        try
        {
            var status = RadminVpnService.GetStatus();
            ready = status.IsServiceRunning;
            ip = RadminVpnService.TryGetAdapterIp();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: Radmin status probe failed (non-fatal): {ex.Message}");
        }
        PaintNetwork(ready && !string.IsNullOrEmpty(ip), ip);
    }

    internal void PaintNetwork(bool ready, string? ip)
    {
        NetDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ready ? "UiOk" : "UiError");
        NetTitle.SetResourceReference(TextBlock.ForegroundProperty, ready ? "UiHeadText" : "UiErrorText");
        NetTitle.Text = Strings.Get(ready ? "MpCreateDialogNetReady" : "MpCreateDialogNetBad");
        NetBody.Text = ready
            ? Strings.Format("MpCreateDialogNetReadyBody", ip ?? "")
            : Strings.Get("MpCreateDialogNetBadBody");
        NetHelpButton.Visibility = !ready && _openRadminHelp != null ? Visibility.Visible : Visibility.Collapsed;
        if (ready) NetCard.ClearValue(Border.BorderBrushProperty);
        else NetCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, 0xF4, 0x90, 0x0C));
    }

    private void NetHelpButton_Click(object sender, RoutedEventArgs e) => _openRadminHelp?.Invoke();

    // ------------------------------------------------------------------- the footer

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile == null || string.IsNullOrEmpty(_selectedHash) || CurrentError() != null) return;

        var title = RoomTitleBox.Text.Trim();
        var maxPlayers = Math.Min(_lobbyMaxPlayers, RoomSize);

        _busy = true;
        RefreshFooter();
        CancelButton.IsEnabled = false;
        // The busy caption, not just the disabled state: going inert is what a button does when
        // there is nothing to do, so on its own it says the opposite of what is happening.
        CreateButton.Content = Strings.Get("MpCreateDialogCreating");
        CreateButton.Opacity = 1;

        try
        {
            var password = PrivateRoomCheck.IsChecked == true ? CurrentPassword() : null;
            CreatedLobbyProfile = _selectedProfile;
            CreatedLobbyTitle = title;
            CreatedLobbyMaxPlayers = maxPlayers;
            CreatedLobbyIsPrivate = !string.IsNullOrEmpty(password);
            CreatedLobby = await _session.Api.CreateLobbyAsync(new CreateLobbyRequest
            {
                Title = title,
                ModId = _selectedProfile.Id,
                ModCombinedHash = _selectedHash,
                MaxPlayers = maxPlayers,
                SpectatorSlots = _spectatorSlots,
                Password = password,
                Competitive = Competitive,
            });
            // The server's answer, not ours. An older backend does not send the field at all,
            // which deserialises to false: a casual room, which is how it will treat the match.
            CreatedLobbyIsCompetitive = CreatedLobby?.Competitive == true;
            CreatedLobbySpectatorSlots = CreatedLobby?.SpectatorSlots ?? 0;
            CreatedLobbyCompetitiveDowngraded = Competitive && !CreatedLobbyIsCompetitive;
            CreatedLobbyFormat = RoomFormats.Resolve(CreatedLobbyIsCompetitive, maxPlayers, CreatedLobbySpectatorSlots);
            if (_config != null)
            {
                _config.LastRoomKind = RoomFormats.KindToken(_kind);
                _config.Save();
            }
            DialogResult = true;
            Close();
        }
        catch (LobbyApiException ex) when (IsLauncherTooOld(ex))
        {
            // Not an error strip: nothing typed into this dialog can fix an old build. The dialog
            // closes and hands the refusal to the tab, which explains it and offers the update.
            DiagnosticLog.Write($"CreateLobbyDialog: refused, launcher too old ({ex.Message}).");
            RefusedAsTooOld = ex;
            DialogResult = false;
            Close();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: {ex.GetType().Name}: {ex.Message}");
            _busy = false;
            CreateButton.ClearValue(OpacityProperty);
            CreateButton.Content = Strings.Get("MpCreateDialogCreate");
            CancelButton.IsEnabled = true;
            RefreshFooter();
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The server's "this launcher is too old" refusal, by its code: the message is
    /// bilingual prose and the status is a 426 only on a backend new enough to send one.</summary>
    internal static bool IsLauncherTooOld(LobbyApiException ex)
        => ex != null && string.Equals(ex.Code, "launcher_too_old", StringComparison.OrdinalIgnoreCase);

    /// <summary>A prototype state, for the harness: <c>--preview-dialog=createroom:b</c>.</summary>
    internal void FillPreview(string title, RoomFormat kind, int observers, string password)
    {
        SelectKind(kind);
        if (observers > 0) SelectSpectatorSlots(observers);
        RoomTitleBox.Text = title;
        if (password.Length > 0)
        {
            PrivateRoomCheck.IsChecked = true;
            PasswordBox.Password = password;
        }
    }
}
