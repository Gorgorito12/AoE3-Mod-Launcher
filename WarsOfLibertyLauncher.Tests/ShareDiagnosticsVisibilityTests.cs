using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// Share diagnostics is visible and findable (design handoff 50a-50c).
///
/// <para><b>Why this exists.</b> It is the first thing the team asks for on Discord, and players
/// could not find it: in the mod window it was a ghost button weighing exactly what "View logs"
/// did, and the window's search could not find it at all — its caption is a plain string, which
/// the search never reads. Each test pins one half of the fix against the real window, because
/// the pure search tests can be right while the window wires none of it.</para>
/// </summary>
[Collection("wpf-and-language")]
public class ShareDiagnosticsVisibilityTests
{
    private static ModPropertiesDialog Build(Action? share = null) => new(
        Services.ModRegistry.Default,
        new Services.UpdateService(new LauncherConfig(), Services.ModRegistry.Default),
        new LauncherConfig(),
        translationIndex: null,
        applyTranslation: _ => { },
        revertToEnglish: () => { },
        openVerify: () => { },
        openRepair: () => { },
        checkForUpdates: () => Task.FromResult<Services.UpdateService.CheckResult?>(null),
        openAoE3Folder: () => { },
        changeModFolder: () => { },
        changeAoE3Folder: () => { },
        openUserDataFolder: () => { },
        createBackup: () => null,
        restoreBackup: () => null,
        viewLogs: () => { },
        shareDiagnostics: share ?? (() => { }),
        uninstall: () => { });

