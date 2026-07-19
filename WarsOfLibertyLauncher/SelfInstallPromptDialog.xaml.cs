using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Themed (dark "dorado imperial") replacement for the plain white
/// <see cref="MessageBox"/> that used to ask "Install a stable copy?" on first
/// launch. Content reuses the Tarea-H strings; by the time it shows, the first-run
/// language chooser has already set the UI language, so it appears localized.
/// Returns the choice via <c>ShowDialog()</c> (<c>true</c> = install, <c>false</c>
/// = not now / ✕).
/// </summary>
public partial class SelfInstallPromptDialog : Window
{
    public SelfInstallPromptDialog()
    {
        InitializeComponent();
        Chrome.Title = Strings.Get("DlgSettingsBgInstallPromptTitle");
        BodyText.Text = Strings.Get("DlgSettingsBgInstallPromptBody");
        YesButton.Content = Strings.Get("DlgSettingsBgInstallPromptYes");
        NoButton.Content = Strings.Get("DlgSettingsBgInstallPromptNo");
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
