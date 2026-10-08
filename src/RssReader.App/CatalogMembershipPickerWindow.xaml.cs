using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RssReader.App.ViewModels;

namespace RssReader.App;

public sealed class CatalogMembershipPickerItem : ObservableObject
{
    private bool _isSelected;

    public CatalogMembershipPickerItem(string id, string name, string details, bool isSelected)
    {
        Id = id;
        Name = name;
        Details = details;
        _isSelected = isSelected;
        SearchText = $"{name} {details}";
    }

    public string Id { get; }
    public string Name { get; }
    public string Details { get; }
    public string SearchText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public partial class CatalogMembershipPickerWindow : Window
{
    private readonly ICollectionView _filteredItems;
    private readonly Func<Window, Task<CatalogMembershipPickerItem?>>? _createNewCollectionAsync;

    public CatalogMembershipPickerWindow(
        string title,
        string description,
        IEnumerable<CatalogMembershipPickerItem> items,
        Func<Window, Task<CatalogMembershipPickerItem?>>? createNewCollectionAsync = null)
    {
        InitializeComponent();
        DialogHeading.Text = title;
        DialogDescription.Text = description;
        _createNewCollectionAsync = createNewCollectionAsync;
        CreateCollectionButton.Visibility = createNewCollectionAsync is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        Items = new ObservableCollection<CatalogMembershipPickerItem>(items);
        foreach (var item in Items)
        {
            item.PropertyChanged += MembershipItem_PropertyChanged;
        }

        _filteredItems = new ListCollectionView(Items)
        {
            Filter = IsItemVisible
        };
        MembershipList.ItemsSource = _filteredItems;
        UpdateSelectionSummary();
    }

    public ObservableCollection<CatalogMembershipPickerItem> Items { get; }

    public IReadOnlyCollection<string> SelectedIds =>
        Items.Where(item => item.IsSelected).Select(item => item.Id).ToArray();

    private bool IsItemVisible(object item) =>
        item is CatalogMembershipPickerItem choice &&
        (string.IsNullOrWhiteSpace(SearchBox.Text) ||
         choice.SearchText.Contains(SearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _filteredItems.Refresh();
        UpdateSelectionSummary();
    }

    private void SelectShown_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _filteredItems.Cast<CatalogMembershipPickerItem>().ToArray())
        {
            item.IsSelected = true;
        }
    }

    private void ClearShown_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _filteredItems.Cast<CatalogMembershipPickerItem>().ToArray())
        {
            item.IsSelected = false;
        }
    }

    private void MembershipItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogMembershipPickerItem.IsSelected))
        {
            UpdateSelectionSummary();
        }
    }

    private void UpdateSelectionSummary()
    {
        var selectedCount = Items.Count(item => item.IsSelected);
        var shownCount = _filteredItems.Cast<CatalogMembershipPickerItem>().Count();
        SelectionSummary.Text = $"{selectedCount} selected · {shownCount} shown";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void CreateCollectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_createNewCollectionAsync is null)
        {
            return;
        }

        CreateCollectionButton.IsEnabled = false;
        try
        {
            if (await _createNewCollectionAsync(this) is not { } newCollection)
            {
                return;
            }

            var existing = Items.FirstOrDefault(item => item.Id == newCollection.Id);
            if (existing is not null)
            {
                existing.IsSelected = true;
                return;
            }

            newCollection.IsSelected = true;
            newCollection.PropertyChanged += MembershipItem_PropertyChanged;
            Items.Add(newCollection);
            _filteredItems.Refresh();
            UpdateSelectionSummary();
        }
        finally
        {
            CreateCollectionButton.IsEnabled = true;
        }
    }
}
