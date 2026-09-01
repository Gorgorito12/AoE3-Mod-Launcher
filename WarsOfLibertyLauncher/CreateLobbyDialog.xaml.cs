using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal dialog to create a new multiplayer room.
///
/// Layout: the host picks the **mod** from a dropdown of installed
/// profiles, then sets a room title, max players and optional password.
/// The mod-combined-hash for the picked profile is computed on the fly
/// by an async callback supplied by the caller (so the dialog stays
/// dumb about how mods are hashed on disk).
///
/// The hash shown under the dropdown is what the join-side check
/// compares against — if a peer's local files don't match this exact
/// value, the join is rejected with <c>mod_mismatch</c>.
/// </summary>
public partial class CreateLobbyDialog : Window
{
    private readonly MultiplayerSession _session;
    private readonly Func<ModProfile, Task<string>> _computeHash;
    private readonly IReadOnlyList<ModProfile> _profiles;
    /// <summary>
    /// The server's per-room player cap. Nothing passes it today, so it is the default
    /// 8 — which is also the backend's own <c>LOBBY_MAX_PLAYERS</c> default, so the
    /// segmented row is right unless that deployment value is changed. The real figure
    /// IS on the wire (<c>ServerConfig.LobbyMaxPlayers</c>, from the sign-in response)
    /// but nothing in the launcher reads that object yet; wiring it is a separate change
    /// from this redesign, and guessing a lower number here would hide rooms sizes the
    /// server would have accepted.
    /// </summary>
    private readonly int _lobbyMaxPlayers;
    private readonly Func<ModProfile, ModCopyInfo> _resolveCopyInfo;
    private readonly Func<string, Task> _switchActiveCopy;
    // Guards the copy combo's SelectionChanged while we repopulate it, so a
    // programmatic rebuild doesn't re-trigger an active-copy switch.
    private bool _suppressCopyCombo;

    private ModProfile? _selectedProfile;
    private string _selectedHash = "";

    /// <summary>Room-size bounds the segmented row renders (the server caps within it).</summary>
    private const int MinRoomPlayers = 2;
    private const int MaxRoomPlayers = 8;

    private readonly Dictionary<int, Button> _maxPlayerButtons = new();
    private int _maxPlayers = MaxRoomPlayers;

    /// <summary>The three competitive formats, in the order the row renders them.</summary>
    private static readonly RoomFormat[] Formats =
        { RoomFormat.OneVOne, RoomFormat.TwoVTwo, RoomFormat.ThreeVThree };

    private readonly Dictionary<RoomFormat, Button> _formatButtons = new();

    /// <summary>
    /// The format chosen while the competitive box is ticked.
    ///
    /// <para>Kept while unticked too, so re-ticking restores what the host picked rather than
    /// snapping back to 1v1 — the room size follows this, and a reset would silently resize
    /// their room under them.</para>
    /// </summary>
    private RoomFormat _format = RoomFormat.OneVOne;

    /// <summary>True while the password is shown in clear, so the two controls stay in sync.</summary>
    private bool _passwordRevealed;

    /// <summary>Set when the dialog returns DialogResult=true.</summary>
    public CreateLobbyResponse? CreatedLobby { get; private set; }

    /// <summary>
    /// The mod profile that was selected when the dialog closed
    /// with DialogResult=true. Exposed so the caller can stamp the
    /// room's mod id (CreateLobbyResponse only carries the lobby
    /// id + status, not the mod). Used by MultiplayerTab to make
    /// sure the right AoE3 install launches when the game starts.
    /// </summary>
    public ModProfile? CreatedLobbyProfile { get; private set; }

    /// <summary>
    /// The room title + max-players the user chose, exposed because
    /// <see cref="CreateLobbyResponse"/> only echoes id + status — the
    /// caller needs these to populate the live room header (title) and
    /// the players-list capacity / open-slot rows (max players).
    /// </summary>
    public string? CreatedLobbyTitle { get; private set; }
    public int CreatedLobbyMaxPlayers { get; private set; }

    /// <summary>
    /// Whether the user ticked "private room". Exposed for the same reason as the two above —
    /// the response does not echo it — and needed because GET /lobbies excludes your own room,
    /// so a host has no other way to learn it. Without this the host of a private room was
    /// shown "Password: none" in their own room.
    /// </summary>
    public bool CreatedLobbyIsPrivate { get; private set; }

