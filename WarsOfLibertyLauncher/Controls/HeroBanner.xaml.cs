using System.Windows.Controls;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Controls;

public partial class HeroBanner : UserControl
{
    public HeroBanner()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string Subtitle
    {
        get => SubtitleText.Text;
        set => SubtitleText.Text = value;
    }

    public Brush HostBackground
    {
        get => ModBannerHost.Background;
        set => ModBannerHost.Background = value;
    }
}
