using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;

namespace RssReader.App;

public sealed record CommandPaletteItem(string Label, string Description, Action Execute);

public partial class CommandPaletteWindow : Window
{
    private readonly ICollectionView _filteredItems;

    public CommandPaletteWindow(IEnumerable<CommandPaletteItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        InitializeComponent();
        var paletteItems = new ObservableCollection<CommandPaletteItem>(items);
        _filteredItems = CollectionViewSource.GetDefaultView(paletteItems);
        _filteredItems.Filter = MatchesSearch;
        CommandList.ItemsSource = _filteredItems;
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            SelectFirstResult();
        };
    }

    private bool MatchesSearch(object item)
    {
        if (item is not CommandPaletteItem paletteItem)
        {
            return false;
        }

        var query = SearchBox?.Text.Trim();
        return string.IsNullOrEmpty(query) ||
            paletteItem.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            paletteItem.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filteredItems.Refresh();
        SelectFirstResult();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && CommandList.Items.Count > 0)
        {
            CommandList.Focus();
            CommandList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteSelectedItem();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && CommandList.IsKeyboardFocusWithin)
        {
            ExecuteSelectedItem();
            e.Handled = true;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        ExecuteSelectedItem();

    private void SelectFirstResult()
    {
        CommandList.SelectedIndex = CommandList.Items.Count == 0 ? -1 : 0;
    }

    private void ExecuteSelectedItem()
    {
        if (CommandList.SelectedItem is not CommandPaletteItem item)
        {
            return;
        }

        item.Execute();
        DialogResult = true;
    }
}
