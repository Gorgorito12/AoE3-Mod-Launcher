using System.Windows;

namespace WarsOfLibertyLauncher;

/// <summary>How a <see cref="ThemedConfirmDialog"/> introduces its question.</summary>
public enum ConfirmTone
{
    /// <summary>A decision with a cost (a large download): ℹ in gold.</summary>
    Question,
    /// <summary>Something may go wrong if you carry on (low disk space): ⚠ in amber.</summary>
    Warning,
}

/// <summary>
/// The launcher's own two-button confirmation, for the places that used the plain white Windows
/// <see cref="MessageBox"/> in the middle of an otherwise dark, themed flow — the repair's
/// download-cost question and the low-disk-space warning that follows it.
///
/// <para>Every choice other than the confirm button is a NO: Cancel, ✕ and Escape all leave
/// <c>DialogResult</c> false or null, and <see cref="Ask"/> only returns true for
/// <c>true</c>. That is the MessageBox contract the callers were written against.</para>
///
/// <para>Reuse this rather than adding another single-purpose prompt window; the existing ones
/// (<see cref="SelfInstallPromptDialog"/> and its siblings) hardcode their strings.</para>
/// </summary>
public partial class ThemedConfirmDialog : Window
{
    public ThemedConfirmDialog(string title, string body, string confirmLabel, string cancelLabel,
        ConfirmTone tone = ConfirmTone.Question)
    {
        InitializeComponent();
        Chrome.Title = title;
        Title = title;
        BodyText.Text = body;
        ConfirmButton.Content = confirmLabel;
        CancelButton.Content = cancelLabel;
        ToneGlyph.Text = tone == ConfirmTone.Warning ? "⚠" : "ℹ";
        ToneGlyph.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            tone == ConfirmTone.Warning ? "WarningBrush" : "AccentBrush");
    }

    /// <summary>
    /// Shows the question modally and returns true only when the player confirmed. With no usable
    /// owner (none given, or one that is not on screen) it centres on the screen instead.
    /// </summary>
    public static bool Ask(Window? owner, string title, string body, string confirmLabel,
        string cancelLabel, ConfirmTone tone = ConfirmTone.Question) =>
        Ask(owner, title, body, confirmLabel, cancelLabel, tone, defaultIsCancel: false);

    /// <param name="defaultIsCancel">
    /// True for a question that ARRIVED from outside — a <c>wol-launcher://</c> link any web page
    /// can fire. Enter then answers no, so a stray keypress can never accept what a stranger asked.
    /// </param>
    public static bool Ask(Window? owner, string title, string body, string confirmLabel,
        string cancelLabel, ConfirmTone tone, bool defaultIsCancel)
    {
        var dialog = new ThemedConfirmDialog(title, body, confirmLabel, cancelLabel, tone);
        if (defaultIsCancel)
        {
            dialog.ConfirmButton.IsDefault = false;
            dialog.CancelButton.IsDefault = true;
            dialog.Loaded += (_, _) => dialog.CancelButton.Focus();
        }
        if (owner is { IsLoaded: true, IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
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
