using System;
using System.Diagnostics;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Explains that Windows — not the launcher — pinned a compatibility mode on the game
/// executable, which is what forces the UAC prompt on every launch, and offers to undo it.
///
/// <para><c>ShowDialog()</c> returns true when the user asked to remove the layer. The
/// "open Properties" route closes with false: the user is going to do it by hand, so the
/// caller must not also try.</para>
///
/// <para>When the layer was set deliberately (no <c>~</c> marker) or lives machine-wide,
/// <see cref="AppCompatLayerInfo.AppliedByWindows"/> / <c>InCurrentUserHive</c> are false
/// and the Remove button is hidden — undoing someone's own deliberate choice, or writing
/// to HKLM, is not ours to offer.</para>
/// </summary>
public partial class CompatibilityLayerDialog : Window
{
    private readonly string _exePath;

    internal CompatibilityLayerDialog(string exePath, AppCompatLayerInfo layer)
    {
        InitializeComponent();
        _exePath = exePath;

        bool canRemove = layer.AppliedByWindows && layer.InCurrentUserHive;

        Chrome.Title = Strings.Get("DlgCompatLayerTitle");
        BodyText.Text = Strings.Get(canRemove ? "DlgCompatLayerBody" : "DlgCompatLayerBodyManual");
        FileLabel.Text = Strings.Get("DlgCompatLayerFileLabel");
        FilePathText.Text = exePath;
        DontAskCheck.Content = Strings.Get("DlgCompatLayerDontAsk");
        LaterButton.Content = Strings.Get("DlgCompatLayerLater");
        PropertiesButton.Content = Strings.Get("DlgCompatLayerProperties");
        RemoveButton.Content = Strings.Get("DlgCompatLayerRemove");

        if (!canRemove)
        {
            RemoveButton.Visibility = Visibility.Collapsed;
            PropertiesButton.IsDefault = true;
        }
    }

    /// <summary>The user ticked "don't show this again".</summary>
    public bool DontAskAgain => DontAskCheck.IsChecked == true;

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void PropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // The shell "properties" verb opens the same dialog as right-click →
            // Properties, where the Compatibility tab lives. Needs UseShellExecute.
            Process.Start(new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = true,
                Verb = "properties",
            });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"CompatibilityLayerDialog: could not open properties: {ex.Message}");
        }

        DialogResult = false;
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
