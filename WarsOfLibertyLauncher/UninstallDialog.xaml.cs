using System.Globalization;
using System.IO;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>
/// Confirmation dialog for uninstalling one copy of a mod — the active one or any other.
/// Shows the folder that goes, how much is in it, and what else can be cleaned up. When
/// the plan refuses (the folder is not this mod, or holds the base game) it explains what
/// was checked and offers only Close: the refusal lives in the plan, not in this window.
/// </summary>
public partial class UninstallDialog : Window
{
    public UninstallOptions Options { get; private set; } = new();

    private readonly UninstallPlan _plan;
    private readonly string _modDisplayName;
    private readonly string _probeFile;
    private readonly string _userDataFolderName;
    private readonly string _copyLabel;
    private readonly int _otherCopies;
    private readonly bool _isCopy;

    /// <summary>
    /// Back-compat overload. Defaults to WoL-labelled copy.
    /// </summary>
    public UninstallDialog(UninstallPlan plan)
        : this(plan, "Wars of Liberty", @"art\zulushield\") { }

    public UninstallDialog(
        UninstallPlan plan, string modDisplayName, string probeFile, string userDataFolderName = "")
        : this(plan, modDisplayName, probeFile, userDataFolderName,
               copyLabel: "", otherCopies: 0, isCopy: false) { }

    /// <param name="copyLabel">The copy's folder name, shown in the question. Empty uses
    /// the folder name of the plan's path.</param>
    /// <param name="otherCopies">How many OTHER registered copies of this mod exist. Only
    /// when this is above zero does the window promise they are not touched — it must not
    /// say so without knowing.</param>
    /// <param name="isCopy">True when this is not the active copy. Hides the saved-games
    /// option: every copy of a mod shares one My Games folder, so it is not this copy's to
    /// clean up.</param>
    public UninstallDialog(
        UninstallPlan plan, string modDisplayName, string probeFile, string userDataFolderName,
        string copyLabel, int otherCopies, bool isCopy)
    {
        InitializeComponent();
        _plan = plan;
        _modDisplayName = string.IsNullOrEmpty(modDisplayName) ? "the mod" : modDisplayName;
        _probeFile = string.IsNullOrEmpty(probeFile) ? "(unknown)" : probeFile;
        _userDataFolderName = string.IsNullOrWhiteSpace(userDataFolderName)
            ? _modDisplayName : userDataFolderName;
        // An in-place overlay lives in the GAME's folder (…\bin), whose name says nothing
        // about the mod; the question names the mod there instead.
        _copyLabel = plan.OverlayOnly
            ? _modDisplayName
            : !string.IsNullOrWhiteSpace(copyLabel)
                ? copyLabel
                : LeafOf(plan.InstallPath, _modDisplayName);
        _otherCopies = otherCopies;
        _isCopy = isCopy;

        ApplyLanguage();
        ApplyPlan();
    }

    private static string LeafOf(string path, string fallback)
    {
        try
        {
            var leaf = Path.GetFileName((path ?? "").TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(leaf) ? fallback : leaf;
        }
        catch { return fallback; }
    }

    /// <summary>The culture the thousands separator follows: the launcher's language, not
    /// Windows' — "65.057" in a Spanish window, "65,057" in an English one.</summary>
    internal static CultureInfo CountCulture()
        => Strings.Language == "es" ? CultureInfo.GetCultureInfo("es-ES") : CultureInfo.GetCultureInfo("en-US");

    private void ApplyLanguage()
    {
        Title = Strings.Get("DlgUninstallTitle");
        TitleBarControl.Title = Strings.Get("DlgUninstallTitle");
        HeaderText.Text = Strings.Format("DlgUninstallQuestion", _copyLabel);
        OptionsTitleText.Text = Strings.Get("DlgUninstallAlsoRemove");
        OptShortcutsTitle.Text = Strings.Get("DlgUninstallOptShortcuts");
        OptShortcutsDesc.Text = Strings.Get("DlgUninstallOptShortcutsDesc");
        OptRegistryTitle.Text = Strings.Get("DlgUninstallOptRegistry");
        OptRegistryDesc.Text = Strings.Get("DlgUninstallOptRegistryDesc");
        OptUserDataTitle.Text = Strings.Get("DlgUninstallOptUserData");
        OptUserDataDesc.Text = Strings.Format("DlgUninstallOptUserDataDesc", _userDataFolderName);
        // Said once, and never more than is known: "your other copies" only when there are
        // some, and for an in-place overlay only what is actually true of the game folder.
        SafeNoteText.Text = Strings.Get(
            _plan.OverlayOnly ? "DlgUninstallSafeOverlay"
            : _otherCopies > 0 ? "DlgUninstallSafeWithCopies"
            : "DlgUninstallSafe");
        OkButton.Content = Strings.Get("BtnUninstall");
        CancelButton.Content = Strings.Get("BtnCancel");
        CloseButton.Content = Strings.Get("BtnClose");

        bool showUserData = !_isCopy && _plan.UserDataFileCount > 0;
        RowUserData.Visibility = showUserData ? Visibility.Visible : Visibility.Collapsed;
        // The last visible row drops its seam.
        RowRegistry.BorderThickness = showUserData ? new Thickness(0, 0, 0, 1) : new Thickness(0);
    }

    private void ApplyPlan()
    {
        var culture = CountCulture();
        InstallPathText.Text = PathDisplay.BreakAtSeparators(_plan.InstallPath);
        RefusedPathText.Text = PathDisplay.BreakAtSeparators(_plan.InstallPath);

        switch (_plan.Mode)
        {
            case UninstallMode.Valid:
                if (_plan.OverlayOnly)
                {
                    // An in-place overlay: the folder STAYS, only the mod's own files go.
                    // Saying "this folder is deleted" there would be false.
                    DeletedLabelText.Text = Strings.Get("DlgUninstallOverlayRemoved");
                    CountsText.Text = Strings.Format("DlgUninstallOverlayCount",
                        _plan.FileCount.ToString("N0", culture));
                }
                else
                {
                    DeletedLabelText.Text = Strings.Get("DlgUninstallFolderDeleted");
                    CountsText.Text = Strings.Format("DlgUninstallCounts",
                        _plan.FileCount.ToString("N0", culture),
                        _plan.DirectoryCount.ToString("N0", culture));
                }
                break;

            case UninstallMode.NotAValidInstall:
                ShowRefused(
                    _plan.ContainsBaseGame
                        ? Strings.Get("DlgUninstallBaseGameHeadline")
                        : Strings.Format("DlgUninstallNotValidHeadline", _modDisplayName),
                    _plan.ContainsBaseGame
                        ? Strings.Get("DlgUninstallBaseGameBody")
                        : Strings.Get("DlgUninstallNotValidBody"),
                    Strings.Get("DlgUninstallNotValidHint"));
                break;

            case UninstallMode.NothingToDo:
                ShowRefused(
                    Strings.Get("DlgUninstallNothingHeadline"),
                    Strings.Get("DlgUninstallNothingBody"),
                    Strings.Get("DlgUninstallNotValidHint"));
                break;
        }
    }

    /// <summary>49d: hide everything that could act, and say what was checked.</summary>
    private void ShowRefused(string headline, string body, string hint)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        RefusedPanel.Visibility = Visibility.Visible;
        OkButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsDefault = true;
        RefusedTitleText.Text = headline;
        // The probe file goes in as a monospace value, "Nothing was deleted." in bold.
        Controls.MarkedText.Set(RefusedDetailText, body, _probeFile);
        Controls.MarkedText.Set(RefusedHintText, hint);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plan.Mode != UninstallMode.Valid) { DialogResult = false; return; }
        Options = BuildOptions();
        DialogResult = true;
    }

    /// <summary>What the ticked boxes ask for. A hidden option is never on, whatever its box
    /// last said, and a copy never falls back on the profile (see
    /// <see cref="UninstallOptions.AllowProfileFallbacks"/>).</summary>
    internal UninstallOptions BuildOptions() => new()
    {
        DeleteModFiles = true,
        DeleteShortcuts = OptDeleteShortcuts.IsChecked ?? false,
        RemoveRegistry = OptRemoveRegistry.IsChecked ?? false,
        DeleteUserDataFiles = RowUserData.Visibility == Visibility.Visible
                              && (OptDeleteUserData.IsChecked ?? false),
        AllowProfileFallbacks = !_isCopy,
    };

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
