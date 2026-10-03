using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RssReader.App;

public partial class CreateFolderWindow : Window
{
    private readonly IReadOnlyList<string> _existingFolders;

    public CreateFolderWindow(IReadOnlyList<string> existingFolders, IReadOnlyList<string> suggestions)
    {
        InitializeComponent();
        _existingFolders = existingFolders;
        Suggestions = new ObservableCollection<string>(suggestions);
        DataContext = this;
    }

    public ObservableCollection<string> Suggestions { get; }
    public bool HasSuggestions => Suggestions.Count > 0;
    public string FolderName => FolderNameBox.Text.Trim();
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FolderNameBox.Focus();
        Keyboard.Focus(FolderNameBox);
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
            Create_Click(sender, e);
            e.Handled = true;
        }
    }

    private void FolderNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ErrorMessageText.Text = string.Empty;
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string suggestion })
        {
            FolderNameBox.Text = suggestion;
            FolderNameBox.CaretIndex = FolderNameBox.Text.Length;
            FolderNameBox.Focus();
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var folderName = FolderName;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            SetError("Enter a folder name.");
            return;
        }

        if (string.Equals(folderName, "All", StringComparison.OrdinalIgnoreCase))
        {
            SetError("All is reserved for the aggregate feed view.");
            return;
        }

        if (_existingFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            SetError("A folder with that name already exists.");
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SetError(string message)
    {
        ErrorMessageText.Text = message;
    }
}