    /// <summary>
    /// Whether the room the server actually created is competitive.
    ///
    /// <para><b>Taken from the RESPONSE, never from the checkbox.</b> The server refuses a
    /// competitive room for a mod with no ladder and creates a casual one instead, and it is the
    /// only side that knows which mods have one. Reading the tick box here would make the whole
    /// launcher — the badge, the Record Game confirm, the hold on leaving — act on a promise the
    /// server declined to keep.</para>
    /// </summary>
    public bool CreatedLobbyIsCompetitive { get; private set; }

    /// <summary>
    /// True when the host asked for a competitive room and the server made a casual one.
    ///
    /// <para>Silently downgrading would be the worst of both: the player believes their rating is
    /// on the line, plays accordingly, and the match counts for nobody. The caller says so in the
    /// room, which is where they will be looking.</para>
    /// </summary>
    public bool CreatedLobbyCompetitiveDowngraded { get; private set; }

    /// <summary>
    /// The format the room was created for, or <c>Casual</c> when it is not competitive.
    ///
    /// <para>Derived from what the SERVER actually made, for the same reason
    /// <see cref="CreatedLobbyIsCompetitive"/> is: a request that got downgraded produced a
    /// casual room whatever was ticked here. See <see cref="RoomFormats"/> for why the format
    /// is read off the room's size rather than sent as a field of its own.</para>
    /// </summary>
    public RoomFormat CreatedLobbyFormat { get; private set; }

