using System.IO;
using System.Windows;
using System.Windows.Controls;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The offer to install a fixed copy of the launcher (design 100a/100b), shown when auto-start
/// would otherwise point at a portable exe. It names what it does, where and how much, because
/// "copy" is the first thing anybody asks about. With no room for the copy the install is off and
/// "Not now" stays. Returns the choice via <c>ShowDialog()</c>: <c>true</c> = install.
/// </summary>
public partial class SelfInstallPromptDialog : Window
{
    private static readonly string[] Steps =
        { "DlgSelfInstallStepFolder", "DlgSelfInstallStepNoService", "DlgSelfInstallStepEntry" };

    public SelfInstallPromptDialog()
        : this(SelfInstallService.CanonicalDir, SelfInstallService.PayloadBytes(),
            DiskSpaceService.SafeFreeSpace(SelfInstallService.CanonicalDir))
    {
    }

    internal SelfInstallPromptDialog(string destination, long sizeBytes, long freeBytes)
    {
        InitializeComponent();
        ModDialogChrome.AttachGlobal(this);

        var title = Strings.Get("DlgSettingsBgInstallPromptTitle");
        Title = title;
        Chrome.Title = title;
        HeadlineText.Text = Strings.Get("DlgSelfInstallHeadline");
        BodyText.Text = Strings.Get("DlgSettingsBgInstallPromptBody");
        WhatItDoesLabel.Text = Strings.Get("DlgSelfInstallWhatItDoes");
        DestinationLabel.Text = Strings.Get("DlgSelfInstallDestination");
        NotNowNote.Text = Strings.Get("DlgSelfInstallNotNow");
        YesButton.Content = Strings.Get("DlgSettingsBgInstallPromptYes");
        NoButton.Content = Strings.Get("DlgSettingsBgInstallPromptNo");
        foreach (var step in Steps) AddStep(Strings.Get(step));

        DestinationPath.Text = PathDisplay.BreakAtSeparators(destination);
        var drive = (Path.GetPathRoot(destination) ?? "").TrimEnd('\\');
        var size = DiskSpaceService.FormatBytes(sizeBytes);
        DestinationSize.Text = freeBytes >= 0
            ? Strings.Format("DlgSelfInstallSize", size, DiskSpaceService.FormatBytes(freeBytes), drive)
            : size;

        if (DiskSpaceService.IsShort(freeBytes, sizeBytes))
        {
            SpaceWarningText.Text = Strings.Format("DlgSelfInstallNoRoom", drive,
                DiskSpaceService.FormatBytes(sizeBytes - freeBytes));
            SpaceWarning.Visibility = Visibility.Visible;
            YesButton.IsEnabled = false;
        }

        ModDialogChrome.SetLineHeight(1.25, HeadlineText);
        ModDialogChrome.SetLineHeight(1.55, BodyText);
        ModDialogChrome.SetLineHeight(1.45, DestinationPath, SpaceWarningText, NotNowNote);
    }

    private void AddStep(string text)
    {
        var row = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(ModDialogChrome.TickMark());

        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 0, 0) };
        line.SetResourceReference(TextBlock.FontSizeProperty, "DialogBodySize");
        line.SetResourceReference(TextBlock.ForegroundProperty, "WindowSoftText");
        ModDialogChrome.AddMarkedText(line, text);
        line.LineHeight = (double)FindResource("DialogBodySize") * 1.5;
        Grid.SetColumn(line, 1);
        row.Children.Add(line);
        StepsList.Children.Add(row);
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
