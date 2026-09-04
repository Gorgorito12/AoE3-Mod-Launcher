using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Asks, once, whether the launcher may start with Windows and wait in the tray.
///
/// <para><b>Why a question and not a notice.</b> The Run key used to be written on the
/// first launch and announced afterwards by a tray balloon. Everything about that was
/// defensible except the order: a balloon that fires after a registry write cannot be
/// answered, is easy to miss entirely, and leaves the launcher having changed what the
/// machine does at logon without anybody agreeing to it. Asking first is strictly
/// stronger, and it costs one dialog on one launch.</para>
///
/// <para><b>The X means no.</b> <c>DialogResult</c> is only ever set to true by the Yes
/// button, so a dismissal, an Escape or a closed window all fall through to the
/// declining branch. Consent is never inferred from silence.</para>
///
/// <para>Themed rather than a <see cref="MessageBox"/> for the same reason
/// <see cref="SelfInstallPromptDialog"/> is: by the time it shows, the first-run
/// language chooser has set the UI language, and it is one of the first things a new
/// player sees.</para>
/// </summary>
public partial class BackgroundConsentDialog : Window
{
    public BackgroundConsentDialog()
    {
        InitializeComponent();
        Chrome.Title = Strings.Get("DlgBackgroundConsentTitle");
        BodyText.Text = Strings.Get("DlgBackgroundConsentBody");
        DetailText.Text = Strings.Get("DlgBackgroundConsentDetail");
        YesButton.Content = Strings.Get("DlgBackgroundConsentYes");
        NoButton.Content = Strings.Get("DlgBackgroundConsentNo");
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
