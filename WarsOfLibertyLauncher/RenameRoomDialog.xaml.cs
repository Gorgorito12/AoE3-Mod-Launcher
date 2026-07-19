using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Tiny modal that asks the host for a new room name. Mirrors
/// <see cref="PasswordPromptDialog"/> (same chrome, same button row) with a
/// plain TextBox instead of the password box.
///
/// The 3-80 length rule is the SERVER's (it validates every rename the same way
/// it validates creation); repeating it here is purely so the host gets instant
/// feedback instead of a round-trip error.
/// </summary>
public partial class RenameRoomDialog : Window
{
    /// <summary>Minimum room-name length — mirrors the backend's check.</summary>
    private const int MinNameLength = 3;

    public string EnteredName { get; private set; } = "";

    public RenameRoomDialog(string currentName)
    {
        InitializeComponent();

        Title = Strings.Get("MpRenameDialogTitle");
        TitleBarControl.Title = Strings.Get("MpRenameDialogTitle");
        PromptText.Text = Strings.Get("MpRenameDialogPrompt");
        CancelButton.Content = Strings.Get("BtnCancel");
        OkButton.Content = Strings.Get("BtnSave");

        NameEntry.Text = currentName ?? "";
        RefreshOkState();

        // Pre-select so typing replaces the old name outright — the common case
        // is "rename it to something else", not "append to it".
        Loaded += (_, _) =>
        {
            NameEntry.Focus();
            NameEntry.SelectAll();
        };
    }

    private void NameEntry_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => RefreshOkState();

    private void RefreshOkState()
        => OkButton.IsEnabled = (NameEntry.Text ?? "").Trim().Length >= MinNameLength;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (NameEntry.Text ?? "").Trim();
        if (name.Length < MinNameLength) return;
        EnteredName = name;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