    private static void Run(string lang, Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var was = Strings.Language;
            try
            {
                Strings.SetLanguage(lang);
                body();
            }
            finally { Strings.SetLanguage(was); }
        });
        Assert.Null(error);
    }

    private static bool IsUnder(DependencyObject child, DependencyObject ancestor)
    {
        for (var d = LogicalTreeHelper.GetParent(child); d != null; d = LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// THE ONE THAT MATTERS: typing the word in either language — with or without the accent,
    /// or the English words a player would use for a crash — lands on LOCAL FILES with the card
    /// on screen, whatever language the window is in.
    /// </summary>
    [Theory]
    [InlineData("en", "diagnostico")]
    [InlineData("en", "diagnóstico")]
    [InlineData("en", "reporte")]
    [InlineData("en", "crash")]
    [InlineData("es", "diagnostico")]
    [InlineData("es", "reporte")]
    [InlineData("es", "crash")]
    [InlineData("es", "Share diagnostics")]
    public void THE_ONE_THAT_MATTERS_TheSearchFindsItInEitherLanguage(string lang, string query)
    {
        Run(lang, () =>
        {
            var dlg = Build();
            dlg.ModSearchBox.Text = query;

            Assert.Equal(Visibility.Visible, dlg.LocalFilesPanel.Visibility);
            Assert.Equal(Visibility.Visible, dlg.LocalTopRow.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.ModSearchNoResults.Visibility);
            dlg.Close();
        });
    }

    /// <summary>The keywords widen what is found, not the rule: nonsense still finds nothing.</summary>
    [Fact]
    public void NonsenseStillFindsNothing()
    {
        Run("es", () =>
        {
            var dlg = Build();
            dlg.ModSearchBox.Text = "qqqzzzxxx";
            Assert.Equal(Visibility.Visible, dlg.ModSearchNoResults.Visibility);
            dlg.Close();
        });
    }

    /// <summary>
    /// The card reads as two steps: Verify, then the diagnostics block with its own solid button.
    /// View logs and Discord sit below as the secondary row — and Share diagnostics is no longer
    /// one of them.
    /// </summary>
    [Fact]
    public void TheCardIsTwoStepsWithDiagnosticsInItsOwnBlock()
    {
        Run("en", () =>
        {
            var dlg = Build();
            Assert.True(IsUnder(dlg.ShareDiagnosticsBtn, dlg.DiagStepBlock),
                "Share diagnostics must sit in its own block");
            Assert.Same(Application.Current.FindResource("SetDiagButton"), dlg.ShareDiagnosticsBtn.Style);
            Assert.False(IsUnder(dlg.ShareDiagnosticsBtn, dlg.TroubleGrid),
                "Share diagnostics went back into the secondary row");
            Assert.True(IsUnder(dlg.ViewLogsBtn, dlg.TroubleGrid));
            Assert.True(IsUnder(dlg.SupportLinkHost, dlg.TroubleGrid));
            Assert.False(string.IsNullOrWhiteSpace(dlg.DiagStep2Title.Text));
            Assert.False(string.IsNullOrWhiteSpace(dlg.DiagStep2Body.Text));
            Assert.IsType<Button>(dlg.SupportLinkHost.Content);
            dlg.Close();
        });
    }

    /// <summary>
    /// The rail box runs the SAME action as the card's button, and does not navigate: the
    /// section that was open stays open.
    /// </summary>
    [Fact]
    public void TheRailBoxRunsTheSameActionAndStaysPut()
    {
        Run("en", () =>
        {
            var calls = 0;
            var dlg = Build(() => calls++);
            var generalWas = dlg.GeneralPanel.Visibility;

            dlg.RailDiagBox.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dlg.ShareDiagnosticsBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(2, calls);
            Assert.Equal(generalWas, dlg.GeneralPanel.Visibility);
            Assert.False(string.IsNullOrWhiteSpace(dlg.RailDiagTag.Text));
            Assert.False(string.IsNullOrWhiteSpace(dlg.RailDiagCaption.Text));
            dlg.Close();
        });
    }

    /// <summary>
    /// The results list (handoff 50b): what it offers for a query, in both languages. Share
    /// diagnostics comes first, and the keyword printed after the path is the one that actually
    /// matched — "diagnóstico" for somebody who typed it without the accent.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TheListOffersBothForDiagnostico_ShareDiagnosticsFirst(string lang)
    {
        Run(lang, () =>
        {
            var dlg = Build();
            var hits = dlg.SearchHitsFor("diagnostico");

            Assert.Equal(2, hits.Count);
            Assert.Same(dlg.ShareDiagnosticsBtn, hits[0].Target.Element);
            Assert.Same(dlg.ViewLogsBtn, hits[1].Target.Element);
            Assert.Equal("diagnóstico", hits[0].Keyword);
            Assert.True(hits[0].Target.IsDiagnostics);
            Assert.Same(dlg.DiagStepBlock, hits[0].Target.RingTarget);
            dlg.Close();
        });
    }

    /// <summary>The path reads as a sentence, not as the rail's capitals.</summary>
    [Theory]
    [InlineData("LOCAL FILES", "Local files")]
    [InlineData("ARCHIVOS LOCALES", "Archivos locales")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ThePathUsesSentenceCase(string? label, string expected)
        => Assert.Equal(expected, ModPropertiesDialog.SentenceCase(label));

    /// <summary>Each query offers what it names, and nothing it does not.</summary>
    [Theory]
    [InlineData("crash", 1)]
    [InlineData("reporte", 1)]
    [InlineData("logs", 1)]
    [InlineData("error", 2)]
    [InlineData("carpeta", 0)]
    [InlineData("", 0)]
    public void TheListOffersWhatTheQueryNames(string query, int expected)
    {
        Run("en", () =>
        {
            var dlg = Build();
            Assert.Equal(expected, dlg.SearchHitsFor(query).Count);
            dlg.Close();
        });
    }

    /// <summary>
    /// Typing fills the count beside the heading; clearing the search takes it away again. The
    /// window is never shown here, so the list itself stays closed — a popup is a window of its
    /// own and a test must not throw one onto the desktop.
    /// </summary>
    [Fact]
    public void TheCountFollowsTheQuery()
    {
        Run("es", () =>
        {
            var dlg = Build();
            dlg.ModSearchBox.Text = "diagnostico";
            Assert.Equal(Visibility.Visible, dlg.ModSearchCount.Visibility);
            Assert.Contains("2", dlg.ModSearchCount.Text);
            Assert.Contains("diagnostico", dlg.ModSearchCount.Text);

            dlg.ModSearchBox.Text = "crash";
            Assert.StartsWith("1 ", dlg.ModSearchCount.Text);

            dlg.ModSearchBox.Text = "";
            Assert.Equal(Visibility.Collapsed, dlg.ModSearchCount.Visibility);
            dlg.Close();
        });
    }

    /// <summary>
    /// Opening a result from another section brings its section to the front — the filter
    /// that found it stays as it was.
    /// </summary>
    [Fact]
    public void OpeningAResultBringsItsSectionForward()
    {
        Run("en", () =>
        {
            var dlg = Build();
            dlg.TabGeneralBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, dlg.LocalFilesPanel.Visibility);

            var hit = dlg.SearchHitsFor("crash").Single();
            dlg.OpenSearchHit(hit);

            Assert.Equal(Visibility.Visible, dlg.LocalFilesPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.GeneralPanel.Visibility);
            Assert.Equal(dlg.TabLocalFilesLabel.Text, dlg.ModSectionTitle.Text);
            dlg.Close();
        });
    }

    /// <summary>
    /// The rail box never widens the rail, in either language. The rail is an Auto column
    /// measured at infinity, so without a ceiling the box's caption would decide the width of
    /// every page — the defect the footer already had to be fixed for. Measured at INFINITY,
    /// because a finite constraint reports an overflow as a fit.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void TheRailBoxNeverWidensTheRail(string lang)
    {
        Run(lang, () =>
        {
            var ceiling = (double)Application.Current.FindResource("SetModRailWidth");
            var plain = Build();
            plain.ModRail.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var wordy = Build();
            wordy.RailDiagCaption.Text = string.Concat(Enumerable.Repeat(Strings.Get("ModPropShareDiagnostics") + " ", 6));
            wordy.RailDiagTag.Text = string.Concat(Enumerable.Repeat(Strings.Get("ModPropRailProblems") + " ", 6));
            wordy.ModRail.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Assert.True(wordy.ModRail.DesiredSize.Width <= ceiling + 0.5,
                $"the diagnostics box grew the rail to {wordy.ModRail.DesiredSize.Width:F0} px against {ceiling:F0}");
            Assert.Equal(plain.ModRail.DesiredSize.Width, wordy.ModRail.DesiredSize.Width, 1);
            plain.Close();
            wordy.Close();
        });
    }
}
