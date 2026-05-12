using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WarsOfLibertyLauncher.Controls;

public partial class StatusCard : UserControl
{
    public event RoutedEventHandler? BrowseAoE3Click;

    public StatusCard()
    {
        InitializeComponent();
    }

    public string InstalledLabel
    {
        get => StatusInstalledLabel.Text;
        set => StatusInstalledLabel.Text = value;
    }

    public string LatestLabel
    {
        get => StatusLatestLabel.Text;
        set => StatusLatestLabel.Text = value;
    }

    public string StateText
    {
        get => StatusValueText.Text;
        set => StatusValueText.Text = value;
    }

    // Setter applies the same brush to both the value text and the row icon
    // so they stay in sync visually (green = ready, red = problem, …).
    public Brush StateForeground
    {
        get => StatusValueText.Foreground;
        set
        {
            StatusValueText.Foreground = value;
            StatusRowIcon.Fill = value;
        }
    }

    public string CurrentVersion
    {
        get => CurrentVersionText.Text;
        set => CurrentVersionText.Text = value;
    }

    public string LatestVersion
    {
        get => LatestVersionText.Text;
        set => LatestVersionText.Text = value;
    }

    public bool AoE3MissingVisible
    {
        get => AoE3MissingRow.Visibility == Visibility.Visible;
        set => AoE3MissingRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public string AoE3MissingMessage
    {
        get => AoE3MissingLabel.Text;
        set => AoE3MissingLabel.Text = value;
    }

    public object BrowseAoE3ButtonContent
    {
        get => BrowseAoE3Button.Content;
        set => BrowseAoE3Button.Content = value;
    }

    private void BrowseAoE3Button_Click(object sender, RoutedEventArgs e)
    {
        BrowseAoE3Click?.Invoke(this, e);
    }
}
