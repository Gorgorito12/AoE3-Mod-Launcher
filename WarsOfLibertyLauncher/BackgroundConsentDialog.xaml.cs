using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
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
/// <para><b>No "don't ask again" box</b>, though 103d draws one: both answers are final, so
/// the question already never comes back, and a box that changes nothing is what the design
/// base forbids.</para>
/// </summary>
public partial class BackgroundConsentDialog : BaseDialog
{
    internal Button YesButton => PrimaryAction;
    internal Button NoButton => CancelAction;

    public BackgroundConsentDialog()
    {
        InitializeComponent();
        SetBarTitle(Strings.Get("DlgBackgroundConsentBar"));
        SetHeadline(Strings.Get("DlgBackgroundConsentTitle"));
        SetLead(Strings.Get("DlgBackgroundConsentBody"));
        DetailText.Text = Strings.Get("DlgBackgroundConsentDetail");
        YesButton.Content = Strings.Get("DlgBackgroundConsentYes");
        NoButton.Content = Strings.Get("DlgBackgroundConsentNo");
    }
}
