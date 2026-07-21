using System.Windows;
using WarsOfLibertyLauncher.Localization;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirms "Remove from my mods" — which only drops the id from
/// <see cref="Models.LauncherConfig.UserModIds"/> and never deletes a single
/// file.
///
/// The dialog exists because that distinction is invisible: an INSTALLED mod
/// vanishes from the dashboard MODS popup while its multi-GB folder stays on
/// disk, which reads as "it uninstalled itself" or, worse, as lost data.
/// Nobody guesses that re-adding it from the Workshop brings everything back,
/// so the copy says it — and shows the folder for an installed mod, so the
/// promise is verifiable rather than merely asserted.
///
/// Returns the choice via <c>ShowDialog()</c> (<c>true</c> = remove).
/// </summary>
public partial class RemoveFromCollectionDialog : Window
{
    public RemoveFromCollectionDialog(string modName, bool isInstalled, string? installPath)
    {
        InitializeComponent();

        Chrome.Title = Strings.Get("DlgRemoveModTitle");
        ModNameText.Text = modName;
        ConfirmButton.Content = Strings.Get("DlgRemoveModConfirm");
        CancelButton.Content = Strings.Get("DlgRemoveModCancel");

        // Two bodies rather than one hedged paragraph: for a mod that isn't
        // installed there is genuinely nothing at stake, and claiming otherwise
        // is what teaches people to click through the warning that does matter.
        bool showPath = isInstalled && !string.IsNullOrWhiteSpace(installPath);
        BodyText.Text = Strings.Get(isInstalled
            ? "DlgRemoveModBodyInstalled"
            : "DlgRemoveModBodyNotInstalled");

        if (showPath)
        {
            PathLabel.Text = Strings.Get("DlgRemoveModPathLabel");
            PathText.Text = installPath;
            PathBlock.Visibility = Visibility.Visible;
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
