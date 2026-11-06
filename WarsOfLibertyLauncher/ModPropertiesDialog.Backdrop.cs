using System.Linq;
using System.Windows;
using System.Windows.Media;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

/// <summary>The mod's image as a strip behind the title bar (BASE.md) and its tint on the bar.</summary>
public partial class ModPropertiesDialog
{
    private void PaintModStrip()
    {
        var accent = ModTheme.AccentOf(_profile.AccentColor, _config.UseModColor);
        Resources["UiToggleOn"] = new SolidColorBrush(accent);
        var image = _config.ShowModImage
            ? MainWindow.TryLoadTileImage(_profile.ResolveHeroSources().FirstOrDefault())
            : null;
        // The tile cache is shared, so a brush still downloading may belong to another thread.
        if (image != null && image.CheckAccess())
        {
            var strip = image.Clone();
            strip.AlignmentY = AlignmentY.Top;
            ModStrip.Background = strip;
        }
        else
        {
            ModStrip.Background = ModTheme.QuietBackground(accent);
        }
        TitleBarControl.Background = new SolidColorBrush(GlassTheme.ModBar(accent, _config.GlassLevel));
    }

    private void ModFooterClose_Click(object sender, RoutedEventArgs e) => Close();
}
