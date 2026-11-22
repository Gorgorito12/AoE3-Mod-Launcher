using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarsOfLibertyLauncher.Controls;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;
using WarsOfLibertyLauncher.Services.Multiplayer;

namespace WarsOfLibertyLauncher;

/// <summary>
/// The automatic entry to the LAN room (<see cref="LanHelperService"/>), when the launcher could
/// not put it in place by itself, or the player asks from the room's chip.
///
/// <para>Two ways, in this order. The footer's primary is the launcher doing it: an ordinary copy
/// when the folder is writable, or one administrator permission when Windows protects it — the
/// launcher runs itself elevated for that single copy (<see cref="LanHelperElevated"/>) and does
/// not restart. The card is the player doing it: save the file, open the exact folder, copy
/// it in, and Check, which reads the folder the way the room does.</para>
///
/// <para>Nothing here installs over another tool's <c>avifil32.dll</c> or a mod that already ships
/// the key: those states have no primary and no steps, only a sentence saying why.</para>
/// </summary>
public partial class LanHelperSetupDialog : BaseDialog
{
    private readonly ModProfile _profile;
    private readonly string _exe;
    private readonly string _folder;
    private DispatcherTimer? _flashTimer;
    private bool _busy;
    private readonly LanHelperState? _previewState;
    private readonly bool? _previewWritable;

    private LanHelperState ProbeNow() => _previewState ?? LanHelperService.Probe(_exe);
    private bool FolderWritable() => _previewWritable ?? ElevationService.CanWriteTo(_folder);

    /// <summary>The helper is in place and the key enabled when the window closed.</summary>
    public bool Active { get; private set; }

    internal Button PrimaryButton => PrimaryAction;

    /// <param name="failure">Why the automatic copy failed, in the launcher's words; null when the
    /// window was opened from the room's chip.</param>
    public LanHelperSetupDialog(ModProfile profile, string exePath, string? failure)
        : this(profile, exePath, failure, null, null) { }

    /// <summary>The preview's way in (<c>--preview-dialog=lanhelper:manual</c>): a state and a
    /// writable folder to paint, without reading or writing any game folder.</summary>
    internal LanHelperSetupDialog(ModProfile profile, string exePath, string? failure,
        LanHelperState? previewState, bool? previewWritable)
    {
        _previewState = previewState;
        _previewWritable = previewWritable;
        InitializeComponent();
        _profile = profile;
        _exe = exePath;
        _folder = Path.GetDirectoryName(exePath) ?? exePath;

        SetBarTitle(Strings.Get("LanSetupBar"));
        UseMod(profile);
        StepsTitle.Text = Strings.Get("LanSetupStepsTitle");
        CancelAction.Content = Strings.Get("LanSetupClose");
        Closed += (_, _) => _flashTimer?.Stop();
        Paint(failure);
    }

    internal static bool ShowFor(Window? owner, ModProfile profile, string exePath, string? failure)
    {
        var dialog = new LanHelperSetupDialog(profile, exePath, failure);
        dialog.ShowOver(owner);
        return dialog.Active;
    }

    /// <summary>Everything that depends on the folder's state, read again after every action.</summary>
    private void Paint(string? failure)
    {
        var state = ProbeNow();
        Active = state is LanHelperState.Installed or LanHelperState.EnabledByMod;
        var writable = FolderWritable();
        var canAct = state is LanHelperState.Missing or LanHelperState.Outdated;

        SetHeadline(Strings.Format(state switch
        {
            LanHelperState.Installed or LanHelperState.EnabledByMod => "LanSetupTitleActive",
            _ when failure != null => "LanSetupTitleFailed",
            _ => "LanSetupTitle",
        }, _profile.DisplayName));
        SetLead(state switch
        {
            LanHelperState.Installed => Strings.Get("LanSetupLeadActive"),
            LanHelperState.EnabledByMod => Strings.Get("LanSetupLeadByMod"),
            LanHelperState.ForeignDll => Strings.Get("LanSetupLeadForeign"),
            LanHelperState.Unsupported or LanHelperState.SupersededByMod => Strings.Get("LanSetupLeadUnsupported"),
            _ when !writable => Strings.Get("LanSetupLeadProtected"),
            _ when failure != null => Strings.Format("LanSetupLeadFailed", failure),
            _ => Strings.Get("LanSetupLeadOff"),
        });
        Tone = Active ? DialogTone.Done : canAct ? DialogTone.Caution : DialogTone.Info;
        if (Active || !canAct) HidePrimary();
        else
        {
            PrimaryAction.Visibility = Visibility.Visible;
            PrimaryAction.Content = Strings.Get(writable ? "LanSetupCopyNow" : "LanSetupAsAdmin");
            PrimaryAction.ToolTip = TooltipHelper.Wrap(Strings.Get(writable ? "LanSetupCopyNowTip" : "LanSetupAsAdminTip"));
        }
        StepsCard.Visibility = canAct ? Visibility.Visible : Visibility.Collapsed;
        if (canAct && StepsHost.Children.Count == 0) BuildSteps();
    }

