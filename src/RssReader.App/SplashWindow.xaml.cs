using System.Windows;

namespace RssReader.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status) => StartupStatus.Text = status;
}