using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed record CatalogCategoryFilterOption(string? CategoryId, string Name);
public sealed record CatalogCollectionFilterOption(string? CollectionId, string Name, IReadOnlySet<string> FeedIds);
public enum CatalogFeedHealthFilter { All, NotChecked, Succeeded, Failed }
public sealed record CatalogHealthFilterOption(CatalogFeedHealthFilter Filter, string Name);

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
    private readonly CatalogFeedPreviewService? _catalogFeedPreviewService;
    private string _feedName = string.Empty;
    private string _feedUrl = string.Empty;
    private string _feedWebsiteUrl = string.Empty;
    private string _feedDescription = string.Empty;
    private string _categoryName = string.Empty;
    private string _collectionName = string.Empty;
    private string _errorMessage = string.Empty;
    private string _importMessage = string.Empty;
    private string _searchQuery = string.Empty;
    private bool _showMetadataGapsOnly;
    private CatalogCategoryFilterOption? _selectedCategoryFilter;
    private CatalogCollectionFilterOption? _selectedCollectionFilter;
    private CatalogHealthFilterOption? _selectedHealthFilter;
    private CatalogCategory? _selectedCategory;
    private CatalogFeedListItem? _selectedFeed;
    private CatalogCollection? _selectedCollection;
    private string? _editingFeedId;
    private string? _editingCategoryId;

    public CatalogManagementViewModel(
        Profile actor,
        CatalogService catalogService,
        CatalogFeedPreviewService? catalogFeedPreviewService = null)
    {
        _actor = actor;
        _catalogService = catalogService;
        _catalogFeedPreviewService = catalogFeedPreviewService;
        AddFeedCommand = new AsyncCommand(AddFeedAsync);
        CheckFeedHealthCommand = new AsyncCommand<CatalogFeedListItem>(CheckFeedHealthAsync);
        LoadStarterPackCommand = new AsyncCommand(LoadStarterPackAsync);
        DeleteFeedCommand = new AsyncCommand<CatalogFeedListItem>(DeleteFeedAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        AddCategoryCommand = new AsyncCommand(AddCategoryAsync);
        MergeCategoryCommand = new AsyncCommand(MergeCategoryIntoExistingAsync);
        DeleteCategoryCommand = new AsyncCommand<CatalogCategory>(DeleteCategoryAsync);
        AddCollectionCommand = new AsyncCommand(AddCollectionAsync);
        DeleteCollectionCommand = new AsyncCommand<CatalogCollection>(DeleteCollectionAsync);
        AddFeedToCollectionCommand = new AsyncCommand(AddFeedToCollectionAsync);
        RemoveFeedFromCollectionCommand = new AsyncCommand(RemoveFeedFromCollectionAsync);

        CategoryFilterOptions.Add(new CatalogCategoryFilterOption(null, "All categories (0)"));
        _selectedCategoryFilter = CategoryFilterOptions[0];
        CollectionFilterOptions.Add(new CatalogCollectionFilterOption(null, "All collections (0)", new HashSet<string>(StringComparer.Ordinal)));
        _selectedCollectionFilter = CollectionFilterOptions[0];
        HealthFilterOptions.Add(new CatalogHealthFilterOption(CatalogFeedHealthFilter.All, "All health states"));
        HealthFilterOptions.Add(new CatalogHealthFilterOption(CatalogFeedHealthFilter.NotChecked, "Not checked"));
        HealthFilterOptions.Add(new CatalogHealthFilterOption(CatalogFeedHealthFilter.Succeeded, "Last check succeeded"));
        HealthFilterOptions.Add(new CatalogHealthFilterOption(CatalogFeedHealthFilter.Failed, "Last check failed"));
        _selectedHealthFilter = HealthFilterOptions[0];

        VisibleFeeds = CreateVisibleFeedView(Feeds);
    }

    public ObservableCollection<CatalogFeedListItem> Feeds { get; private set; } = [];
    internal Func<CancellationToken, Task>? CatalogRefreshRequested { get; set; }
    public ICollectionView VisibleFeeds { get; private set; }
    public ObservableCollection<CatalogCategory> Categories { get; } = [];
    public ObservableCollection<CatalogCollection> Collections { get; } = [];
    public ObservableCollection<CatalogCategoryFilterOption> CategoryFilterOptions { get; } = [];
    public ObservableCollection<CatalogCollectionFilterOption> CollectionFilterOptions { get; } = [];
    public ObservableCollection<CatalogHealthFilterOption> HealthFilterOptions { get; } = [];

    public AsyncCommand AddFeedCommand { get; }
    public AsyncCommand<CatalogFeedListItem> CheckFeedHealthCommand { get; }
    public AsyncCommand LoadStarterPackCommand { get; }
    public AsyncCommand<CatalogFeedListItem> DeleteFeedCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand MergeCategoryCommand { get; }
    public AsyncCommand<CatalogCategory> DeleteCategoryCommand { get; }
    public AsyncCommand AddCollectionCommand { get; }
    public AsyncCommand<CatalogCollection> DeleteCollectionCommand { get; }
    public AsyncCommand AddFeedToCollectionCommand { get; }
    public AsyncCommand RemoveFeedFromCollectionCommand { get; }

    public string MetadataReviewSummary => $"Metadata gaps: {Feeds.Count(feed => feed.HasMetadataGaps)}";
    public int VisibleFeedCount => VisibleFeeds.Cast<CatalogFeedListItem>().Count();
    public string FeedResultsSummary => $"Showing {VisibleFeedCount} of {Feeds.Count} feeds";
    public bool IsFeedResultsEmpty => VisibleFeeds.IsEmpty;
    public string FeedResultsEmptyMessage => Feeds.Count == 0
        ? "No feeds are in the catalog yet."
        : "No feeds match these filters.";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value ?? string.Empty))
            {
                RefreshVisibleFeeds();
            }
        }
    }

    public CatalogCategoryFilterOption? SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedCategoryFilter, value))
            {
                RefreshVisibleFeeds();
            }
        }
    }

    public CatalogCollectionFilterOption? SelectedCollectionFilter
    {
        get => _selectedCollectionFilter;
        set
        {
            if (SetProperty(ref _selectedCollectionFilter, value))
            {
                RefreshVisibleFeeds();
            }
        }
    }

    public CatalogHealthFilterOption? SelectedHealthFilter
    {
        get => _selectedHealthFilter;
        set
        {
            if (SetProperty(ref _selectedHealthFilter, value))
            {
                RefreshVisibleFeeds();
            }
        }
    }

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
        set
        {
            if (SetProperty(ref _categoryName, value))
            {
                OnPropertyChanged(nameof(MergeTargetCategory));
                OnPropertyChanged(nameof(CanMergeCategory));
                OnPropertyChanged(nameof(MergeCategoryButtonLabel));
            }
        }
    }

    public CatalogCategory? MergeTargetCategory =>
        _editingCategoryId is null || string.IsNullOrWhiteSpace(CategoryName)
            ? null
            : Categories.FirstOrDefault(category =>
                category.Id != _editingCategoryId &&
                string.Equals(category.Name, CategoryName.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool CanMergeCategory => MergeTargetCategory is not null;
    public string MergeCategoryButtonLabel => MergeTargetCategory is { } target
        ? $"Merge into {target.Name}"
        : "Merge into existing";

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
        _editingCategoryId = null;
        FeedName = string.Empty;
        FeedUrl = string.Empty;
        FeedWebsiteUrl = string.Empty;
        FeedDescription = string.Empty;
        SelectedCategory = null;
        CategoryName = string.Empty;
        CollectionName = string.Empty;
        NotifyMergeCategoryProperties();
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

    public void PrepareCategoryEdit(CatalogCategory category)
    {
        ErrorMessage = string.Empty;
        _editingFeedId = null;
        _editingCategoryId = category.Id;
        CategoryName = category.Name;
        NotifyMergeCategoryProperties();
    }

    public async Task<CatalogFeedPreview> PreviewFeedAsync(
        CatalogFeedListItem feedItem,
        CancellationToken cancellationToken = default)
    {
        if (_catalogFeedPreviewService is null)
        {
            throw new InvalidOperationException("Feed preview is unavailable.");
        }

        var feed = (await _catalogService.GetFeedsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedItem.Id)
            ?? throw new InvalidOperationException("That feed no longer exists in the catalog.");
        var items = await _catalogFeedPreviewService.GetPreviewItemsAsync(feed, cancellationToken);
        return new CatalogFeedPreview(feedItem.Name, feedItem.CategoryName, feedItem.Description, items);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshAsync(cancellationToken, notifyMainCatalog: false);

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

    private async Task RefreshAsync(
        CancellationToken cancellationToken = default,
        bool notifyMainCatalog = true)
    {
        var selectedCategoryId = SelectedCategoryFilter?.CategoryId;
        var selectedCollectionId = SelectedCollectionFilter?.CollectionId;
        var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
        var sameNameCounts = feeds
            .GroupBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var collections = await _catalogService.GetCollectionsAsync(cancellationToken);
        var collectionFeedIds = await _catalogService.GetCollectionFeedIdsByCollectionAsync(cancellationToken);
        var categoryFeedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            if (feed.CategoryId is { } categoryId)
            {
                categoryFeedCounts[categoryId] = categoryFeedCounts.GetValueOrDefault(categoryId) + 1;
            }
        }

        var refreshedFeeds = new ObservableCollection<CatalogFeedListItem>();
        foreach (var feed in feeds)
        {
            refreshedFeeds.Add(new CatalogFeedListItem(
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
        Feeds = refreshedFeeds;

        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }
        NotifyMergeCategoryProperties();

        CategoryFilterOptions.Clear();
        var allCategories = new CatalogCategoryFilterOption(null, $"All categories ({Feeds.Count})");
        CategoryFilterOptions.Add(allCategories);
        foreach (var category in categories.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var categoryFeedCount = categoryFeedCounts.GetValueOrDefault(category.Id);
            CategoryFilterOptions.Add(new CatalogCategoryFilterOption(
                category.Id,
                $"{category.Name} ({categoryFeedCount})"));
        }

        CollectionFilterOptions.Clear();
        var allCollections = new CatalogCollectionFilterOption(
            null,
            $"All collections ({Feeds.Count})",
            new HashSet<string>(StringComparer.Ordinal));
        CollectionFilterOptions.Add(allCollections);
        foreach (var collection in collections.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var feedIdSet = collectionFeedIds.TryGetValue(collection.Id, out var feedIds)
                ? feedIds.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            CollectionFilterOptions.Add(new CatalogCollectionFilterOption(
                collection.Id,
                $"{collection.Name} ({feedIdSet.Count})",
                feedIdSet));
        }

        _selectedCategoryFilter = CategoryFilterOptions.FirstOrDefault(option => option.CategoryId == selectedCategoryId)
            ?? allCategories;
        _selectedCollectionFilter = CollectionFilterOptions.FirstOrDefault(option => option.CollectionId == selectedCollectionId)
            ?? allCollections;
        OnPropertyChanged(nameof(SelectedCategoryFilter));
        OnPropertyChanged(nameof(SelectedCollectionFilter));

        Collections.Clear();
        foreach (var collection in collections.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            Collections.Add(collection);
        }

        VisibleFeeds = CreateVisibleFeedView(Feeds);
        OnPropertyChanged(nameof(Feeds));
        OnPropertyChanged(nameof(VisibleFeeds));
        OnPropertyChanged(nameof(MetadataReviewSummary));
        OnPropertyChanged(nameof(FeedResultsEmptyMessage));
        RefreshVisibleFeeds();

        if (notifyMainCatalog && CatalogRefreshRequested is { } refreshMainCatalogAsync)
        {
            await refreshMainCatalogAsync(cancellationToken);
        }
    }

    private ListCollectionView CreateVisibleFeedView(ObservableCollection<CatalogFeedListItem> feeds) => new(feeds)
    {
        Filter = IsFeedVisible,
        CustomSort = Comparer<CatalogFeedListItem>.Create((left, right) =>
        {
            var nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.Ordinal.Compare(left.Id, right.Id);
        })
    };

    private void RefreshVisibleFeeds()
    {
        VisibleFeeds.Refresh();
        OnPropertyChanged(nameof(VisibleFeedCount));
        OnPropertyChanged(nameof(FeedResultsSummary));
        OnPropertyChanged(nameof(IsFeedResultsEmpty));
        OnPropertyChanged(nameof(FeedResultsEmptyMessage));
    }

    private bool IsFeedVisible(object item)
    {
        if (item is not CatalogFeedListItem feed ||
            (ShowMetadataGapsOnly && !feed.HasMetadataGaps) ||
            (SelectedCategoryFilter?.CategoryId is { } categoryId && feed.CategoryId != categoryId) ||
            (SelectedCollectionFilter?.CollectionId is not null &&
             !SelectedCollectionFilter.FeedIds.Contains(feed.Id)))
        {
            return false;
        }

        var healthFilter = SelectedHealthFilter?.Filter ?? CatalogFeedHealthFilter.All;
        if (healthFilter == CatalogFeedHealthFilter.NotChecked && feed.LastHealthCheckedAt is not null ||
            healthFilter == CatalogFeedHealthFilter.Succeeded && feed.LastHealthCheckSucceeded != true ||
            healthFilter == CatalogFeedHealthFilter.Failed &&
            (feed.LastHealthCheckedAt is null || feed.LastHealthCheckSucceeded != false))
        {
            return false;
        }

        var searchTerms = SearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (searchTerms.Length == 0)
        {
            return true;
        }

        var searchableText = string.Join(" ", new[]
        {
            feed.Name,
            feed.Description,
            feed.CategoryName,
            feed.FeedUrl,
            feed.WebsiteUrl
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return searchTerms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        SelectedCategoryFilter = CategoryFilterOptions.FirstOrDefault(option => option.CategoryId is null);
        SelectedCollectionFilter = CollectionFilterOptions.FirstOrDefault(option => option.CollectionId is null);
        SelectedHealthFilter = HealthFilterOptions[0];
        ShowMetadataGapsOnly = false;
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
            if (_editingCategoryId is { } categoryId)
            {
                await _catalogService.UpdateCategoryAsync(_actor, categoryId, CategoryName);
                _editingCategoryId = null;
            }
            else
            {
                await _catalogService.AddCategoryAsync(_actor, CategoryName);
            }

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

    private async Task MergeCategoryIntoExistingAsync()
    {
        ErrorMessage = string.Empty;
        if (_editingCategoryId is not { } sourceCategoryId || MergeTargetCategory is not { } targetCategory)
        {
            return;
        }

        try
        {
            await _catalogService.MergeCategoriesAsync(_actor, sourceCategoryId, targetCategory.Id);
            _editingCategoryId = null;
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

    private void NotifyMergeCategoryProperties()
    {
        OnPropertyChanged(nameof(MergeTargetCategory));
        OnPropertyChanged(nameof(CanMergeCategory));
        OnPropertyChanged(nameof(MergeCategoryButtonLabel));
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