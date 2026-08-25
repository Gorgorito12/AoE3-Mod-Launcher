using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Tiny modal that asks the user for a password and returns it via
/// <see cref="EnteredPassword"/>. Used by the multiplayer join flow to
/// gate private rooms without dragging in <c>Microsoft.VisualBasic</c>
/// just for its <c>InputBox</c>.
/// </summary>
public partial class PasswordPromptDialog : Window
{
    public string EnteredPassword { get; private set; } = "";

    /// <param name="prompt">
    /// Overrides the default question. Left null by every caller today — the prompt used to be
    /// a hardcoded English sentence passed in from the join flow, which is how this dialog came
    /// to be the one untranslated window in the multiplayer surface.
    /// </param>
    public PasswordPromptDialog(string? prompt = null)
    {
        InitializeComponent();
        Title = Strings.Get("MpJoinPasswordTitle");
        TitleBarControl.Title = Strings.Get("MpJoinPasswordTitle");
        PromptText.Text = prompt ?? Strings.Get("MpJoinPasswordPrompt");
        OkButton.Content = Strings.Get("MpJoinPasswordEnter");
        CancelButton.Content = Strings.Get("BtnCancel");
        Loaded += (_, _) => PasswordEntry.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        EnteredPassword = PasswordEntry.Password ?? "";
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
