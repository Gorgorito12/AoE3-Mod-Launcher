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
        PasswordRevealButton.Content = Strings.Get("MpCreateDialogShowPassword");
        Suggest1.Content = Strings.Get("MpCreateDialogSuggest1");
        Suggest2.Content = Strings.Get("MpCreateDialogSuggest2");
        Suggest3.Content = Strings.Get("MpCreateDialogSuggest3");
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
    /// be emphasised inside a localized sentence.
    ///
    /// <para>The name is LOCALIZED (`MpCreateDialogRecordWarnName`), not fixed English. It
    /// used to be pinned to "Record Game" in both languages, on the assumption that AoE3
    /// showed that label whatever language it ran in. It does not — the Spanish game calls
    /// the box "Grabar partida", so the Spanish text was naming a checkbox the reader could
    /// not find. Anything the launcher tells the player to click in AoE3 has to be quoted in
    /// the language that player's AoE3 is actually in.</para>
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
            });
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
