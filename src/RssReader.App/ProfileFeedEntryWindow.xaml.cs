using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.ObjectModel;

namespace RssReader.App;

public partial class ProfileFeedEntryWindow : Window
{
    public ProfileFeedEntryWindow(IEnumerable<string> categories)
    {
        InitializeComponent();
        Categories = new ObservableCollection<string>(
            categories
                .Append("Unfiled")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase));
        CategoryBox.ItemsSource = Categories;
    }

    public ObservableCollection<string> Categories { get; }
    public string FeedName => FeedNameBox.Text.Trim();
    public string FeedUrl => FeedUrlBox.Text.Trim();
    public string CategoryName => CategoryBox.Text.Trim();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CategoryBox.ApplyTemplate();
        if (CategoryBox.Template.FindName("PART_EditableTextBox", CategoryBox) is TextBox categoryTextBox)
        {
            categoryTextBox.TextChanged += EntryField_TextChanged;
        }

        UpdateAddButtonState();
        FeedNameBox.Focus();
        Keyboard.Focus(FeedNameBox);
    }

    private void EntryField_TextChanged(object sender, TextChangedEventArgs e) => UpdateAddButtonState();

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAddButtonState();

    private void UpdateAddButtonState() =>
        AddFeedButton.IsEnabled =
            !string.IsNullOrWhiteSpace(FeedName) &&
            !string.IsNullOrWhiteSpace(FeedUrl) &&
            !string.IsNullOrWhiteSpace(CategoryName);

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

        if (string.IsNullOrWhiteSpace(CategoryName))
        {
            CategoryBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
