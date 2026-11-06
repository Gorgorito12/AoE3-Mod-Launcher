using System;
using System.Linq;
using System.Windows;
using WarsOfLibertyLauncher.Localization;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>Settings › Interface (design 69): the Library's look, with a preview on top.</summary>
public partial class LauncherSettingsDialog
{
    /// <summary>Set by MainWindow: repaints the Library once a change is in the config.</summary>
    public Action? AppearanceChanged { get; set; }

    private bool _loadingAppearance;

    private void ApplyAppearanceStrings()
    {
        AppearanceGroupLabel.Text = Strings.Get("DlgSetAppearanceGroup");
        TextAndTabsLabel.Text = Strings.Get("DlgSetTextTabsGroup");
        GlassTitle.Text = Strings.Get("DlgSetGlassTitle");
        GlassHint.Text = Strings.Get("DlgSetGlassHint");
        GlassSolidOption.Content = Strings.Get("DlgSetGlassSolid");
        GlassMediumOption.Content = Strings.Get("DlgSetGlassMedium");
        GlassMaximumOption.Content = Strings.Get("DlgSetGlassMaximum");
        ModImageTitle.Text = Strings.Get("DlgSetModImageTitle");
        ModImageHint.Text = Strings.Get("DlgSetModImageHint");
        ModColorTitle.Text = Strings.Get("DlgSetModColorTitle");
        ModColorHint.Text = Strings.Get("DlgSetModColorHint");
        EffectsTitle.Text = Strings.Get("DlgSetEffectsTitle");
        EffectsHint.Text = Strings.Get("DlgSetEffectsHint");
        EffectsAutoOption.Content = Strings.Get("DlgSetEffectsAuto");
        EffectsFullOption.Content = Strings.Get("DlgSetEffectsFull");
        EffectsLowOption.Content = Strings.Get("DlgSetEffectsLow");
        ModListTitle.Text = Strings.Get("DlgSetModListTitle");
        ModListHint.Text = Strings.Get("DlgSetModListHint");
        ModListAutoOption.Content = Strings.Get("DlgSetModListAuto");
        ModListOpenOption.Content = Strings.Get("DlgSetModListOpen");
        ModListClosedOption.Content = Strings.Get("DlgSetModListClosed");
        StartTabTitle.Text = Strings.Get("DlgSetStartTabTitle");
        StartTabHint.Text = Strings.Get("DlgSetStartTabHint");
        StartMultiplayerOption.Content = Strings.Get("DlgSetStartMultiplayer");
        StartLibraryOption.Content = Strings.Get("DlgSetStartLibrary");
        ResetAppearanceButton.Content = Strings.Get("DlgSetResetAppearance");
    }

    private void LoadAppearance()
    {
        _loadingAppearance = true;
        var glass = GlassTheme.Normalize(_config.GlassLevel);
        GlassSolidOption.IsChecked = glass == GlassTheme.Solid;
        GlassMediumOption.IsChecked = glass == GlassTheme.Medium;
        GlassMaximumOption.IsChecked = glass == GlassTheme.Maximum;
        ShowModImageToggle.IsChecked = _config.ShowModImage;
        UseModColorToggle.IsChecked = _config.UseModColor;
        EffectsAutoOption.IsChecked = _config.EffectsMode is not ("full" or "low");
        EffectsFullOption.IsChecked = _config.EffectsMode == "full";
        EffectsLowOption.IsChecked = _config.EffectsMode == "low";
        ModListAutoOption.IsChecked = _config.ModListMode is not ("open" or "closed");
        ModListOpenOption.IsChecked = _config.ModListMode == "open";
        ModListClosedOption.IsChecked = _config.ModListMode == "closed";
        var multiplayerFirst = _tabOrder.FirstOrDefault() == "multiplayer";
        StartMultiplayerOption.IsChecked = multiplayerFirst;
        StartLibraryOption.IsChecked = !multiplayerFirst;
        _loadingAppearance = false;
        RefreshAppearancePreview();
    }

    private string PickedGlass => GlassSolidOption.IsChecked == true ? GlassTheme.Solid
        : GlassMaximumOption.IsChecked == true ? GlassTheme.Maximum
        : GlassTheme.Medium;

    private string PickedEffects => EffectsFullOption.IsChecked == true ? "full"
        : EffectsLowOption.IsChecked == true ? "low"
        : "auto";

    private string PickedModList => ModListOpenOption.IsChecked == true ? "open"
        : ModListClosedOption.IsChecked == true ? "closed"
        : "auto";

    /// <summary>What the instant-save fingerprint compares for this section.</summary>
    private string AppearanceKey()
        => $"{PickedGlass}|{ShowModImageToggle.IsChecked}|{UseModColorToggle.IsChecked}|{PickedEffects}|{PickedModList}";

    private void WriteAppearance()
    {
        var changed = _config.GlassLevel != PickedGlass
            || _config.ShowModImage != (ShowModImageToggle.IsChecked == true)
            || _config.UseModColor != (UseModColorToggle.IsChecked == true)
            || _config.EffectsMode != PickedEffects
            || _config.ModListMode != PickedModList;
        _config.GlassLevel = PickedGlass;
        _config.ShowModImage = ShowModImageToggle.IsChecked == true;
        _config.UseModColor = UseModColorToggle.IsChecked == true;
        _config.EffectsMode = PickedEffects;
        _config.ModListMode = PickedModList;
        if (changed) AppearanceChanged?.Invoke();
    }

    /// <summary>The segmented control is what shows; the collapsed combo keeps feeding the
    /// fingerprint and the save, which both read it.</summary>
    private void LanguageOption_Checked(object sender, RoutedEventArgs e)
    {
        var tag = ReferenceEquals(sender, LanguageEsOption) ? "es" : "en";
        foreach (System.Windows.Controls.ComboBoxItem item in LanguageCombo.Items)
            if (item.Tag as string == tag)
                LanguageCombo.SelectedItem = item;
    }

    private void AppearanceOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingAppearance) return;
        var start = StartMultiplayerOption.IsChecked == true ? "multiplayer" : "library";
        if (_tabOrder.FirstOrDefault() != start)
        {
            _tabOrder.Remove(start);
            _tabOrder.Insert(0, start);
        }
        RefreshAppearancePreview();
    }

    private void ResetAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        GlassMediumOption.IsChecked = true;
        ShowModImageToggle.IsChecked = true;
        UseModColorToggle.IsChecked = true;
        EffectsAutoOption.IsChecked = true;
        ModListAutoOption.IsChecked = true;
        StartLibraryOption.IsChecked = true;
    }

    private void RefreshAppearancePreview()
    {
        var active = ModRegistry.Find(_config.ActiveModId) ?? ModRegistry.Default;
        var rows = new[] { active }.Concat(ModRegistry.All.Where(m => m.Id != active.Id)).ToList();
        var reduced = PickedEffects == "low" || (PickedEffects == "auto" && !SystemParameters.ClientAreaAnimation);
        AppearancePreviewPanel.Show(active, rows, PickedGlass, ShowModImageToggle.IsChecked == true,
            UseModColorToggle.IsChecked == true, reduced);
    }
}
