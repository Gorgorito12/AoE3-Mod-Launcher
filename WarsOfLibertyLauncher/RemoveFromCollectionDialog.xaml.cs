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
/// so the copy says it — and shows the folder, so the promise is verifiable
/// rather than merely asserted.
///
/// Only shown for an INSTALLED mod. The caller
/// (<c>MainWindow.ModsBrowserView_RemoveFromCollectionRequested</c>) removes a
/// non-installed mod outright: nothing is at stake there, and confirming
/// harmless things is what trains users to click through the prompt that
/// matters. That's why the body is unconditional here — don't add a
/// not-installed variant without moving that gate too.
///
/// Returns the choice via <c>ShowDialog()</c> (<c>true</c> = remove).
/// </summary>
public partial class RemoveFromCollectionDialog : Window
{
    public RemoveFromCollectionDialog(string modName, string? installPath)
    {
        InitializeComponent();

        Chrome.Title = Strings.Get("DlgRemoveModTitle");
        ModNameText.Text = modName;
        BodyText.Text = Strings.Get("DlgRemoveModBodyInstalled");
        ConfirmButton.Content = Strings.Get("DlgRemoveModConfirm");
        CancelButton.Content = Strings.Get("DlgRemoveModCancel");

        // Defensive: the mod is installed, but path resolution can still come
        // up empty (a probe-only detection that later fails). Better to drop
        // the row than to render an empty box under "its files stay here".
        if (!string.IsNullOrWhiteSpace(installPath))
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
