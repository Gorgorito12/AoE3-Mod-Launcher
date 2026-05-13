using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Models.Multiplayer;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Modal dialog to create a new multiplayer room. The mod and its
/// fingerprint are pre-computed by the caller (the Multiplayer tab) and
/// shown read-only — the user can only change the room title, max
/// players and optional password. That keeps the join-side mod-hash
/// check honest: the host can't accidentally claim to be hosting a
/// different version of the mod than what's on disk.
/// </summary>
public partial class CreateLobbyDialog : Window
{
    private readonly MultiplayerSession _session;
    private readonly ModProfile _profile;
    private readonly string _modCombinedHash;
    private readonly int _lobbyMaxPlayers;

    /// <summary>Set when the dialog returns DialogResult=true.</summary>
    public CreateLobbyResponse? CreatedLobby { get; private set; }

    public CreateLobbyDialog(
        MultiplayerSession session,
        ModProfile profile,
        string modCombinedHash,
        int lobbyMaxPlayers = 8)
    {
        InitializeComponent();
        _session = session;
        _profile = profile;
        _modCombinedHash = modCombinedHash;
        _lobbyMaxPlayers = lobbyMaxPlayers;

        Title = Strings.Get("MpCreateDialogTitle");
        TitleText.Text = Strings.Get("MpCreateDialogTitle");
        ModLabel.Text = Strings.Get("MpCreateDialogModLabel");
        ModNameText.Text = profile.DisplayName;
        HashLabel.Text = Strings.Get("MpCreateDialogHashLabel");
        HashText.Text = modCombinedHash;
        TitleLabel.Text = Strings.Get("MpCreateDialogTitleLabel");
        MaxPlayersLabel.Text = Strings.Get("MpCreateDialogMaxPlayers");
        PasswordLabel.Text = Strings.Get("MpCreateDialogPassword");
        CancelButton.Content = Strings.Get("MpCreateDialogCancel");
        CreateButton.Content = Strings.Get("MpCreateDialogCreate");

        RoomTitleBox.Text = $"{profile.DisplayName} room";
        RoomTitleBox.Focus();

        // Cap the combo at the server's lobby cap. Items beyond the cap
        // stay visible but disabled — keeps the dropdown stable across
        // server-side config tweaks without needing a rebuild.
        for (int i = 0; i < MaxPlayersCombo.Items.Count; i++)
        {
            if (MaxPlayersCombo.Items[i] is ComboBoxItem item)
            {
                if (int.TryParse(item.Content?.ToString(), out var val) && val > _lobbyMaxPlayers)
                    item.IsEnabled = false;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
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
                ModId = _profile.Id,
                ModCombinedHash = _modCombinedHash,
                MaxPlayers = maxPlayers,
                Password = string.IsNullOrEmpty(PasswordBox.Password) ? null : PasswordBox.Password,
            });
            DialogResult = true;
            Close();
        }
        catch (LobbyApiException ex)
        {
            ShowError(ex.Message);
            CreateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
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
