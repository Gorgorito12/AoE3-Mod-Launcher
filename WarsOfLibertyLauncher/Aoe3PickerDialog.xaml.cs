using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Lets the user pick which AoE3:TAD installation to clone from when
/// installing Wars of Liberty. Lists every detected install with its source
/// (Steam/GOG/retail), full path, and approximate size, plus a "Browse..."
/// option for manual selection.
/// </summary>
public partial class Aoe3PickerDialog : Window
{
    /// <summary>The install the user selected. Set when DialogResult == true.</summary>
    public Aoe3DetectorService.Aoe3Install? SelectedInstall { get; private set; }

    public Aoe3PickerDialog(IReadOnlyList<Aoe3DetectorService.Aoe3Install> detected)
    {
        InitializeComponent();
        ApplyLanguage();

        // Populate the list with friendly labels
        foreach (var install in detected)
        {
            InstallsList.Items.Add(new InstallListEntry(install));
        }

        // Auto-select the first entry when there's any detected install,
        // so the common case is "click OK and go".
        if (InstallsList.Items.Count > 0)
            InstallsList.SelectedIndex = 0;
        else
            UpdateOkEnabled();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgAoe3PickerTitle");
        HeaderText.Text = Strings.Get("DlgAoe3PickerHeader");
        DescriptionText.Text = Strings.Get("DlgAoe3PickerDescription");
        BrowseManualButton.Content = Strings.Get("DlgAoe3PickerBrowse");
        OkButton.Content = Strings.Get("BtnContinue");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void InstallsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOkEnabled();
    }

    private void UpdateOkEnabled()
    {
        OkButton.IsEnabled = InstallsList.SelectedItem is InstallListEntry;
    }

    private void BrowseManualButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.Get("DlgAoe3PickerBrowse"),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        var picked = dialog.FolderName;

        // Validate that what they picked is actually a valid AoE3:TAD install
        if (!Aoe3DetectorService.IsValidAoe3Install(picked))
        {
            WarningText.Text = Strings.Get("WarnNotValidAoe3");
            WarningText.Visibility = Visibility.Visible;
            return;
        }

        WarningText.Visibility = Visibility.Collapsed;

        var size = Aoe3DetectorService.TryGetFolderSize(picked);
        var manual = new Aoe3DetectorService.Aoe3Install(
            Aoe3DetectorService.InstallSource.Manual, picked, size);

        // Add and pre-select the manual entry so the user can confirm
        var entry = new InstallListEntry(manual);
        InstallsList.Items.Add(entry);
        InstallsList.SelectedItem = entry;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (InstallsList.SelectedItem is InstallListEntry entry)
        {
            SelectedInstall = entry.Install;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// ListBox item type. Overrides ToString to control how each entry
    /// displays in the list — multiline so we can show source on top,
    /// path and size below.
    /// </summary>
    private sealed class InstallListEntry
    {
        public Aoe3DetectorService.Aoe3Install Install { get; }

        public InstallListEntry(Aoe3DetectorService.Aoe3Install install)
        {
            Install = install;
        }

        public override string ToString()
        {
            var sourceLabel = Install.Source switch
            {
                Aoe3DetectorService.InstallSource.Steam => "Steam",
                Aoe3DetectorService.InstallSource.Gog => "GOG",
                Aoe3DetectorService.InstallSource.MicrosoftGames => "Microsoft Games",
                Aoe3DetectorService.InstallSource.Manual => Strings.Get("AoeSourceManual"),
                _ => "?",
            };
            var size = FormatBytes(Install.ApproximateSizeBytes);
            return $"[{sourceLabel}]  {Install.Path}    ({size})";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "?";
            string[] units = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            return $"{size:0.#} {units[unit]}";
        }
    }
}
