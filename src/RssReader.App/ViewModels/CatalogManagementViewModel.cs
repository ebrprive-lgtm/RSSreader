using System.Collections.ObjectModel;
using System.IO;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed class CatalogManagementViewModel : ObservableObject
{
    private static readonly OpmlFeed[] StarterFeeds =
    [
        new("BBC News", "https://feeds.bbci.co.uk/news/rss.xml", "BBC News front page", "News"),
        new("NPR News", "https://feeds.npr.org/1001/rss.xml", "NPR news and reporting", "News"),
        new("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index", "All Ars Technica stories", "Technology"),
        new("The Verge", "https://www.theverge.com/rss/index.xml", "Technology and culture", "Technology"),
        new("NASA", "https://www.nasa.gov/feed/", "Official NASA news", "Science"),
        new("The GitHub Blog", "https://github.blog/feed/", "GitHub product and engineering updates", "Developer tools")
    ];

    private readonly Profile _actor;
    private readonly CatalogService _catalogService;
    private string _feedName = string.Empty;
    private string _feedUrl = string.Empty;
    private string _feedWebsiteUrl = string.Empty;
    private string _feedDescription = string.Empty;
    private string _categoryName = string.Empty;
    private string _collectionName = string.Empty;
    private string _errorMessage = string.Empty;
    private string _importMessage = string.Empty;
    private bool _showMetadataGapsOnly;
    private CatalogCategory? _selectedCategory;
    private CatalogFeedListItem? _selectedFeed;
    private CatalogCollection? _selectedCollection;
    private string? _editingFeedId;

    public CatalogManagementViewModel(Profile actor, CatalogService catalogService)
    {
        _actor = actor;
        _catalogService = catalogService;
        AddFeedCommand = new AsyncCommand(AddFeedAsync);
        CheckFeedHealthCommand = new AsyncCommand<CatalogFeedListItem>(CheckFeedHealthAsync);
        LoadStarterPackCommand = new AsyncCommand(LoadStarterPackAsync);
        DeleteFeedCommand = new AsyncCommand<CatalogFeedListItem>(DeleteFeedAsync);
        AddCategoryCommand = new AsyncCommand(AddCategoryAsync);
        DeleteCategoryCommand = new AsyncCommand<CatalogCategory>(DeleteCategoryAsync);
        AddCollectionCommand = new AsyncCommand(AddCollectionAsync);
        DeleteCollectionCommand = new AsyncCommand<CatalogCollection>(DeleteCollectionAsync);
        AddFeedToCollectionCommand = new AsyncCommand(AddFeedToCollectionAsync);
        RemoveFeedFromCollectionCommand = new AsyncCommand(RemoveFeedFromCollectionAsync);
    }

    public ObservableCollection<CatalogFeedListItem> Feeds { get; } = [];
    public ObservableCollection<CatalogFeedListItem> VisibleFeeds { get; } = [];
    public ObservableCollection<CatalogCategory> Categories { get; } = [];
    public ObservableCollection<CatalogCollection> Collections { get; } = [];

    public AsyncCommand AddFeedCommand { get; }
    public AsyncCommand<CatalogFeedListItem> CheckFeedHealthCommand { get; }
    public AsyncCommand LoadStarterPackCommand { get; }
    public AsyncCommand<CatalogFeedListItem> DeleteFeedCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand<CatalogCategory> DeleteCategoryCommand { get; }
    public AsyncCommand AddCollectionCommand { get; }
    public AsyncCommand<CatalogCollection> DeleteCollectionCommand { get; }
    public AsyncCommand AddFeedToCollectionCommand { get; }
    public AsyncCommand RemoveFeedFromCollectionCommand { get; }

    public string MetadataReviewSummary => $"Metadata gaps: {Feeds.Count(feed => feed.HasMetadataGaps)}";

    public bool ShowMetadataGapsOnly
    {
        get => _showMetadataGapsOnly;
        set
        {
            if (SetProperty(ref _showMetadataGapsOnly, value))
            {
                RefreshVisibleFeeds();
            }
        }
    }

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

    public string FeedWebsiteUrl
    {
        get => _feedWebsiteUrl;
        set => SetProperty(ref _feedWebsiteUrl, value);
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

    public string ImportMessage
    {
        get => _importMessage;
        private set => SetProperty(ref _importMessage, value);
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

    public void ResetEntryForm()
    {
        ErrorMessage = string.Empty;
        _editingFeedId = null;
        FeedName = string.Empty;
        FeedUrl = string.Empty;
        FeedWebsiteUrl = string.Empty;
        FeedDescription = string.Empty;
        SelectedCategory = null;
        CategoryName = string.Empty;
        CollectionName = string.Empty;
    }

    public void PrepareFeedEdit(CatalogFeedListItem feed)
    {
        ErrorMessage = string.Empty;
        _editingFeedId = feed.Id;
        FeedName = feed.Name;
        FeedUrl = feed.FeedUrl;
        FeedWebsiteUrl = feed.WebsiteUrl ?? string.Empty;
        FeedDescription = feed.Description ?? string.Empty;
        SelectedCategory = Categories.FirstOrDefault(category => category.Id == feed.CategoryId);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken);

    public async Task ImportOpmlAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ErrorMessage = string.Empty;
        ImportMessage = string.Empty;
        try
        {
            var parsed = OpmlFeedParser.Parse(stream);
            await ImportFeedsAsync(parsed.Feeds, parsed.SkippedCount, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = $"Could not import OPML: {exception.Message}";
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
        var sameNameCounts = feeds
            .GroupBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
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
                    : null,
                feed.CategoryId,
                feed.WebsiteUrl,
                sameNameCounts[feed.Name],
                feed.LastHealthCheckedAt,
                feed.LastHealthCheckSucceeded));
        }

            OnPropertyChanged(nameof(MetadataReviewSummary));
            RefreshVisibleFeeds();

        Collections.Clear();
        foreach (var collection in collections)
        {
            Collections.Add(collection);
        }
    }

    private void RefreshVisibleFeeds()
    {
        VisibleFeeds.Clear();
        foreach (var feed in Feeds)
        {
            if (!ShowMetadataGapsOnly || feed.HasMetadataGaps)
            {
                VisibleFeeds.Add(feed);
            }
        }
    }

    private async Task AddFeedAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            if (_editingFeedId is { } feedId)
            {
                await _catalogService.UpdateFeedAsync(
                    _actor,
                    feedId,
                    FeedName,
                    FeedUrl,
                    FeedDescription,
                    SelectedCategory?.Id,
                    websiteUrl: FeedWebsiteUrl);
            }
            else
            {
                await _catalogService.AddFeedAsync(
                    _actor,
                    FeedName,
                    FeedUrl,
                    FeedDescription,
                    SelectedCategory?.Id,
                    websiteUrl: FeedWebsiteUrl);
            }

            ResetEntryForm();
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

    private async Task LoadStarterPackAsync()
    {
        ErrorMessage = string.Empty;
        ImportMessage = string.Empty;
        await ImportFeedsAsync(StarterFeeds, 0, CancellationToken.None);
    }

    private async Task ImportFeedsAsync(
        IReadOnlyList<OpmlFeed> feeds,
        int parserSkippedCount,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _catalogService.ImportFeedsAsync(_actor, feeds, cancellationToken);
            await RefreshAsync(cancellationToken);
            ImportMessage = $"Added {result.AddedCount} feed(s); skipped {result.SkippedCount + parserSkippedCount}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task DeleteFeedAsync(CatalogFeedListItem feed)
    {
        await _catalogService.DeleteFeedAsync(_actor, feed.Id);
        await RefreshAsync();
    }

    private async Task CheckFeedHealthAsync(CatalogFeedListItem feed)
    {
        ErrorMessage = string.Empty;
        feed.IsHealthCheckInProgress = true;
        try
        {
            await _catalogService.CheckFeedHealthAsync(_actor, feed.Id);
            await RefreshAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = $"Could not check feed: {exception.Message}";
        }
        finally
        {
            feed.IsHealthCheckInProgress = false;
        }
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