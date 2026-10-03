using Microsoft.Win32;
using System.Windows;
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
        viewModel.ProfileSwitchRequested += () => ProfileSwitchRequested?.Invoke();
        viewModel.FolderSelectionRequested = ShowFolderSelectionAsync;
    }

    public event Action? ProfileSwitchRequested;

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
}