using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// A new persistent team (design 103k): a name (2 to 40 characters, the server's bounds) and an
/// optional tag of up to 8, in one double field. The prototype draws a three-letter tag; the
/// server takes eight. Only collects; the tab creates the team.
/// </summary>
public partial class CreateTeamDialog : BaseDialog
{
    private const int MinNameLength = 2;

    public string EnteredName { get; private set; } = "";
    public string EnteredTag { get; private set; } = "";

    public CreateTeamDialog()
    {
        InitializeComponent();
        SetBarTitle(Strings.Get("MpTeamsCreateTitle"));
        SetHeadline(Strings.Get("MpTeamsCreateHeadline"));
        SetLead(Strings.Get("MpTeamsCreateHint"));
        TagHint.Text = Strings.Get("MpTeamsCreateTagHint");
        NameLabel.Text = Strings.Get("MpTeamsCreateFieldLabel");
        CancelAction.Content = Strings.Get("BtnCancel");
        PrimaryAction.Content = Strings.Get("MpTeamsCreateButton");
        PrimaryAction.IsEnabled = false;
    }

    /// <summary>Whether the typed name may be sent. For the tests and the button.</summary>
    internal bool CanConfirm => NameEntry.Text.Trim().Length >= MinNameLength;

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
        => PrimaryAction.IsEnabled = CanConfirm;

    protected override void OnPrimary()
    {
        if (!CanConfirm) return;
        EnteredName = NameEntry.Text.Trim();
        EnteredTag = TagEntry.Text.Trim();
        Answer(true);
    }
}
