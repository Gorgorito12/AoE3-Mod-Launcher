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
    private readonly int _lobbyMaxPlayers;

    private ModProfile? _selectedProfile;
    private string _selectedHash = "";

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
        int lobbyMaxPlayers = 8)
    {
        InitializeComponent();
        _session = session;
        _computeHash = computeHash;
        _profiles = profiles;
        _lobbyMaxPlayers = lobbyMaxPlayers;

        Title = Strings.Get("MpCreateDialogTitle");
        TitleBarControl.Title = Strings.Get("MpCreateDialogTitle");
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        // HashLabel is rendered next to the "Advanced details" toggle
        // and uses the localised "(mod fingerprint)" suffix from the
        // strings table. Wrap in parens to make it read as a hint.
        HashLabel.Text = "(" + Strings.Get("MpCreateDialogHashLabel") + ")";
        TitleLabel.Text = Strings.Get("MpCreateDialogTitleLabel");
        MaxPlayersLabel.Text = Strings.Get("MpCreateDialogMaxPlayers");
        // The dialog no longer has a "Password" label per se — the
        // PasswordBox is grouped under the Private room checkbox.
        // Keep the resource lookup for future re-localisation but
        // don't try to assign to a removed control.
        var _passwordLabel = Strings.Get("MpCreateDialogPassword");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
        CreateButton.Content = Strings.Get("MpCreateDialogCreate");

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

        RoomTitleBox.Focus();

        // Cap the max-players combo at the server's lobby cap.
        for (int i = 0; i < MaxPlayersCombo.Items.Count; i++)
        {
            if (MaxPlayersCombo.Items[i] is ComboBoxItem item)
            {
                if (int.TryParse(item.Content?.ToString(), out var val) && val > _lobbyMaxPlayers)
                    item.IsEnabled = false;
            }
        }
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
        UpdateModIcon(profile);
        ModNameDefaultTitle(profile);
        HashText.Text = "Loading…";
        CreateButton.IsEnabled = false;

        try
        {
            _selectedHash = await _computeHash(profile);
            HashText.Text = _selectedHash;
            CreateButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _selectedHash = "";
            HashText.Text = "";
            ShowError(ex.Message);
            // Create stays disabled — the user can't submit without a
            // valid hash. Switching mods triggers a fresh attempt.
        }
    }

    /// <summary>Refresh the room-title placeholder to match the picked mod.</summary>
    private void ModNameDefaultTitle(ModProfile profile)
    {
        // Only auto-replace if the user hasn't typed something custom.
        // We detect "custom" by checking whether the current text was
        // produced by us for any of the other profiles.
        var current = RoomTitleBox.Text?.Trim() ?? "";
        var looksAuto = _profiles.Any(p => current == $"{p.DisplayName} room");
        if (string.IsNullOrEmpty(current) || looksAuto)
            RoomTitleBox.Text = $"{profile.DisplayName} room";
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
            ShowError("Pick a mod first — the fingerprint is still loading.");
            return;
        }

        var title = RoomTitleBox.Text.Trim();
        if (title.Length < 3)
        {
            ShowError("Title must be at least 3 characters.");
            return;
        }

        int maxPlayers = _lobbyMaxPlayers;
        if (MaxPlayersCombo.SelectedItem is ComboBoxItem sel
            && int.TryParse(sel.Content?.ToString(), out var val))
            maxPlayers = Math.Min(_lobbyMaxPlayers, val);

        CreateButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            // Password is only honoured when "Private room" is on —
            // matches the new UI grouping and prevents the user
            // from typing a password that the server quietly drops
            // because IsPrivate was never set.
            var isPrivate = PrivateRoomCheck?.IsChecked == true;
            var password = isPrivate && !string.IsNullOrEmpty(PasswordBox.Password)
                ? PasswordBox.Password
                : null;
            CreatedLobbyProfile = _selectedProfile;
            CreatedLobbyTitle = title;
            CreatedLobbyMaxPlayers = maxPlayers;
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
            CreateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CreateLobbyDialog: {ex.GetType().Name}: {ex.Message}");
            ShowError(ex.Message);
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
        TitleCounter.Text = $"{len} / {RoomTitleBox.MaxLength}";
    }

    /// <summary>
    /// Gate the PasswordBox on the Private room checkbox so users
    /// can't accidentally set a password that isn't enforced. When
    /// unchecked we also clear the field so an old value doesn't
    /// leak into the request.
    /// </summary>
    private void PrivateRoomCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (PasswordBox == null || PrivateRoomCheck == null) return;
        PasswordBox.IsEnabled = PrivateRoomCheck.IsChecked == true;
        if (PrivateRoomCheck.IsChecked != true)
            PasswordBox.Password = "";
    }

    /// <summary>
    /// Expand/collapse the "Advanced details" section that holds
    /// the mod fingerprint. Kept demoted because the redesign brief
    /// explicitly says the hash shouldn't dominate the dialog.
    /// </summary>
    private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
    {
        if (HashText == null || AdvancedCaret == null) return;
        var nowVisible = HashText.Visibility != Visibility.Visible;
        HashText.Visibility = nowVisible ? Visibility.Visible : Visibility.Collapsed;
        AdvancedCaret.Text = nowVisible ? "▾" : "▸";
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