    private void BuildSteps()
    {
        StepsHost.Children.Clear();
        AddStep(1, Strings.Get("LanSetupStep1"), Button("LanSetupSave", "LanSetupSave", SaveFiles_Click));
        var path = new TextBlock
        {
            Text = PathDisplay.BreakAtSeparators(_folder),
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = (double)FindResource("SetDescSize"),
            Foreground = (Brush)FindResource("UiTextStrong"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = Button("LanSetupCopyPath", "LanSetupCopyPath", CopyPath_Click);
        buttons.Children.Add(copy);
        var open = Button("LanSetupOpenFolder", "LanSetupOpenFolder", OpenFolder_Click);
        open.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(open);
        AddStep(2, Strings.Get("LanSetupStep2"), buttons, path);
        AddStep(3, Strings.Format("LanSetupStep3", LanHelperService.DllName, Path.GetFileName(_exe)), null);
        AddStep(4, Strings.Get("LanSetupStep4"), Button("LanSetupCheck", "LanSetupCheck", Check_Click));
    }

    private Button Button(string automationId, string captionKey, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = Strings.Get(captionKey),
            Style = (Style)FindResource("WindowSecondaryButton"),
            MinWidth = 92,
            VerticalAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += click;
        return button;
    }

    /// <summary>A numbered row: the number, the words (and what goes under them), the action.</summary>
    private void AddStep(int number, string words, FrameworkElement? action, FrameworkElement? under = null)
    {
        var row = new Grid { Margin = new Thickness(0, number == 1 ? 0 : 12, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("UiCardRim"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = (double)FindResource("SetGroupLabelSize"),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("UiTextDim"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        var text = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var line = new TextBlock
        {
            FontSize = (double)FindResource("SetControlSize"),
            Foreground = (Brush)FindResource("MpTextBody"),
            TextWrapping = TextWrapping.Wrap,
        };
        MarkedText.Set(line, words);
        text.Children.Add(line);
        if (under != null) text.Children.Add(under);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (action != null)
        {
            action.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(action, 2);
            row.Children.Add(action);
        }
        StepsHost.Children.Add(row);
    }

    protected override void OnPrimary() => _ = DoItAsync();

    /// <summary>The launcher does it: a plain copy, or one elevated copy for a protected folder.</summary>
    private async Task DoItAsync()
    {
        if (_busy) return;
        if (_previewState != null) { Say(true, Strings.Format("LanSetupActive", _profile.DisplayName)); return; }
        _busy = true;
        PrimaryAction.IsEnabled = false;
        try
        {
            if (ElevationService.CanWriteTo(_folder))
            {
                var problem = LanHelperService.Install(_exe);
                if (problem != null) Say(false, Strings.Format("LanSetupCopyFailed", problem));
            }
            else
            {
                var args = LanHelperElevated.BuildInstallArguments(_exe);
                if (args == null)
                {
                    Say(false, Strings.Get("LanSetupAdminCannot"));
                }
                else
                {
                    var run = await ElevationService.RunSelfElevatedAsync(args, TimeSpan.FromMinutes(2));
                    DiagnosticLog.Write($"LanHelper: elevated copy into {_folder} -> {run.Status} {run.ExitCode}");
                    if (run.Status == ElevationService.ElevatedRunStatus.Declined)
                        Say(false, Strings.Get("LanSetupAdminDeclined"));
                }
            }
        }
        finally
        {
            _busy = false;
            PrimaryAction.IsEnabled = true;
        }
        AfterAction();
    }

    /// <summary>Reads the folder again and says what it found, whatever the action reported.</summary>
    private void AfterAction()
    {
        var state = LanHelperService.Probe(_exe);
        if (state is LanHelperState.Installed or LanHelperState.EnabledByMod)
        {
            Say(true, Strings.Format("LanSetupActive", _profile.DisplayName));
            Paint(null);
        }
        else if (StatusRow.Visibility != Visibility.Visible)
        {
            Say(false, Strings.Get("LanSetupNotYet"));
        }
    }

    private void Say(bool good, string text)
    {
        StatusRow.Visibility = Visibility.Visible;
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(good ? "MpOkText" : "MpCautionText");
        StatusDot.Fill = (Brush)FindResource(good ? "MpOk" : "MpCaution");
    }

    private void SaveFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_previewState != null) { Say(true, Strings.Format("LanSetupSaved", @"C:\Users\Player\Desktop\" + LanHelperService.ExportFolderName)); return; }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var parent = Directory.Exists(desktop) ? desktop : AppPaths.DataDir;
        var folder = LanHelperService.ExportTo(parent,
            Strings.Format("LanSetupReadme", _profile.DisplayName, _folder, LanHelperService.DllName),
            out var error);
        if (folder == null)
        {
            Say(false, Strings.Format("LanSetupSaveFailed", error ?? ""));
            return;
        }
        Say(true, Strings.Format("LanSetupSaved", folder));
        FileReveal.Reveal(Path.Combine(folder, LanHelperService.DllName));
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        try { Clipboard.SetText(_folder); }
        catch (Exception ex) { DiagnosticLog.Write($"LanHelper: copy path failed: {ex.Message}"); return; }
        var caption = Strings.Get("LanSetupCopyPath");
        button.Content = Strings.Get("LanSetupCopied");
        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _flashTimer.Tick += (_, _) => { _flashTimer?.Stop(); button.Content = caption; };
        _flashTimer.Start();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_previewState != null) return;
        try { HarnessMode.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true }); }
        catch (Exception ex) { DiagnosticLog.Write($"LanHelper: open folder failed: {ex.Message}"); }
    }

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        StatusRow.Visibility = Visibility.Collapsed;
        var state = ProbeNow();
        // Outdated: our file is there and enters; what is left of the earlier helper goes at the
        // next room, or with the button above.
        if (state is LanHelperState.Installed or LanHelperState.EnabledByMod or LanHelperState.Outdated)
        {
            Say(true, Strings.Format("LanSetupActive", _profile.DisplayName));
            Paint(null);
            return;
        }
        Say(false, Strings.Get(state == LanHelperState.ForeignDll ? "LanSetupLeadForeign" : "LanSetupMissingDll"));
    }
}
