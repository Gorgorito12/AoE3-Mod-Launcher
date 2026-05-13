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
        TitleText.Text = Strings.Get("MpCreateDialogTitle");
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        HashLabel.Text = Strings.Get("MpCreateDialogHashLabel");
        TitleLabel.Text = Strings.Get("MpCreateDialogTitleLabel");
        MaxPlayersLabel.Text = Strings.Get("MpCreateDialogMaxPlayers");
        PasswordLabel.Text = Strings.Get("MpCreateDialogPassword");
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
        ModNameDefaultTitle(profile);
        HashText.Text = Strings.Get("MpVlanInstalling");   // "Loading…"
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
            CreatedLobby = await _session.Api.CreateLobbyAsync(new CreateLobbyRequest
            {
                Title = title,
                ModId = _selectedProfile.Id,
                ModCombinedHash = _selectedHash,
                MaxPlayers = maxPlayers,
                Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password,
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
}