    /// <summary>
    /// Build the dialog. <paramref name="profiles"/> populates the mod
    /// dropdown; <paramref name="initiallySelected"/> is the entry that
    /// starts highlighted (typically the active profile from the Play
    /// tab). <paramref name="computeHash"/> is the async function the
    /// dialog calls every time the user picks a different mod so the
    /// fingerprint stays in sync with what's on disk.
    /// </summary>
    public CreateLobbyDialog(
        MultiplayerSession session,
        IReadOnlyList<ModProfile> profiles,
        ModProfile? initiallySelected,
        Func<ModProfile, Task<string>> computeHash,
        Func<ModProfile, ModCopyInfo> resolveCopyInfo,
        Func<string, Task> switchActiveCopy,
        int lobbyMaxPlayers = 8)
    {
        InitializeComponent();
        _session = session;
        _computeHash = computeHash;
        _resolveCopyInfo = resolveCopyInfo;
        _switchActiveCopy = switchActiveCopy;
        _profiles = profiles;
        _lobbyMaxPlayers = lobbyMaxPlayers;

        Title = Strings.Get("MpCreateDialogTitle");
        TitleBarControl.Title = Strings.Get("MpCreateDialogTitle");
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        CopyLabel.Text = Strings.Get("MpCreateDialogCopyLabel");
        TitleLabel.Text = Strings.Get("MpCreateDialogTitleLabel");
        MaxPlayersLabel.Text = Strings.Get("MpCreateDialogMaxPlayers");
        PrivateTitleText.Text = Strings.Get("MpCreateDialogPrivate");
        PrivateHint.Text = Strings.Get("MpCreateDialogPrivateBody");
        PrivateRoomCheck.ToolTip = TooltipHelper.Wrap(Strings.Get("MpCreateDialogPrivateHint"));
        CompetitiveTitleText.Text = Strings.Get("MpCreateDialogCompetitive");
        CompetitiveHint.Text = Strings.Get("MpCreateDialogCompetitiveHint");
        CompetitiveCheck.ToolTip = TooltipHelper.Wrap(Strings.Get("MpCreateDialogCompetitiveHint"));
        CompetitiveFormatLabel.Text = Strings.Get("MpCreateDialogFormat");
        PasswordRevealButton.Content = Strings.Get("MpCreateDialogShowPassword");
        Suggest1.Content = Strings.Get("MpCreateDialogSuggest1");
        Suggest2.Content = Strings.Get("MpCreateDialogSuggest2");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
        CreateButton.Content = Strings.Get("MpCreateDialogCreate");
        BuildRecordWarning();
        RefreshAnnounceNote();

        // Populate the mod dropdown. We show DisplayName, store the
        // ModProfile in Tag so SelectionChanged can read it back.
        foreach (var p in profiles)
        {
            var item = new ComboBoxItem
            {
                Content = p.DisplayName,
                Tag = p,
            };
            ModCombo.Items.Add(item);
            if (initiallySelected != null
                && string.Equals(p.Id, initiallySelected.Id, StringComparison.OrdinalIgnoreCase))
            {
                item.IsSelected = true;
            }
        }
        if (ModCombo.SelectedItem == null && ModCombo.Items.Count > 0)
            ((ComboBoxItem)ModCombo.Items[0]!).IsSelected = true;

        // One installed mod means there is nothing to pick: the card stays, the
        // affordance goes. A chevron that opens a list of one is a promise of choice.
        if (profiles.Count < 2)
        {
            ModChevron.Visibility = Visibility.Collapsed;
            ModCombo.IsHitTestVisible = false;
        }

        BuildMaxPlayersRow();
        BuildFormatRow();
        // A format is chosen from the start even while the row is hidden, so ticking the box
        // reveals a row with something already selected rather than three inert buttons.
        SelectFormat(RoomFormat.OneVOne);
        RefreshCompetitiveUi();
        RoomTitleBox.Focus();

        // Ceiling for SizeToContent="Height". This form only grows — the Record Game
        // notice is permanent, and the Radmin warning, the password row, the copy row and
        // the error line stack on top of it — and with no ceiling the window happily sizes
        // itself taller than the screen, which puts the FOOTER (Cancel / Create) past the
        // bottom edge where it reads as "the interface ate the buttons". The body row is a
        // star with a ScrollViewer in it, so clamping here makes the form scroll instead.
        // Read at construction rather than in XAML because it depends on the user's screen.
        try
        {
            var work = SystemParameters.WorkArea.Height;
            if (work > 200) MaxHeight = work - 40;   // leave the window some breathing room
        }
        catch (Exception ex)
        {
            // Best-effort: without a ceiling we are simply back to the old behaviour.
            DiagnosticLog.Write($"CreateLobbyDialog: could not read the work area (non-fatal): {ex.Message}");
        }

        // INFORMATIONAL (not blocking, not discouraging) heads-up when Radmin isn't
        // recognised as active. Creating the room and joining it are NEVER gated on
        // Radmin (join is gated only by the mod fingerprint; Create is never disabled),
        // and the game already auto-injects the 26.x IP via OverrideAddress — so the old
        // "other players won't be able to join" copy was FALSE and scared testers off.
        // The real requirement is only for actual in-game play. Two tones by whether we
        // can read an injectable 26.x IP. Best-effort: a probe failure hides the note.
        try
        {
            var status = RadminVpnService.GetStatus();
            if (!status.IsServiceRunning)
            {
                var ip = RadminVpnService.TryGetAdapterIp();
                RadminWarning.Text = string.IsNullOrEmpty(ip)
                    ? Strings.Get("MpCreateDialogRadminWarning")
                    : Strings.Format("MpCreateDialogRadminInfo", ip);
                RadminWarning.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: Radmin status probe failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>
    /// The recording warning, built as two Runs so the in-game checkbox's own name can
    /// be emphasised inside a localized sentence. That name stays English on purpose —
    /// it is what AoE3 actually shows on its setup screen.
    /// </summary>
    private void BuildRecordWarning()
    {
        var name = Strings.Get("MpCreateDialogRecordWarnName");
        var sentence = Strings.Format("MpCreateDialogRecordWarn", " ");
        var parts = sentence.Split(' ');
        RecordWarnText.Inlines.Clear();
        RecordWarnText.Inlines.Add(new System.Windows.Documents.Run(parts[0]));
        RecordWarnText.Inlines.Add(new System.Windows.Documents.Run(name)
        {
            Foreground = (System.Windows.Media.Brush)FindResource("MpCautionTextAlt"),
            FontWeight = FontWeights.SemiBold,
        });
        if (parts.Length > 1)
            RecordWarnText.Inlines.Add(new System.Windows.Documents.Run(parts[1]));
    }

    /// <summary>
    /// The footer note says what pressing Create will do. It flips with the private
    /// checkbox, because "it will be announced" is exactly what a private room does not
    /// do, and that promise is the reason someone ticks the box.
    /// </summary>
    private void RefreshAnnounceNote()
        => AnnounceNote.Text = Strings.Get(PrivateRoomCheck?.IsChecked == true
            ? "MpCreateDialogAnnounceNotePrivate"
            : "MpCreateDialogAnnounceNote");

    /// <summary>
    /// The 2..8 segmented row. Options above the server's cap are rendered but
    /// disabled: they say what the ceiling IS, which a shorter row would not.
    /// </summary>
    private void BuildMaxPlayersRow()
    {
        MaxPlayersRow.Children.Clear();
        _maxPlayerButtons.Clear();
        for (var n = MinRoomPlayers; n <= MaxRoomPlayers; n++)
        {
            var btn = new Button
            {
                Content = n.ToString(),
                Style = (Style)FindResource("MpSegment"),
                Tag = null,
                IsEnabled = n <= _lobbyMaxPlayers,
            };
            var value = n;
            btn.Click += (_, _) => SelectMaxPlayers(value);
            _maxPlayerButtons[n] = btn;
            MaxPlayersRow.Children.Add(btn);
        }
        SelectMaxPlayers(Math.Min(_lobbyMaxPlayers, MaxRoomPlayers));
    }

    private void SelectMaxPlayers(int value)
    {
        _maxPlayers = value;
        foreach (var kv in _maxPlayerButtons)
            kv.Value.Tag = kv.Key == value ? "active" : null;
        RefreshCompetitiveSizeNote();
    }

    /// <summary>The 1v1 / 2v2 / 3v3 row, revealed only while the competitive box is ticked.</summary>
    private void BuildFormatRow()
    {
        FormatRow.Children.Clear();
        _formatButtons.Clear();
        foreach (var format in Formats)
        {
            var btn = new Button
            {
                Content = Strings.Get(RoomFormats.LabelKey(format)!),
                Style = (Style)FindResource("MpSegment"),
                Tag = null,
                // A format needing more seats than the server allows cannot be created at all,
                // so it is shown disabled rather than hidden — the row then says what the
                // ceiling IS, the same choice the player-count row makes one section up.
                IsEnabled = RoomFormats.PlayersFor(format) <= _lobbyMaxPlayers,
            };
            var chosen = format;
            btn.Click += (_, _) =>
            {
                // PICKING A FORMAT IS DECLARING THE ROOM COMPETITIVE. The format only means
                // anything for a competitive room, so the alternative — a click that does
                // nothing until you also find the checkbox — is a control that looks broken.
                //
                // Assigning IsChecked raises CompetitiveCheck_Changed SYNCHRONOUSLY, and that
                // handler adopts whatever format matches the size currently chosen. So the state
                // may pass through a different format before the line below. That is fine — the
                // last call wins and the end state is the clicked one — but it is worth knowing
                // before somebody reads the SelectFormat below as redundant and deletes it.
                if (CompetitiveCheck != null && CompetitiveCheck.IsChecked != true)
                    CompetitiveCheck.IsChecked = true;

                SelectFormat(chosen);
            };
            _formatButtons[format] = btn;
            FormatRow.Children.Add(btn);
        }
    }

    /// <summary>
    /// Pick a format, which also picks the room size — 1v1 is two seats, 2v2 four, 3v3 six.
    ///
    /// <para><b>The size is moved BEFORE the row is disabled, and that order is the whole
    /// trap.</b> <c>IsEnabled = false</c> does not deselect: leaving it to the disabling pass
    /// would keep the old segment tagged active and <c>_maxPlayers</c> holding the old number,
    /// so the room would be created the wrong size with nothing on screen disagreeing.</para>
    /// </summary>
    private void SelectFormat(RoomFormat format)
    {
        _format = format;

        // The highlight is NOT set here — RefreshCompetitiveUi at the bottom of this method owns
        // it, so that a casual room can never end up showing a format it has not declared.

        // The size follows the format ONLY while the room is competitive. Without that guard the
        // constructor's own SelectFormat would drag a casual room down from eight seats to two
        // before anybody had ticked anything.
        var seats = RoomFormats.PlayersFor(format);
        if (CompetitiveCheck?.IsChecked == true && seats > 0 && seats <= _lobbyMaxPlayers)
            SelectMaxPlayers(seats);
        RefreshCompetitiveUi();
    }

    private void CompetitiveCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Ticking adopts the format that matches the size already chosen, when there is one.
        // Snapping a host who deliberately set four players back to 1v1 would resize their room
        // as a side effect of ticking a box.
        if (CompetitiveCheck.IsChecked == true)
        {
            var fromSize = RoomFormats.Resolve(competitive: true, _maxPlayers);
            SelectFormat(fromSize == RoomFormat.Unknown ? _format : fromSize);
        }
        RefreshCompetitiveUi();
    }

    /// <summary>
    /// Show or hide the format row, lock the player-count row while a format owns it, and say
    /// what the chosen format means for the rating.
    /// </summary>
    private void RefreshCompetitiveUi()
    {
        if (CompetitiveCheck == null || CompetitiveFormatRow == null) return;
        var competitive = CompetitiveCheck.IsChecked == true;

        // ALWAYS VISIBLE. It used to appear only once the box was ticked, which made one
        // decision take two steps and jumped the dialog's height at the moment of ticking.
        // Picking a format is now itself the way to declare the room competitive.
        CompetitiveFormatRow.Visibility = Visibility.Visible;

        // THE SELECTION IS PAINTED HERE AND NOWHERE ELSE, and that is what keeps the row honest
        // now that it is on screen for casual rooms too. SelectFormat used to tag the active
        // button itself, unconditionally — harmless while the row was hidden, and a lie the
        // moment it is not: a casual room would show 1v1 lit up, three centimetres under a
        // "Max players: 8" it flatly contradicts. It would also be claiming the one thing this
        // model refuses to claim, that a two-seat casual room IS a 1v1 (see RoomFormats).
        //
        // No format chosen yet, therefore nothing lit. MpSegment's own inactive state does the
        // drawing — never an Opacity layer, which would take ClearType down with it.
        foreach (var kv in _formatButtons)
            kv.Value.Tag = competitive && kv.Key == _format ? "active" : null;

        // The size belongs to the format while there is one, so the row is shown but inert —
        // hiding it would leave the host unable to see how big their own room is.
        foreach (var kv in _maxPlayerButtons)
            kv.Value.IsEnabled = !competitive && kv.Key <= _lobbyMaxPlayers;

        RefreshCompetitiveSizeNote();
    }

    /// <summary>
    /// Warn — never forbid — when a competitive room is sized so that it can never score.
    ///
    /// <para>The server refuses anything but exactly two participants (<c>not_1v1</c>), because a
    /// recording names one loser and that says nothing about who won a team game. So a
    /// competitive room for three is playable and will simply never rate, and learning that after
    /// the match is the bad outcome. Hooked to BOTH inputs it reads, or it goes stale the moment
    /// the other one changes.</para>
    /// </summary>
    private void RefreshCompetitiveSizeNote()
    {
        if (CompetitiveSizeNote == null || CompetitiveCheck == null) return;

        // Asked of the FORMAT, never of the seat count. "More than two players" also describes
        // a casual room, which has no rating to miss out on — and the two things worth saying
        // here are opposites, so one slot carries whichever applies.
        var format = RoomFormats.Resolve(CompetitiveCheck.IsChecked == true, _maxPlayers);

        // 1v1: the forfeit clause, which used to sit in the main hint and was shown to team
        // rooms too. The server refuses to apply it past two players, so saying it there was a
        // threat nothing carries out.
        if (RoomFormats.AbandonmentApplies(format))
        {
            CompetitiveSizeNote.Text = Strings.Get("MpCreateDialogCompetitiveForfeit");
            CompetitiveSizeNote.Visibility = Visibility.Visible;
            return;
        }

        // A team format: the rating is the thing that is NOT true for it yet.
        if (RoomFormats.IsTeam(format))
        {
            CompetitiveSizeNote.Text = Strings.Format(
                "MpCreateDialogCompetitiveTeamNote", Strings.Get(RoomFormats.LabelKey(format)!));
            CompetitiveSizeNote.Visibility = Visibility.Visible;
            return;
        }

        CompetitiveSizeNote.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// User picked a different mod from the dropdown. Recompute the
    /// fingerprint and update the title placeholder. Disable Create
    /// while the hash is in flight so a fast-clicker can't submit
    /// with a stale value.
    /// </summary>
    private async void ModCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModCombo.SelectedItem is not ComboBoxItem item || item.Tag is not ModProfile profile)
            return;

        _selectedProfile = profile;
        ModNameText.Text = profile.DisplayName;
        UpdateModIcon(profile);
        RefreshCopyRow(profile);
        ModNameDefaultTitle(profile);
        SetFingerprintState(FingerprintState.Loading, null);
        CreateButton.IsEnabled = false;

        try
        {
            _selectedHash = await _computeHash(profile);
            SetFingerprintState(FingerprintState.Ok, _selectedHash);
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _selectedHash = "";
            SetFingerprintState(FingerprintState.Failed, null);
            DiagnosticLog.Write($"CreateLobbyDialog: fingerprint failed: {ex.Message}");
            // Create stays disabled — the user can't submit without a valid hash.
            // Switching mods triggers a fresh attempt. The message is NOT pushed to
            // ErrorText: the card already says what is wrong, next to what it is
            // wrong about, and the footer line is for the server's answer to Create.
        }
    }

    private enum FingerprintState { Ok, Loading, Failed }

    /// <summary>
    /// Paint the fingerprint line under the mod name. Only the first six characters of
    /// the hash are shown — enough for two players to compare over voice, which is the
    /// only thing anyone does with this value, and short enough to fit beside the name.
    /// </summary>
    private void SetFingerprintState(FingerprintState state, string? hash)
    {
        var (brushKey, dotKey, text) = state switch
        {
            FingerprintState.Ok => ("MpOkText", "MpOk",
                Strings.Format("MpCreateDialogFingerprintOk", ShortHash(hash))),
            FingerprintState.Loading => ("MpCautionText", "MpCaution",
                Strings.Get("MpCreateDialogFingerprintLoading")),
            _ => ("MpDestructiveText", "MpDestructiveText",
                Strings.Get("MpCreateDialogFingerprintFailed")),
        };
        FingerprintText.Text = text;
        FingerprintText.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
        FingerprintDot.Fill = (System.Windows.Media.Brush)FindResource(dotKey);
        FingerprintText.ToolTip = string.IsNullOrEmpty(hash)
            ? null
            : TooltipHelper.Wrap(Strings.Get("MpCreateDialogHashLabel") + ": " + hash);
    }

    private static string ShortHash(string? hash)
        => string.IsNullOrEmpty(hash) ? "" : (hash!.Length <= 6 ? hash : hash[..6] + "…");

    /// <summary>Append a suggestion to the room title instead of replacing it.</summary>
    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Content is not string suggestion) return;
        var current = RoomTitleBox.Text?.Trim() ?? "";
        if (current.Contains(suggestion, StringComparison.OrdinalIgnoreCase)) return;
        var joined = string.IsNullOrEmpty(current) ? suggestion : current + " · " + suggestion;
        if (joined.Length > RoomTitleBox.MaxLength) joined = joined[..RoomTitleBox.MaxLength];
        RoomTitleBox.Text = joined;
        RoomTitleBox.CaretIndex = RoomTitleBox.Text.Length;
        RoomTitleBox.Focus();
    }

    /// <summary>
    /// Reveal or hide the password. A PasswordBox cannot show its own value, so the
    /// reveal swaps in a plain TextBox; whichever is visible is copied back into the
    /// PasswordBox, which stays the single source of truth for the request.
    /// </summary>
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
    private string CurrentPassword()
        => _passwordRevealed ? PasswordPlainBox.Text : PasswordBox.Password;

    /// <summary>
    /// Repaint the copy row for the picked mod. Hidden for single-install mods
    /// (zero change for the common case). When the mod has 2+ copies it lists
    /// them, marking the ACTIVE one; the combo is interactive only for the active
    /// dashboard mod (choosing a copy rotates the active copy — the copy
    /// multiplayer actually launches / fingerprints), display-only otherwise.
    /// </summary>
    private void RefreshCopyRow(ModProfile profile)
    {
        var info = _resolveCopyInfo?.Invoke(profile);
        if (info == null || !info.HasMultiple)
        {
            CopyRow.Visibility = Visibility.Collapsed;
            return;
        }

        _suppressCopyCombo = true;
        CopyCombo.Items.Clear();
        foreach (var c in info.Copies)
        {
            CopyCombo.Items.Add(new ComboBoxItem
            {
                Content = c.Label,
                Tag = c.InstallId,
                IsSelected = c.IsActive,
            });
        }
        if (CopyCombo.SelectedItem == null && CopyCombo.Items.Count > 0)
            ((ComboBoxItem)CopyCombo.Items[0]!).IsSelected = true;
        _suppressCopyCombo = false;

        CopyCombo.IsEnabled = info.CanSwitch;
        CopyHint.Visibility = info.CanSwitch ? Visibility.Collapsed : Visibility.Visible;
        if (!info.CanSwitch)
            CopyHint.Text = Strings.Get("MpCreateDialogCopyHintReadonly");
        CopyRow.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// User picked a different install copy. Multiplayer always uses the ACTIVE
    /// copy, so this rotates the active copy (single source of truth) and then
    /// recomputes the fingerprint so the room's required hash matches what will
    /// actually launch. A no-op when the picked copy is already active.
    /// </summary>
    private async void CopyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCopyCombo || _selectedProfile == null) return;
        if (CopyCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string installId)
            return;

        CreateButton.IsEnabled = false;
        CopyCombo.IsEnabled = false;
        SetFingerprintState(FingerprintState.Loading, null);
        try
        {
            await _switchActiveCopy(installId);
            _selectedHash = await _computeHash(_selectedProfile);
            SetFingerprintState(FingerprintState.Ok, _selectedHash);
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _selectedHash = "";
            SetFingerprintState(FingerprintState.Failed, null);
            DiagnosticLog.Write($"CreateLobbyDialog: copy switch failed: {ex.Message}");
        }
        // Re-render so the active marker + enabled state follow the switch
        // (also restores CopyCombo.IsEnabled per CanSwitch).
        RefreshCopyRow(_selectedProfile);
    }

    /// <summary>Refresh the room-title placeholder to match the picked mod.</summary>
    private void ModNameDefaultTitle(ModProfile profile)
    {
        // Only auto-replace if the user hasn't typed something custom.
        // We detect "custom" by checking whether the current text was
        // produced by us for any of the other profiles.
        var current = RoomTitleBox.Text?.Trim() ?? "";
        var looksAuto = _profiles.Any(p =>
            current == Strings.Format("MpCreateDialogDefaultTitle", p.DisplayName));
        if (string.IsNullOrEmpty(current) || looksAuto)
            RoomTitleBox.Text = Strings.Format("MpCreateDialogDefaultTitle", profile.DisplayName);
    }

    /// <summary>
    /// Swap the room mod-card's placeholder 🎮 for the picked mod's real
    /// icon (cached catalog icon.png or built-in packed icon) when one is
    /// available; otherwise keep the emoji on the blue disc.
    /// </summary>
    private void UpdateModIcon(ModProfile profile)
    {
        var brush = LoadIconBrush(profile);
        if (brush != null)
        {
            ModIconHost.Background = brush;
            ModIconEmoji.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            ModIconHost.Background = (System.Windows.Media.Brush)FindResource("MpBlueSubtle");
            ModIconEmoji.Visibility = System.Windows.Visibility.Visible;
        }
    }

    /// <summary>
    /// Resolve a mod's icon (cached catalog icon.png → live remote URL →
    /// built-in packed icon, via <see cref="ModProfile.ResolveIconSource"/>)
    /// to a UniformToFill brush. Shared with
    /// <see cref="ModProfileIconBrushConverter"/> so the selected-mod card
    /// disc and the dropdown items resolve icons identically. A remote icon
    /// downloads async and can't be frozen mid-flight (unconditional Freeze
    /// throws); unfrozen it repaints itself when the download completes.
    /// </summary>
    internal static System.Windows.Media.ImageBrush? LoadIconBrush(ModProfile profile)
    {
        string? uri = profile.ResolveIconSource();
        if (string.IsNullOrEmpty(uri)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new System.Uri(uri, System.UriKind.Absolute);
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            var br = new System.Windows.Media.ImageBrush(bmp)
            {
                Stretch = System.Windows.Media.Stretch.UniformToFill,
            };
            if (br.CanFreeze) br.Freeze();
            return br;
        }
        catch
        {
            return null;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProfile == null || string.IsNullOrEmpty(_selectedHash))
        {
            ShowError(Strings.Get("MpCreateDialogNoFingerprint"));
            return;
        }

        var title = RoomTitleBox.Text.Trim();
        if (title.Length < 3)
        {
            ShowError(Strings.Get("MpCreateDialogTitleTooShort"));
            return;
        }

        var maxPlayers = Math.Min(_lobbyMaxPlayers, _maxPlayers);

        CreateButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        // The busy caption, not just the disabled state: going inert is what a button does
        // when there is nothing to do, so on its own it says the opposite of what is happening.
        CreateButton.Content = Strings.Get("MpCreateDialogCreating");
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            // Password is only honoured when "Private room" is on —
            // matches the new UI grouping and prevents the user
            // from typing a password that the server quietly drops
            // because IsPrivate was never set.
            var isPrivate = PrivateRoomCheck?.IsChecked == true;
            var typed = CurrentPassword();
            var password = isPrivate && !string.IsNullOrEmpty(typed) ? typed : null;
            CreatedLobbyProfile = _selectedProfile;
            CreatedLobbyTitle = title;
            CreatedLobbyMaxPlayers = maxPlayers;
            CreatedLobbyIsPrivate = isPrivate;
            CreatedLobby = await _session.Api.CreateLobbyAsync(new CreateLobbyRequest
            {
                Title = title,
                ModId = _selectedProfile.Id,
                ModCombinedHash = _selectedHash,
                MaxPlayers = maxPlayers,
                Password = password,
                Competitive = CompetitiveCheck.IsChecked == true,
            });
            // The server's answer, not ours. An older backend does not send the field at all,
            // which deserialises to false — a casual room, which is exactly how such a backend
            // will treat the match anyway.
            CreatedLobbyIsCompetitive = CreatedLobby?.Competitive == true;
            CreatedLobbyCompetitiveDowngraded =
                CompetitiveCheck.IsChecked == true && !CreatedLobbyIsCompetitive;
            CreatedLobbyFormat = RoomFormats.Resolve(CreatedLobbyIsCompetitive, maxPlayers);
            DialogResult = true;
            Close();
        }
        catch (LobbyApiException ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: API error {ex.Code}: {ex.Message}");
            ShowError(ex.Message);
            CreateButton.Content = Strings.Get("MpCreateDialogCreate");
            CreateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: {ex.GetType().Name}: {ex.Message}");
            ShowError(ex.Message);
            CreateButton.Content = Strings.Get("MpCreateDialogCreate");
            CreateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Live "X / 64" counter under the Room title field. Cheap to
    /// update; gives the user immediate feedback when they bump
    /// up against the MaxLength.
    /// </summary>
    private void RoomTitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TitleCounter == null) return;
        var len = RoomTitleBox.Text?.Length ?? 0;
        TitleCounter.Text = $"{len}/{RoomTitleBox.MaxLength}";
    }

    /// <summary>
    /// Gate the PasswordBox on the Private room checkbox so users
    /// can't accidentally set a password that isn't enforced. When
    /// unchecked we also clear the field so an old value doesn't
    /// leak into the request.
    /// </summary>
    private void PrivateRoomCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (PasswordRow == null || PrivateRoomCheck == null) return;
        var isPrivate = PrivateRoomCheck.IsChecked == true;
        PasswordRow.Visibility = isPrivate ? Visibility.Visible : Visibility.Collapsed;
        if (!isPrivate)
        {
            // Clear BOTH, or a password typed, revealed and then un-privated would still
            // be sitting in the plain box the next time the row appears.
            PasswordBox.Password = "";
            PasswordPlainBox.Text = "";
        }
        RefreshAnnounceNote();
    }

}

/// <summary>
/// Resolves a <see cref="ModProfile"/> — bound from a mod ComboBoxItem's Tag —
/// to a circular icon brush for the create-room mod dropdown items. Returns the
/// mod's real icon when available, else a neutral blue-subtle placeholder disc
/// so an icon-less mod still shows a consistent avatar. The selected mod's icon
/// is handled separately by the disc beside the combo (<c>UpdateModIcon</c>), so
/// the combo's selection box keeps showing just the name string — only the
/// dropdown items use this converter.
/// </summary>
public sealed class ModProfileIconBrushConverter : System.Windows.Data.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is ModProfile profile)
        {
            var brush = CreateLobbyDialog.LoadIconBrush(profile);
            if (brush != null) return brush;
        }
        return Application.Current?.TryFindResource("MpBlueSubtle")
            ?? (object)System.Windows.Media.Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
