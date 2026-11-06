using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using Xunit;

namespace WarsOfLibertyLauncher.Tests;

/// <summary>
/// docs/design_archivos_antivirus (48-49): the uninstall window, the antivirus exclusion
/// dialog and the LOCAL FILES tab. None of them is opened by the smoke launch, and the first
/// two are only reachable after committing to a delete or after an antivirus has eaten a
/// file — so these are the only automated check that they parse, and the place the rules
/// that are easy to lose are pinned.
/// </summary>
[Collection("wpf-and-language")]
public class FilesAndAntivirusDialogTests
{
    private static void Run(System.Action body)
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            Strings.SetLanguage("en");
            try { body(); }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    // ---------------------------------------------------------------- uninstall (49b / 49d)

    /// <summary>49d: when the plan refuses, nothing that could act is left on screen —
    /// hidden, never merely disabled — and Close is the only way out.</summary>
    [Theory]
    [InlineData(UninstallMode.NotAValidInstall, false)]
    [InlineData(UninstallMode.NotAValidInstall, true)]
    [InlineData(UninstallMode.NothingToDo, false)]
    public void ARefusedPlanHidesEveryActionAndOffersOnlyClose(UninstallMode mode, bool containsBaseGame)
    {
        Run(() =>
        {
            var plan = new UninstallPlan(mode, @"D:\Games\Wars of Liberty (2)", 0, 0,
                ContainsBaseGame: containsBaseGame);
            var dlg = new UninstallDialog(plan, "Wars of Liberty", @"data\stringtabley.xml", "");

            Assert.Equal(Visibility.Collapsed, dlg.ConfirmPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.OkButton.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.CancelButton.Visibility);
            Assert.Equal(Visibility.Visible, dlg.CloseButton.Visibility);
            Assert.Equal(Visibility.Visible, dlg.RefusedPanel.Visibility);
            dlg.Close();
        });
    }

    [Fact]
    public void AValidPlanCountsWithThousandsSeparatorsAndSaysSafetyOnce()
    {
        Run(() =>
        {
            var plan = new UninstallPlan(UninstallMode.Valid, @"D:\Games\Wars of Liberty (2)", 65057, 4130);
            var dlg = new UninstallDialog(plan, "Wars of Liberty", @"data\stringtabley.xml", "",
                copyLabel: "", otherCopies: 0, isCopy: false);

            Assert.Equal("65,057 files · 4,130 folders", dlg.CountsText.Text);
            Assert.Equal("Uninstall Wars of Liberty?", dlg.HeaderText.Text);
            Assert.Equal("Uninstall", dlg.TitleBarControl.Title);
            // Without other copies the window must not promise anything about them.
            Assert.DoesNotContain("copies", dlg.SafeNoteText.Text);
            // The path breaks at its separators, not at its spaces.
            Assert.Contains("\u200B", dlg.InstallPathText.Text);
            Assert.DoesNotContain(" ", dlg.InstallPathText.Text);
            dlg.Close();
        });
    }

    /// <summary>A copy that is not the active one never offers the saved-games cleanup:
    /// every copy of a mod shares one My Games folder. And its options say not to fall back
    /// on the profile, or a manifest-less copy would take the ACTIVE copy's shortcuts and
    /// Windows entry with it.</summary>
    [Fact]
    public void UninstallingACopyNeverTouchesSharedData()
    {
        Run(() =>
        {
            var plan = new UninstallPlan(UninstallMode.Valid, @"D:\Games\Wars of Liberty (2)", 10, 2,
                UserDataFileCount: 12);
            var dlg = new UninstallDialog(plan, "Wars of Liberty", @"data\stringtabley.xml", "",
                copyLabel: "Wars of Liberty (2)", otherCopies: 2, isCopy: true);

            Assert.Equal(Visibility.Collapsed, dlg.RowUserData.Visibility);
            Assert.Contains("copies", dlg.SafeNoteText.Text);
            dlg.OptDeleteUserData.IsChecked = true;   // even ticked, a hidden option stays off
            var options = dlg.BuildOptions();
            Assert.False(options.AllowProfileFallbacks);
            Assert.False(options.DeleteUserDataFiles);
            dlg.Close();
        });
    }

