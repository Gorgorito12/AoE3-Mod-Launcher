using System;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirmation for removing the LAUNCHER from this PC (Settings → Advanced). Everything it lists
/// as removed comes from <see cref="SelfInstallService.UninstallFacts"/>, i.e. is actually there;
/// everything it promises to leave alone — the mods, Age of Empires III, the saved games — the
/// uninstall never touches. The one irreversible choice, the data folder, starts unticked.
/// </summary>
public partial class SelfUninstallDialog : Window
{
    private readonly SelfInstallService.UninstallFacts _facts;
    private readonly bool _elevated;

    /// <summary>Whether the player asked for the data folder to go too. Read after a true result.</summary>
    public bool RemoveUserData { get; private set; }

    /// <param name="elevated">The launcher already runs as administrator, so removing leftover
    /// registry entries will not show a Windows prompt and the window does not announce one.</param>
    public SelfUninstallDialog(SelfInstallService.UninstallFacts facts, bool elevated)
    {
        InitializeComponent();
        Controls.ModDialogChrome.AttachGlobal(this);
        _facts = facts;
        _elevated = elevated;

        // Never taller than the screen: the body scrolls, the buttons stay where they can be pressed.
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 40);

        ApplyLanguage();
    }

    private bool Portable => !_facts.RunningInstalledCopy;

    private void ApplyLanguage()
    {
        var title = Strings.Get("DlgSelfUninstallTitle");
        Title = title;
        TitleBarControl.Title = title;
        HeaderText.Text = Strings.Get(Portable ? "DlgSelfUninstallHeadlinePortable" : "DlgSelfUninstallHeadline");
        LeadText.Text = Strings.Get(!Portable ? "DlgSelfUninstallLead"
            : _facts.InstalledCopyDir != null ? "DlgSelfUninstallLeadPortable" : "DlgSelfUninstallLeadPortableAlone");

        RemovedLabelText.Text = Strings.Get("DlgSelfUninstallRemovedLabel");
        BuildRemovedList();

        // Named, because what stays is the point: "your mods" is a promise nobody can check, while
        // the list is the one the player can read and recognise.
        var hasMods = _facts.InstalledMods.Count > 0;
        SafeNoteText.Text = hasMods
            ? Strings.Format("DlgSelfUninstallSafeMods", JoinNames(_facts.InstalledMods))
            : Strings.Get("DlgSelfUninstallSafeNoMods");
        ModsHintText.Text = Strings.Get("DlgSelfUninstallModsHint");
        ModsHintText.Visibility = hasMods ? Visibility.Visible : Visibility.Collapsed;

        PortablePanel.Visibility = Portable && !string.IsNullOrWhiteSpace(_facts.RunningExe)
            ? Visibility.Visible : Visibility.Collapsed;
        PortableLabelText.Text = Strings.Get("DlgSelfUninstallPortableLabel");
        PortableNoteText.Text = Strings.Get("DlgSelfUninstallPortableStays");
        PortablePathText.Text = PathDisplay.BreakAtSeparators(_facts.RunningExe);

        DataPanel.Visibility = _facts.CanRemoveUserData ? Visibility.Visible : Visibility.Collapsed;
        DataLabelText.Text = Strings.Get("DlgSelfUninstallDataLabel");
        OptDataTitle.Text = Strings.Get("DlgSelfUninstallDataTitle");
        OptDataDesc.Text = Strings.Get("DlgSelfUninstallDataDesc");
        OptDataPath.Text = PathDisplay.BreakAtSeparators(_facts.DataDir);
        if (_facts.PendingUploads > 0)
        {
            OptDataPending.Text = _facts.PendingUploads == 1
                ? Strings.Get("DlgSelfUninstallDataPendingOne")
                : Strings.Format("DlgSelfUninstallDataPending", Count(_facts.PendingUploads));
            OptDataPending.Visibility = Visibility.Visible;
        }
        if (_facts.HasRepairBackups)
        {
            OptDataBackups.Text = Strings.Get("DlgSelfUninstallDataBackups");
            OptDataBackups.Visibility = Visibility.Visible;
        }

        AntivirusNoteText.Text = Strings.Get("DlgSelfUninstallAntivirus");
        CancelButton.Content = Strings.Get("BtnCancel");
        UpdateConfirmCaption();

        Controls.ModDialogChrome.SetLineHeight(1.25, HeaderText);
        Controls.ModDialogChrome.SetLineHeight(1.55, LeadText);
        Controls.ModDialogChrome.SetLineHeight(1.45, SafeNoteText, ModsHintText, PortablePathText, PortableNoteText,
            OptDataDesc, OptDataPath, OptDataPending, OptDataBackups, AntivirusNoteText);
    }

    /// <summary>What cannot be undone is read on the button that does it.</summary>
    private void UpdateConfirmCaption()
    {
        OkButton.Content = Strings.Get(OptDeleteData.IsChecked == true ? "DlgSelfUninstallConfirmData"
            : Portable ? "DlgSelfUninstallConfirmPortable" : "DlgSelfUninstallConfirm");
    }

    private void OptDeleteData_Changed(object sender, RoutedEventArgs e) => UpdateConfirmCaption();

    private static string JoinNames(System.Collections.Generic.IReadOnlyList<string> names)
    {
        if (names.Count < 2) return string.Join("", names);
        return string.Join(", ", System.Linq.Enumerable.Take(names, names.Count - 1))
            + Strings.Get("DlgSelfUninstallAnd") + names[^1];
    }

    private static string Count(int n) => n.ToString("N0", UninstallDialog.CountCulture());

    /// <summary>One bullet per thing that goes — and only for what is there.</summary>
    private void BuildRemovedList()
    {
        RemovedList.Children.Clear();
        if (_facts.InstalledCopyDir != null)
            AddItem(Strings.Get(Portable ? "DlgSelfUninstallItemOtherCopy" : "DlgSelfUninstallItemProgram"),
                _facts.InstalledCopyDir, detailIsPath: true);
        if (_facts.ShortcutsPresent)
            AddItem(Strings.Get("DlgSelfUninstallItemShortcuts"));
        AddItem(Strings.Get("DlgSelfUninstallItemStartup"));
        if (_facts.RemovesTempFiles)
            AddItem(Strings.Get("DlgSelfUninstallItemTemp"));
        if (_facts.LeftoverFiles > 0)
            AddItem(Strings.Get("DlgSelfUninstallItemUpdateLeftovers"));
        if (_facts.LeftoverKeys > 0)
            AddItem(_facts.LeftoverKeys == 1
                    ? Strings.Get("DlgSelfUninstallItemKeysOne")
                    : Strings.Format("DlgSelfUninstallItemKeys", Count(_facts.LeftoverKeys)),
                _elevated ? null : Strings.Get("DlgSelfUninstallKeysUac"), detailIsPath: false);
    }

    private void AddItem(string text, string? detail = null, bool detailIsPath = false)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bullet = new Border
        {
            Width = 5,
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Margin = new Thickness(1, 7, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        bullet.SetResourceReference(Border.BackgroundProperty, "UiDangerText");
        row.Children.Add(bullet);

        var body = new StackPanel { Margin = new Thickness(9, 0, 0, 0) };
        var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium };
        line.SetResourceReference(TextBlock.FontSizeProperty, "SetRowTitleSize");
        line.SetResourceReference(TextBlock.ForegroundProperty, "WindowStrongText");
        Controls.ModDialogChrome.SetLineHeight(1.4, line);
        body.Children.Add(line);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var note = new TextBlock
            {
                Text = detailIsPath ? PathDisplay.BreakAtSeparators(detail) : detail,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            };
            if (detailIsPath) note.FontFamily = new System.Windows.Media.FontFamily("Consolas, Courier New, monospace");
            note.SetResourceReference(TextBlock.FontSizeProperty, "SetControlSize");
            note.SetResourceReference(TextBlock.ForegroundProperty, "WindowMuted");
            body.Children.Add(note);
        }
        Grid.SetColumn(body, 1);
        row.Children.Add(body);
        RemovedList.Children.Add(row);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        // A hidden option is never on, whatever its box last said.
        RemoveUserData = DataPanel.Visibility == Visibility.Visible && OptDeleteData.IsChecked == true;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
