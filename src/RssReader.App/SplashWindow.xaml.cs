using System.Windows;

namespace RssReader.App;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status) => StartupStatus.Text = status;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();
}