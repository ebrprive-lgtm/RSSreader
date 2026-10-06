using System.Windows;
using System.Windows.Input;

namespace RssReader.App;

public partial class ProfileFeedEntryWindow : Window
{
    public ProfileFeedEntryWindow()
    {
        InitializeComponent();
    }

    public string FeedName => FeedNameBox.Text.Trim();
    public string FeedUrl => FeedUrlBox.Text.Trim();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FeedNameBox.Focus();
        Keyboard.Focus(FeedNameBox);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Add_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FeedName))
        {
            FeedNameBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(FeedUrl))
        {
            FeedUrlBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