    /// <summary>The "reset launcher settings" option is gone from this window: it deleted
    /// the WHOLE launcher config and was then overwritten by the next save, so it reset
    /// nothing while claiming to. Guards against it coming back as a field.</summary>
    [Fact]
    public void TheResetConfigOptionIsGone()
    {
        Assert.Null(typeof(UninstallDialog).GetField("OptResetConfig",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.Null(typeof(UninstallOptions).GetProperty("ResetConfig"));
    }

    // ---------------------------------------------------------------- antivirus (48a / 48b)

    [Fact]
    public void TheNoticeHasOneSolidButtonAndCancelIsAGhost()
    {
        Run(() =>
        {
            var dlg = new AntivirusExclusionDialog("Wars of Liberty", @"AI3\wolai.upl",
                @"C:\Games\Age Of Empires 3\Wars of Liberty", preventive: true);

            Assert.Same(Application.Current.FindResource("SetFooterBarGhostButton"), dlg.CancelButton.Style);
            Assert.Same(Application.Current.FindResource("SetFooterSolidButton"), dlg.ContinueButton.Style);
            Assert.Equal(Visibility.Visible, dlg.DontShowAgainCheck.Visibility);
            Assert.Equal(Visibility.Visible, dlg.InstallRow.Visibility);
            // Paths break at the backslash, never between "Age Of" and "Empires 3".
            Assert.DoesNotContain(" ", dlg.InstallPathText.Text);
            dlg.Close();
        });
    }

    /// <summary>48b: the person who hit the failure gets the paths no matter what they
    /// dismissed before — no checkbox, no Cancel, just Close.</summary>
    [Fact]
    public void TheBlockedModeOffersNoWayToSilenceIt()
    {
        Run(() =>
        {
            var dlg = new AntivirusExclusionDialog("", @"AI3\wolai.upl", "", preventive: false);

            Assert.Equal(Visibility.Collapsed, dlg.DontShowAgainCheck.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.CancelButton.Visibility);
            // No install folder known: its row is hidden rather than shown empty.
            Assert.Equal(Visibility.Collapsed, dlg.InstallRow.Visibility);
            Assert.Equal("danger", dlg.FileTag.Tag);
            dlg.Close();
        });
    }

    /// <summary>The reference draws the row labels in a 92 px column, and "Carpeta del mod"
    /// does not fit it — it was cut to "Carpeta del ı" on screen. The column is as wide as
    /// the longer label now; a label is never what gets clipped.</summary>
    [Fact]
    public void TheFolderLabelsFitInSpanish()
    {
        var error = DialogXamlTests.RunOnStaThread(() =>
        {
            DialogXamlTests.EnsureResources();
            var previous = Strings.Language;
            Strings.SetLanguage("es");
            try
            {
                var dlg = new AntivirusExclusionDialog("Wars of Liberty", @"AI3\wolai.upl",
                    @"C:\Games\Age Of Empires 3\Wars of Liberty", preventive: true);
                // Each label has the row's whole width above its path, so nothing can clip it;
                // a fixed column beside the path is exactly what did.
                foreach (var label in new[] { dlg.TempRowLabel, dlg.InstallRowLabel })
                    Assert.IsType<StackPanel>(label.Parent);
                Assert.Equal("Carpeta del mod", dlg.InstallRowLabel.Text);
                dlg.Close();
            }
            finally { Strings.SetLanguage(previous); }
        });
        Assert.Null(error);
    }

    // ---------------------------------------------------------------- LOCAL FILES (49a)

    private static ModPropertiesDialog BuildModWindow(ModProfile profile)
    {
        var config = new LauncherConfig();
        return new ModPropertiesDialog(
            profile, new UpdateService(config, profile), config,
            translationIndex: null,
            applyTranslation: _ => { }, revertToEnglish: () => { },
            openVerify: () => { }, openRepair: () => { },
            checkForUpdates: () => Task.FromResult<UpdateService.CheckResult?>(null),
            openAoE3Folder: () => { }, changeAoE3Folder: () => { },
            openUserDataFolder: () => { }, createBackup: () => null, restoreBackup: () => null,
            viewLogs: () => { }, shareDiagnostics: () => { }, uninstall: () => { },
            uninstallCopy: _ => Task.CompletedTask);
    }

    /// <summary>The danger zone at the bottom is gone: Uninstall… sits on the active copy
    /// it acts on, and nothing named after the old section is left behind.</summary>
    [Fact]
    public void LocalFilesPutsUninstallOnTheActiveCopy()
    {
        Run(() =>
        {
            var dlg = BuildModWindow(ModRegistry.Default);
            Assert.NotNull(dlg.ActiveCopyCard);
            Assert.True(IsInside(dlg.UninstallBtn, dlg.ActiveCopyCard));
            Assert.Null(typeof(ModPropertiesDialog).GetField("LblDangerZone",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
            Assert.Equal(Visibility.Visible, dlg.CopiesSection.Visibility);
            Assert.Equal(1120, dlg.Width);
            dlg.Close();
        });
    }

    /// <summary>
    /// Design 71: one row per other copy, the note with the count, the notice when another copy
    /// is newer, and a menu whose three actions reach the right copy.
    /// </summary>
    [Fact]
    public void TheCopiesListShowsEachCopyAndItsMenuActsOnThatCopy()
    {
        Run(() =>
        {
            var was = Strings.Language;
            Strings.SetLanguage("es");
            try
            {
                var config = new LauncherConfig();
                var state = config.GetState(ModRegistry.Default.Id);
                state.InstallPath = @"C:\Juegos\Principal";
                state.LastKnownVersion = "1.2.0d";
                state.OtherInstalls.Add(new ModInstall { Id = "t", InstallPath = @"D:\Juegos\Torneo", LastKnownVersion = "1.2.0e" });

                string? removed = null, uninstalled = null;
                var dlg = new ModPropertiesDialog(
                    ModRegistry.Default, new UpdateService(config, ModRegistry.Default), config,
                    translationIndex: null, applyTranslation: _ => { }, revertToEnglish: () => { },
                    openVerify: () => { }, openRepair: () => { },
                    checkForUpdates: () => Task.FromResult<UpdateService.CheckResult?>(null),
                    openAoE3Folder: () => { }, changeAoE3Folder: () => { },
                    openUserDataFolder: () => { }, createBackup: () => null, restoreBackup: () => null,
                    viewLogs: () => { }, shareDiagnostics: () => { }, uninstall: () => { },
                    removeInstall: id => removed = id,
                    uninstallCopy: id => { uninstalled = id; return Task.CompletedTask; });

                Assert.Single(dlg.ManageInstallsHost.Children);
                Assert.StartsWith("2 copias", dlg.LblManageInstallsDesc.Text);
                Assert.Equal(Visibility.Visible, dlg.LblCopiesIntro.Visibility);
                Assert.Equal(Visibility.Visible, dlg.NewerCopyNotice.Visibility);
                Assert.Contains("Torneo", dlg.NewerCopyText.Text);
                Assert.Contains("v1.2.0e", dlg.NewerCopyText.Text);

                var menu = dlg.BuildCopyMenu(new Button(), "t", "Torneo", @"D:\Juegos\Torneo", "1.2.0e");
                var rows = menu.Items.OfType<MenuItem>().ToList();
                Assert.Equal(new object[] { "Torneo · v1.2.0e", "Abrir carpeta", "Quitar de la lista", "Desinstalar…" },
                    rows.Select(r => r.Header).ToArray());
                rows[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                rows[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.Equal("t", removed);
                Assert.Equal("t", uninstalled);
                dlg.Close();
            }
            finally { Strings.SetLanguage(was); }
        });
    }

    /// <summary>The base game is the player's own AoE3: no copies, no uninstall, no repair —
    /// hidden whole, so no empty card frame is left on the page.</summary>
    [Fact]
    public void TheStockGameHidesEverythingThatCouldActOnIt()
    {
        Run(() =>
        {
            var stock = ModRegistry.Find("aoe3-tad");
            Assert.NotNull(stock);
            var dlg = BuildModWindow(stock!);
            Assert.Equal(Visibility.Collapsed, dlg.UninstallBtn.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.RepairBtn.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.VerifyBtn.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.CopiesSection.Visibility);
            Assert.Equal(Visibility.Collapsed, dlg.MaintenanceGroup.Visibility);
            dlg.Close();
        });
    }

    private static bool IsInside(DependencyObject child, DependencyObject ancestor)
    {
        for (var d = child; d != null; d = LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }
}
