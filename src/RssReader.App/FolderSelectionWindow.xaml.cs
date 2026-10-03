using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class FolderSelectionWindow : Window
{
    private readonly List<string> _allFolders;
    private readonly IReadOnlyList<string> _suggestions;
    private readonly Func<string, Task> _createFolderAsync;

    public FolderSelectionWindow(
        IReadOnlyList<string> folders,
        IReadOnlyList<string> suggestions,
        Func<string, Task> createFolderAsync)
    {
        _allFolders = folders.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        _suggestions = suggestions;
        _createFolderAsync = createFolderAsync;
        InitializeComponent();
        DataContext = this;
        UpdateVisibleFolders();
    }

    public ObservableCollection<string> VisibleFolders { get; } = [];
    public string? SelectedFolder { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FolderSearchBox.Focus();
        Keyboard.Focus(FolderSearchBox);
        SelectFirstVisibleFolder();
        UpdateEmptyMessage();
    }

    private void FolderSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateVisibleFolders();
        SelectFirstVisibleFolder();
        UpdateEmptyMessage();
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
            AcceptSelectedFolder();
            e.Handled = true;
        }
    }

    private void FoldersListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: string })
        {
            AcceptSelectedFolder();
        }
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateFolderWindow(_allFolders, _suggestions)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _createFolderAsync(dialog.FolderName);
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                "The folder could not be created.",
                "RSS Reader",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _allFolders.Add(dialog.FolderName);
        FolderSearchBox.Text = dialog.FolderName;
        FoldersListBox.SelectedItem = dialog.FolderName;
        AcceptSelectedFolder();
    }

    private void UpdateVisibleFolders()
    {
        var query = FolderSearchBox?.Text.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allFolders
            : _allFolders.Where(folder => folder.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        VisibleFolders.Clear();
        foreach (var folder in matches)
        {
            VisibleFolders.Add(folder);
        }
    }

    private void SelectFirstVisibleFolder()
    {
        FoldersListBox.SelectedIndex = VisibleFolders.Count == 0 ? -1 : 0;
    }

    private void UpdateEmptyMessage() =>
        EmptyFoldersMessage.Visibility = VisibleFolders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AcceptSelectedFolder()
    {
        if (FoldersListBox.SelectedItem is not string folder)
        {
            return;
        }

        SelectedFolder = folder;
        DialogResult = true;
    }
}