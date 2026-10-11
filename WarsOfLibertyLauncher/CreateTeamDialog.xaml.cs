using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// A new persistent team: a name (2 to 40 characters, the server's bounds) and an optional tag
/// of up to 8. Only collects; the tab creates the team.
/// </summary>
public partial class CreateTeamDialog : Window
{
    private const int MinNameLength = 2;

    public string EnteredName { get; private set; } = "";
    public string EnteredTag { get; private set; } = "";

    public CreateTeamDialog()
    {
        InitializeComponent();
        Title = Strings.Get("MpTeamsCreateTitle");
        TitleBarControl.Title = Strings.Get("MpTeamsCreateTitle");
        NameLabel.Text = Strings.Get("MpTeamsCreateName");
        TagLabel.Text = Strings.Get("MpTeamsCreateTag");
        HintText.Text = Strings.Get("MpTeamsCreateHint");
        CancelButton.Content = Strings.Get("BtnCancel");
        OkButton.Content = Strings.Get("MpTeamsCreateButton");
    }

    /// <summary>Whether the typed name may be sent. For the tests and the button.</summary>
    internal bool CanConfirm => NameEntry.Text.Trim().Length >= MinNameLength;

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OkButton != null) OkButton.IsEnabled = CanConfirm;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfirm) return;
        EnteredName = NameEntry.Text.Trim();
        EnteredTag = TagEntry.Text.Trim();
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
