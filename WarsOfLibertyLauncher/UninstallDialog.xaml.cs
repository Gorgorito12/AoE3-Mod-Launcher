using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirmation dialog for uninstalling a mod. Shows the install path being
/// targeted, a count of files / dirs that will be removed, and lets the
/// user toggle optional cleanup steps (shortcuts, registry, config). If the
/// path doesn't have the profile's probe file we surface a red error panel
/// and refuse to proceed instead of touching files.
/// </summary>
public partial class UninstallDialog : Window
{
    public UninstallOptions Options { get; private set; } = new();

    private readonly UninstallPlan _plan;
    private readonly string _modDisplayName;
    private readonly string _probeFile;

    /// <summary>
    /// Back-compat overload. Defaults to WoL-labelled copy. New callers
    /// should use the four-argument form so the dialog templates the active
    /// mod's name into every visible string.
    /// </summary>
    public UninstallDialog(UninstallPlan plan)
        : this(plan, "Wars of Liberty", @"art\zulushield\") { }

    /// <param name="modDisplayName">
    /// Display name of the mod being uninstalled (e.g. "Wars of Liberty",
    /// "Improvement Mod"). Templated into the title, description and error
    /// messages so every mod sees its own name.
    /// </param>
    /// <param name="probeFile">
    /// File the launcher checks to confirm the target folder really is an
    /// install of this mod (e.g. <c>"age3m.exe"</c> for Improvement Mod,
    /// <c>"data\\stringtabley.xml"</c> for Wars of Liberty). Shown in the
    /// "not a valid install" error message so the user can understand
    /// what's missing.
    /// </param>
    public UninstallDialog(UninstallPlan plan, string modDisplayName, string probeFile)
    {
        InitializeComponent();
        _plan = plan;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;
        _probeFile = string.IsNullOrEmpty(probeFile) ? "(unknown)" : probeFile;

        ApplyLanguage();
        ApplyPlan();
    }

    private void ApplyLanguage()
    {
        Title = Strings.Format("DlgUninstallTitle", _modDisplayName);
        HeaderText.Text = Strings.Format("DlgUninstallHeader", _modDisplayName);
        DescriptionText.Text = Strings.Format("DlgUninstallDescription", _modDisplayName);
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
                RefusedTitleText.Text = Strings.Format(
                    "DlgUninstallNotValidTitle", _modDisplayName);
                RefusedDetailText.Text = Strings.Format(
                    "DlgUninstallNotValidDetail", _plan.InstallPath, _probeFile, _modDisplayName);
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
