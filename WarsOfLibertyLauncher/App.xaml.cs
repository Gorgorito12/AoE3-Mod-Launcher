using System.Windows;
using WarsOfLibertyLauncher.Models;
using WarsOfLibertyLauncher.Services;

namespace WarsOfLibertyLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Apply the saved theme before MainWindow's XAML materializes so the
        // first frame already paints in the right palette. DynamicResource
        // consumers in the shared styles see the swapped Colors dictionary
        // when their template setters evaluate.
        try
        {
            var config = LauncherConfig.Load();
            ThemeService.Apply(config.Theme);
        }
        catch
        {
            // Config read failed — leave the default dark theme in place.
        }

        base.OnStartup(e);
    }
}
