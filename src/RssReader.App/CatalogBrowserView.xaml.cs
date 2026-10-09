using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class CatalogBrowserView : UserControl
{
    public CatalogBrowserView()
    {
        InitializeComponent();
    }

    private async void AddPersonalFeed_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var dialog = new ProfileFeedEntryWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await viewModel.AddPersonalFeedAsync(dialog.FeedName, dialog.FeedUrl);
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                Window.GetWindow(this),
                $"The personal feed could not be added.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ImportPersonalOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var dialog = new OpenFileDialog
        {
            Title = "Import feeds to your profile",
            Filter = "OPML files (*.opml;*.xml)|*.opml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            await using var stream = dialog.OpenFile();
            var result = await viewModel.ImportPersonalFeedsAsync(stream);
            MessageDialogWindow.Show(
                owner,
                $"Added {result.AddedCount} feed(s). Skipped {result.DuplicateCount} duplicate(s) and {result.SkippedCount} invalid or unsupported outline(s).",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                owner,
                $"The OPML file could not be imported.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportPersonalOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || !viewModel.IsPersonalFeedManagementVisible)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        var dialog = new SaveFileDialog
        {
            Title = "Export your feeds",
            Filter = "OPML files (*.opml)|*.opml",
            DefaultExt = ".opml",
            AddExtension = true,
            FileName = "rss-reader-feeds.opml"
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            var opml = await viewModel.ExportPersonalFeedsAsync();
            await File.WriteAllTextAsync(dialog.FileName, opml, new UTF8Encoding(false));
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                owner,
                $"Your feeds could not be exported.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PreviewCatalogFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CatalogFeedListItem feed } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new CatalogFeedPreviewWindow(
            feed,
            token => viewModel.LoadCatalogFeedPreviewAsync(feed, token),
            loadRawFeedXml: token => viewModel.LoadCatalogFeedRawXmlAsync(feed, token))
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}
