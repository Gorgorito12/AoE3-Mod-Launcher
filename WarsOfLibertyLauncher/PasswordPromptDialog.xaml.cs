using System.Windows;

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

    public PasswordPromptDialog(string prompt)
    {
        InitializeComponent();
        PromptText.Text = prompt;
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
