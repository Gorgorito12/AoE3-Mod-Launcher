using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirmation dialog for uninstalling Wars of Liberty. Shows the install
/// path being targeted, a count of files / dirs that will be removed, and
/// lets the user toggle optional cleanup steps (shortcuts, registry, config).
/// If the path doesn't have the WoL marker we surface a red error panel
/// and refuse to proceed instead of touching files.
/// </summary>
public partial class UninstallDialog : Window
{
    public UninstallOptions Options { get; private set; } = new();

    private readonly UninstallPlan _plan;

    public UninstallDialog(UninstallPlan plan)
    {
        InitializeComponent();
        _plan = plan;

        ApplyLanguage();
        ApplyPlan();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgUninstallTitle");
        HeaderText.Text = Strings.Get("DlgUninstallHeader");
        DescriptionText.Text = Strings.Get("DlgUninstallDescription");
        LblInstallPath.Text = Strings.Get("DlgUninstallInstallPathLabel");
        OptionsTitleText.Text = Strings.Get("DlgUninstallOptionsTitle");
        OptDeleteShortcuts.Content = Strings.Get("DlgUninstallOptShortcuts");
        OptRemoveRegistry.Content = Strings.Get("DlgUninstallOptRegistry");
        OptResetConfig.Content = Strings.Get("DlgUninstallOptResetConfig");
        AoE3SafeNoteText.Text = Strings.Get("DlgUninstallAoE3SafeNote");
        OkButton.Content = Strings.Get("BtnUninstall");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void ApplyPlan()
    {
        InstallPathText.Text = _plan.InstallPath;

        switch (_plan.Mode)
        {
            case UninstallMode.Valid:
                SummaryPanel.Visibility = Visibility.Visible;
                SummaryDetailText.Text = Strings.Format(
                    "DlgUninstallValidDetail",
                    _plan.FileCount, _plan.DirectoryCount);
                OkButton.IsEnabled = true;
                break;

            case UninstallMode.NotAValidInstall:
                RefusedPanel.Visibility = Visibility.Visible;
                RefusedTitleText.Text = Strings.Get("DlgUninstallNotValidTitle");
                RefusedDetailText.Text = Strings.Format(
                    "DlgUninstallNotValidDetail", _plan.InstallPath);
                DisableActions();
                break;

            case UninstallMode.NothingToDo:
                RefusedPanel.Visibility = Visibility.Visible;
                RefusedTitleText.Text = Strings.Get("DlgUninstallNothingTitle");
                RefusedDetailText.Text = Strings.Get("DlgUninstallNothingDetail");
                DisableActions();
                break;
        }
    }

    private void DisableActions()
    {
        OkButton.IsEnabled = false;
        OptDeleteShortcuts.IsEnabled = false;
        OptRemoveRegistry.IsEnabled = false;
        OptResetConfig.IsEnabled = false;
        AoE3SafeNoteText.Visibility = Visibility.Collapsed;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Options = new UninstallOptions
        {
            DeleteModFiles = true,
            DeleteShortcuts = OptDeleteShortcuts.IsChecked ?? false,
            RemoveRegistry = OptRemoveRegistry.IsChecked ?? false,
            ResetConfig = OptResetConfig.IsChecked ?? false,
        };
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
