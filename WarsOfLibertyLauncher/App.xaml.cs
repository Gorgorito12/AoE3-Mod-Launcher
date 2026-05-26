namespace WarsOfLibertyLauncher;

public partial class App : System.Windows.Application
{
    // App-level startup was previously used to apply the saved theme
    // (dark/light/system) before MainWindow's XAML materialised. The
    // theme picker is gone (see LauncherSettingsDialog.xaml) — the
    // launcher is dorado-imperial dark-only — so there's nothing for
    // OnStartup to do that base.OnStartup doesn't already handle.
    // App.xaml's own ResourceDictionary (Styles/Colors.xaml) is the
    // single source of truth for the palette.
}
