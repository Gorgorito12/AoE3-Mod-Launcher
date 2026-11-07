using System;
using System.Diagnostics;
using System.Windows;
using WarsOfLibertyLauncher.Controls;
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
public partial class CompatibilityLayerDialog : BaseDialog
{
    private readonly string _exePath;
    private readonly bool _canRemove;

    internal CompatibilityLayerDialog(string exePath, AppCompatLayerInfo layer)
    {
        InitializeComponent();
        _exePath = exePath;

        _canRemove = layer.AppliedByWindows && layer.InCurrentUserHive;

        SetBarTitle(Strings.Get("DlgCompatLayerBar"));
        SetHeadline(Strings.Get("DlgCompatLayerTitle"));
        SetLead(Strings.Get(_canRemove ? "DlgCompatLayerBody" : "DlgCompatLayerBodyManual"));
        SetDetail(Strings.Get("DlgCompatLayerFileLabel"), exePath);
        DontAskText.Text = Strings.Get("DlgCompatLayerDontAsk");
        SupportLinkHost.Content = Controls.SupportLink.Build();
        CancelAction.Content = Strings.Get("DlgCompatLayerLater");

        // With nothing to remove, the way to Properties is the one thing left to do, so it is
        // the primary button rather than a third one to the left of it.
        if (_canRemove)
        {
            SetLeftAction(Strings.Get("DlgCompatLayerProperties"));
            PrimaryAction.Content = Strings.Get("DlgCompatLayerRemove");
        }
        else
        {
            PrimaryAction.Content = Strings.Get("DlgCompatLayerProperties");
        }
    }

    /// <summary>The user ticked "don't show this again".</summary>
    public bool DontAskAgain => DontAskCheck.IsChecked == true;

    protected override void OnPrimary()
    {
        if (_canRemove) Answer(true);
        else OnLeft();
    }

    protected override void OnLeft()
    {
        try
        {
            // The shell "properties" verb opens the same dialog as right-click →
            // Properties, where the Compatibility tab lives. Needs UseShellExecute.
            Services.HarnessMode.Start(new ProcessStartInfo
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

        Answer(false);
    }
}
