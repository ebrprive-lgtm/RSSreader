using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using RssReader.App.ViewModels;

namespace RssReader.App;

public partial class FeedTagEditorWindow : Window
{
    private readonly ObservableCollection<FeedTagChoice> _tagChoices;

    public FeedTagEditorWindow(
        string feedName,
        IEnumerable<string> availableTags,
        IEnumerable<string> assignedTags)
    {
        InitializeComponent();
        Title = string.Empty;
        FeedNameText.Text = feedName;
        var assignedNames = assignedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _tagChoices = new ObservableCollection<FeedTagChoice>(availableTags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new FeedTagChoice(name, assignedNames.Contains(name))));
        TagChoicesList.ItemsSource = _tagChoices;
        UpdateEmptyState();
        Loaded += (_, _) => NewTagTextBox.Focus();
    }

    public IReadOnlyList<string> SelectedTagNames => _tagChoices
        .Where(choice => choice.IsAssigned)
        .Select(choice => choice.Name)
        .ToArray();

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var tagName = NewTagTextBox.Text.Trim();
        if (tagName.Length == 0)
        {
            ErrorMessageText.Text = "Enter a tag name.";
            NewTagTextBox.Focus();
            return;
        }

        var existingChoice = _tagChoices.FirstOrDefault(choice =>
            string.Equals(choice.Name, tagName, StringComparison.OrdinalIgnoreCase));
        if (existingChoice is not null)
        {
            existingChoice.IsAssigned = true;
        }
        else
        {
            _tagChoices.Add(new FeedTagChoice(tagName, true));
            UpdateEmptyState();
        }

        ErrorMessageText.Text = string.Empty;
        NewTagTextBox.Clear();
        NewTagTextBox.Focus();
    }

    private void NewTagTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTag_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void UpdateEmptyState() =>
        EmptyTagsMessage.Visibility = _tagChoices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class FeedTagChoice(string name, bool isAssigned) : ObservableObject
{
    private bool _isAssigned = isAssigned;

    public string Name { get; } = name;
    public bool IsAssigned
    {
        get => _isAssigned;
        set => SetProperty(ref _isAssigned, value);
    }
}