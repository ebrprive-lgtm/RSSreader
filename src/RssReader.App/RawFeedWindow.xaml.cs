using System.Windows;

namespace RssReader.App;

public partial class RawFeedWindow : Window
{
    public RawFeedWindow(string feedSource, string rawContent)
    {
        InitializeComponent();
        FeedSourceText.Text = feedSource;
        RawContentTextBox.Text = rawContent;
    }
}