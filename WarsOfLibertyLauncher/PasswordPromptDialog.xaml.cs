using System.Windows;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks for the password of a private room and returns it via <see cref="EnteredPassword"/>
/// (design 103i). It is a dialog of the room's mod: its tint on the bar and its colour on "Join".
/// </summary>
public partial class PasswordPromptDialog : BaseDialog
{
    public string EnteredPassword { get; private set; } = "";

    /// <param name="prompt">
    /// Overrides the default question. Left null by every caller today — the prompt used to be
    /// a hardcoded English sentence passed in from the join flow, which is how this dialog came
    /// to be the one untranslated window in the multiplayer surface.
    /// </param>
    /// <param name="hostName">Whose room it is, for the headline. Null says "Private room".</param>
    /// <param name="mod">The room's mod.</param>
    public PasswordPromptDialog(string? prompt = null, string? hostName = null, ModProfile? mod = null)
    {
        InitializeComponent();
        SetBarTitle(Strings.Get("MpJoinPasswordTitle"));
        SetHeadline(string.IsNullOrWhiteSpace(hostName)
            ? Strings.Get("MpJoinPasswordTitle")
            : Strings.Format("MpJoinPasswordHeadline", hostName));
        SetLead(prompt ?? Strings.Get("MpJoinPasswordPrompt"));
        FieldLabel.Text = Strings.Get("MpJoinPasswordField");
        Placeholder.Text = Strings.Get("MpJoinPasswordPlaceholder");
        RevealButton.Content = Strings.Get("MpCreateDialogShowPassword");
        PrimaryAction.Content = Strings.Get("MpJoinPasswordEnter");
        CancelAction.Content = Strings.Get("BtnCancel");
        UseMod(mod);
        Loaded += (_, _) => PasswordEntry.Focus();
    }

    private bool Revealed => PasswordPlain.Visibility == Visibility.Visible;

    private void PasswordEntry_Changed(object sender, RoutedEventArgs e)
        => Placeholder.Visibility = PasswordEntry.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        if (Revealed)
        {
            PasswordEntry.Password = PasswordPlain.Text;
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordEntry.Visibility = Visibility.Visible;
            RevealButton.Content = Strings.Get("MpCreateDialogShowPassword");
            PasswordEntry.Focus();
            return;
        }
        PasswordPlain.Text = PasswordEntry.Password;
        PasswordEntry.Visibility = Visibility.Collapsed;
        PasswordPlain.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Collapsed;
        RevealButton.Content = Strings.Get("MpCreateDialogHidePassword");
        PasswordPlain.Focus();
        PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
    }

    protected override void OnPrimary()
    {
        EnteredPassword = (Revealed ? PasswordPlain.Text : PasswordEntry.Password) ?? "";
        Answer(true);
    }
}
