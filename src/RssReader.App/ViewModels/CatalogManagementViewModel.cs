using System.Collections.ObjectModel;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed class CatalogManagementViewModel : ObservableObject
{
    private readonly Profile _actor;
    private readonly CatalogService _catalogService;
    private string _feedName = string.Empty;
    private string _feedUrl = string.Empty;
    private string _feedDescription = string.Empty;
    private string _categoryName = string.Empty;
    private string _collectionName = string.Empty;
    private string _errorMessage = string.Empty;
    private CatalogCategory? _selectedCategory;
    private CatalogFeedListItem? _selectedFeed;
    private CatalogCollection? _selectedCollection;

    public CatalogManagementViewModel(Profile actor, CatalogService catalogService)
    {
        _actor = actor;
        _catalogService = catalogService;
        AddFeedCommand = new AsyncCommand(AddFeedAsync);
        DeleteFeedCommand = new AsyncCommand<CatalogFeedListItem>(DeleteFeedAsync);
        AddCategoryCommand = new AsyncCommand(AddCategoryAsync);
        DeleteCategoryCommand = new AsyncCommand<CatalogCategory>(DeleteCategoryAsync);
        AddCollectionCommand = new AsyncCommand(AddCollectionAsync);
        DeleteCollectionCommand = new AsyncCommand<CatalogCollection>(DeleteCollectionAsync);
        AddFeedToCollectionCommand = new AsyncCommand(AddFeedToCollectionAsync);
        RemoveFeedFromCollectionCommand = new AsyncCommand(RemoveFeedFromCollectionAsync);
    }

    public ObservableCollection<CatalogFeedListItem> Feeds { get; } = [];
    public ObservableCollection<CatalogCategory> Categories { get; } = [];
    public ObservableCollection<CatalogCollection> Collections { get; } = [];

    public AsyncCommand AddFeedCommand { get; }
    public AsyncCommand<CatalogFeedListItem> DeleteFeedCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand<CatalogCategory> DeleteCategoryCommand { get; }
    public AsyncCommand AddCollectionCommand { get; }
    public AsyncCommand<CatalogCollection> DeleteCollectionCommand { get; }
    public AsyncCommand AddFeedToCollectionCommand { get; }
    public AsyncCommand RemoveFeedFromCollectionCommand { get; }

    public string FeedName
    {
        get => _feedName;
        set => SetProperty(ref _feedName, value);
    }

    public string FeedUrl
    {
        get => _feedUrl;
        set => SetProperty(ref _feedUrl, value);
    }

    public string FeedDescription
    {
        get => _feedDescription;
        set => SetProperty(ref _feedDescription, value);
    }

    public string CategoryName
    {
        get => _categoryName;
        set => SetProperty(ref _categoryName, value);
    }

    public string CollectionName
    {
        get => _collectionName;
        set => SetProperty(ref _collectionName, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public CatalogCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => SetProperty(ref _selectedCategory, value);
    }

    public CatalogFeedListItem? SelectedFeed
    {
        get => _selectedFeed;
        set => SetProperty(ref _selectedFeed, value);
    }

    public CatalogCollection? SelectedCollection
    {
        get => _selectedCollection;
        set => SetProperty(ref _selectedCollection, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
        var collections = await _catalogService.GetCollectionsAsync(cancellationToken);

        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        Feeds.Clear();
        foreach (var feed in feeds)
        {
            Feeds.Add(new CatalogFeedListItem(
                feed.Id,
                feed.Name,
                feed.FeedUrl,
                feed.Description,
                feed.CategoryId is not null && categoryNames.TryGetValue(feed.CategoryId, out var categoryName)
                    ? categoryName
                    : null));
        }

        Collections.Clear();
        foreach (var collection in collections)
        {
            Collections.Add(collection);
        }
    }

    private async Task AddFeedAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await _catalogService.AddFeedAsync(
                _actor,
                FeedName,
                FeedUrl,
                FeedDescription,
                SelectedCategory?.Id);
            FeedName = string.Empty;
            FeedUrl = string.Empty;
            FeedDescription = string.Empty;
            await RefreshAsync();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task DeleteFeedAsync(CatalogFeedListItem feed)
    {
        await _catalogService.DeleteFeedAsync(_actor, feed.Id);
        await RefreshAsync();
    }

    private async Task AddCategoryAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await _catalogService.AddCategoryAsync(_actor, CategoryName);
            CategoryName = string.Empty;
            await RefreshAsync();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task DeleteCategoryAsync(CatalogCategory category)
    {
        await _catalogService.DeleteCategoryAsync(_actor, category.Id);
        await RefreshAsync();
    }

    private async Task AddCollectionAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            await _catalogService.AddCollectionAsync(_actor, CollectionName);
            CollectionName = string.Empty;
            await RefreshAsync();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task DeleteCollectionAsync(CatalogCollection collection)
    {
        await _catalogService.DeleteCollectionAsync(_actor, collection.Id);
        await RefreshAsync();
    }

    private async Task AddFeedToCollectionAsync()
    {
        if (SelectedFeed is null || SelectedCollection is null)
        {
            ErrorMessage = "Select a feed and collection.";
            return;
        }

        ErrorMessage = string.Empty;
        await _catalogService.AddFeedToCollectionAsync(_actor, SelectedCollection.Id, SelectedFeed.Id);
    }

    private async Task RemoveFeedFromCollectionAsync()
    {
        if (SelectedFeed is null || SelectedCollection is null)
        {
            ErrorMessage = "Select a feed and collection.";
            return;
        }

        ErrorMessage = string.Empty;
        await _catalogService.RemoveFeedFromCollectionAsync(_actor, SelectedCollection.Id, SelectedFeed.Id);
    }
}