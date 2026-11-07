using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks the host for a new room name (design 103j). The counter sits inside the field.
///
/// The 3-80 length rule is the SERVER's (it validates every rename the same way it validates
/// creation); repeating it here is purely so the host gets instant feedback instead of a
/// round-trip error. The prototype draws /40; the server's number is the true one.
/// </summary>
public partial class RenameRoomDialog : BaseDialog
{
    /// <summary>Minimum room-name length — mirrors the backend's check.</summary>
    private const int MinNameLength = 3;

    public string EnteredName { get; private set; } = "";

    public RenameRoomDialog(string currentName, ModProfile? mod = null)
    {
        InitializeComponent();

        SetBarTitle(Strings.Get("MpRenameDialogTitle"));
        SetHeadline(Strings.Get("MpRenameHeadline"));
        FieldLabel.Text = Strings.Get("MpTeamsCreateName");
        PromptText.Text = Strings.Get("MpRenameDialogPrompt");
        CancelAction.Content = Strings.Get("BtnCancel");
        PrimaryAction.Content = Strings.Get("BtnSave");

        NameEntry.Text = currentName ?? "";
        Refresh();
        UseMod(mod);

        // Pre-select so typing replaces the old name outright — the common case
        // is "rename it to something else", not "append to it".
        Loaded += (_, _) =>
        {
            NameEntry.Focus();
            NameEntry.SelectAll();
        };
    }

    private void NameEntry_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        CounterText.Text = $"{NameEntry.Text.Length}/{NameEntry.MaxLength}";
        PrimaryAction.IsEnabled = NameEntry.Text.Trim().Length >= MinNameLength;
    }

    protected override void OnPrimary()
    {
        var name = NameEntry.Text.Trim();
        if (name.Length < MinNameLength) return;
        EnteredName = name;
        Answer(true);
    }
}
