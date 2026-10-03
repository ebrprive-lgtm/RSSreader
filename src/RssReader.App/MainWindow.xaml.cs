using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class MainWindow : Window
{
    public MainWindow() : this(new MainWindowViewModel(Profile.CreateRegular("Reader")))
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.FolderSelectionRequested = ShowFolderSelectionAsync;
    }

    public event Action? LogoutRequested;

    public event Action? PreferencesRequested;

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) => PreferencesRequested?.Invoke();

    private void LogoutMenuItem_Click(object sender, RoutedEventArgs e) => LogoutRequested?.Invoke();

    private Task<string?> ShowFolderSelectionAsync(
        IReadOnlyList<string> folders,
        IReadOnlyList<string> suggestions,
        Func<string, Task> createFolderAsync)
    {
        var dialog = new FolderSelectionWindow(folders, suggestions, createFolderAsync)
        {
            Owner = this
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.SelectedFolder : null);
    }

    private async void ImportOpml_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import feed list",
            Filter = "OPML files (*.opml;*.xml)|*.opml;*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using var stream = dialog.OpenFile();
        await catalogManagement.ImportOpmlAsync(stream);
    }

    private void AddFeed_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Feed);

    private void AddCategory_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Category);

    private void AddCollection_Click(object sender, RoutedEventArgs e) => ShowCatalogEntry(CatalogEntryKind.Collection);

    private void ShowCatalogEntry(CatalogEntryKind entryKind)
    {
        if (DataContext is not MainWindowViewModel { CatalogManagement: { } catalogManagement })
        {
            return;
        }

        var dialog = new CatalogEntryWindow(catalogManagement, entryKind)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}