using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirmation dialog for uninstalling Wars of Liberty. Shows the planned
/// strategy (manifest / subfolder / refused), the install path, and lets
/// the user choose optional cleanup steps.
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
        ProtectionNoteText.Text = Strings.Get("DlgUninstallProtectionNote");
        OkButton.Content = Strings.Get("BtnUninstall");
        CancelButton.Content = Strings.Get("BtnCancel");
    }

    private void ApplyPlan()
    {
        InstallPathText.Text = _plan.InstallPath;

        switch (_plan.Mode)
        {
            case UninstallMode.Manifest:
                ManifestPanel.Visibility = Visibility.Visible;
                ManifestTitleText.Text = Strings.Get("DlgUninstallManifestTitle");
                ManifestDetailText.Text = Strings.Format("DlgUninstallManifestDetail",
                    _plan.FileCount, _plan.DirectoryCount);
                OkButton.IsEnabled = true;
                break;

            case UninstallMode.SubfolderFallback:
                SubfolderPanel.Visibility = Visibility.Visible;
                SubfolderTitleText.Text = Strings.Get("DlgUninstallSubfolderTitle");
                SubfolderDetailText.Text = Strings.Format("DlgUninstallSubfolderDetail",
                    _plan.FileCount, _plan.DirectoryCount);
                OkButton.IsEnabled = true;
                break;

            case UninstallMode.RefusedMergedWithAoe3:
                RefusedPanel.Visibility = Visibility.Visible;
                RefusedTitleText.Text = Strings.Get("DlgUninstallRefusedTitle");
                RefusedDetailText.Text = Strings.Get("DlgUninstallRefusedDetail");
                // Disable everything but Cancel
                OkButton.IsEnabled = false;
                OptDeleteShortcuts.IsEnabled = false;
                OptRemoveRegistry.IsEnabled = false;
                OptResetConfig.IsEnabled = false;
                break;

            case UninstallMode.NothingToDo:
                RefusedPanel.Visibility = Visibility.Visible;
                RefusedTitleText.Text = Strings.Get("DlgUninstallNothingTitle");
                RefusedDetailText.Text = Strings.Get("DlgUninstallNothingDetail");
                OkButton.IsEnabled = false;
                OptDeleteShortcuts.IsEnabled = false;
                OptRemoveRegistry.IsEnabled = false;
                OptResetConfig.IsEnabled = false;
                break;
        }
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
