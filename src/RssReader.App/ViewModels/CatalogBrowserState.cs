using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed class CatalogBrowserState : ObservableObject
{
    private readonly CatalogService? _catalogService;
    private readonly bool _canFollowFeeds;
    private string _catalogSearchQuery = string.Empty;
    private CatalogCategoryOption? _selectedCatalogCategory;
    private CatalogCollectionOption? _selectedCatalogCollection;
    private bool _hideFollowedCatalogFeeds;
    private bool _isCatalogLoading;
    private bool _isCatalogLoaded;
    private string _catalogLoadErrorMessage = string.Empty;

    public CatalogBrowserState(
        CatalogService? catalogService,
        bool canFollowFeeds,
        bool hideFollowedCatalogFeeds)
    {
        _catalogService = catalogService;
        _canFollowFeeds = canFollowFeeds;
        _hideFollowedCatalogFeeds = hideFollowedCatalogFeeds;
        CatalogFeeds = new BulkObservableCollection<CatalogFeedListItem>();
        CatalogCategories = [];
        CatalogCategoryOptions = [];
        var allCategoriesOption = new CatalogCategoryOption(null, $"All categories ({CatalogFeeds.Count})");
        CatalogCategoryOptions.Add(allCategoriesOption);
        _selectedCatalogCategory = allCategoriesOption;
        CatalogCollectionOptions = [];
        var allCollectionsOption = new CatalogCollectionOption(
            null,
            $"All collections ({CatalogFeeds.Count})",
            new HashSet<string>(StringComparer.Ordinal));
        CatalogCollectionOptions.Add(allCollectionsOption);
        _selectedCatalogCollection = allCollectionsOption;
        CatalogFeedListView = new ListCollectionView(CatalogFeeds)
        {
            Filter = IsCatalogFeedVisible
        };
        CatalogFeedListView.SortDescriptions.Add(
            new SortDescription(nameof(CatalogFeedListItem.Name), ListSortDirection.Ascending));
        CatalogCollections = [];
    }

    public BulkObservableCollection<CatalogFeedListItem> CatalogFeeds { get; }
    public ObservableCollection<CatalogCategory> CatalogCategories { get; }
    public ObservableCollection<CatalogCategoryOption> CatalogCategoryOptions { get; }
    public ObservableCollection<CatalogCollectionOption> CatalogCollectionOptions { get; }
    public ICollectionView CatalogFeedListView { get; }
    public ObservableCollection<CatalogCollection> CatalogCollections { get; }

    public string CatalogSearchQuery
    {
        get => _catalogSearchQuery;
        set
        {
            if (SetProperty(ref _catalogSearchQuery, value ?? string.Empty))
            {
                RefreshCatalogFeedView();
            }
        }
    }

    public CatalogCategoryOption? SelectedCatalogCategory
    {
        get => _selectedCatalogCategory;
        set
        {
            if (SetProperty(ref _selectedCatalogCategory, value))
            {
                RefreshCatalogFeedView();
            }
        }
    }

    public CatalogCollectionOption? SelectedCatalogCollection
    {
        get => _selectedCatalogCollection;
        set
        {
            if (SetProperty(ref _selectedCatalogCollection, value))
            {
                OnPropertyChanged(nameof(SelectedCatalogCollectionCuratorLabel));
                RefreshCatalogFeedView();
            }
        }
    }

    public string SelectedCatalogCollectionCuratorLabel =>
        SelectedCatalogCollection?.CollectionId is null ? string.Empty : "Curated by Catalog Master";

    public bool HideFollowedCatalogFeeds
    {
        get => _hideFollowedCatalogFeeds;
        set
        {
            if (SetProperty(ref _hideFollowedCatalogFeeds, value))
            {
                RefreshCatalogFeedView();
            }
        }
    }

    public bool IsCatalogFilterActive =>
        !string.IsNullOrWhiteSpace(CatalogSearchQuery) ||
        SelectedCatalogCategory?.CategoryId is not null ||
        SelectedCatalogCollection?.CollectionId is not null ||
        HideFollowedCatalogFeeds;

    public bool IsCatalogResultsEmpty =>
        IsCatalogLoaded && !IsCatalogLoading && !HasCatalogLoadError && CatalogFeedListView.IsEmpty;

    public string CatalogResultsSummary =>
        !IsCatalogLoaded && IsCatalogLoading ? "Loading feed catalog..." :
        !IsCatalogLoaded && HasCatalogLoadError ? "Catalog unavailable" :
        IsCatalogFilterActive
            ? $"{CatalogFeedListView.Cast<CatalogFeedListItem>().Count()} of {CatalogFeeds.Count} feeds"
            : $"{CatalogFeeds.Count} feeds";

    public string CatalogResultsEmptyMessage => CatalogFeeds.Count == 0
        ? "No feeds are available in the catalog yet."
        : "No feeds match these filters.";

    public bool IsCatalogLoading
    {
        get => _isCatalogLoading;
        private set
        {
            if (SetProperty(ref _isCatalogLoading, value))
            {
                OnPropertyChanged(nameof(IsCatalogResultsEmpty));
                OnPropertyChanged(nameof(CatalogResultsSummary));
            }
        }
    }

    public bool IsCatalogLoaded
    {
        get => _isCatalogLoaded;
        private set
        {
            if (SetProperty(ref _isCatalogLoaded, value))
            {
                OnPropertyChanged(nameof(IsCatalogResultsEmpty));
                OnPropertyChanged(nameof(CatalogResultsSummary));
            }
        }
    }

    public string CatalogLoadErrorMessage
    {
        get => _catalogLoadErrorMessage;
        private set
        {
            if (SetProperty(ref _catalogLoadErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasCatalogLoadError));
                OnPropertyChanged(nameof(IsCatalogResultsEmpty));
                OnPropertyChanged(nameof(CatalogResultsSummary));
            }
        }
    }

    public bool HasCatalogLoadError => !string.IsNullOrWhiteSpace(CatalogLoadErrorMessage);
    public int SelectedCatalogFeedCount => CatalogFeeds.Count(feed => feed.IsSelectedForFollow);
    public bool HasSelectedCatalogFeeds => SelectedCatalogFeedCount > 0;
    public bool CanFollowSelectedCatalogFeeds => _canFollowFeeds && HasSelectedCatalogFeeds;
    public string FollowSelectedCatalogFeedsLabel => $"Follow selected ({SelectedCatalogFeedCount})";
    public bool CanSelectVisibleCatalogFeeds => GetVisibleSelectableCatalogFeeds().Length > 0;
    public bool? VisibleCatalogFeedSelectionState
    {
        get
        {
            var selectableFeeds = GetVisibleSelectableCatalogFeeds();
            var selectedCount = selectableFeeds.Count(feed => feed.IsSelectedForFollow);
            return selectedCount == 0 ? false :
                selectedCount == selectableFeeds.Length ? true : null;
        }
    }

    public bool CanUnfollowAllCatalogFeeds =>
        _canFollowFeeds && GetVisibleSubscribedCatalogFeeds().Length > 0;

    public async Task LoadAsync(
        CancellationToken cancellationToken,
        IReadOnlySet<string> subscribedFeedIds,
        Func<CancellationToken, Task>? initializeCatalogManagement)
    {
        if (_catalogService is null)
        {
            return;
        }

        IsCatalogLoading = true;
        CatalogLoadErrorMessage = string.Empty;
        try
        {
            var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
            var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
            var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
            var collections = await _catalogService.GetCollectionsAsync(cancellationToken);
            var collectionFeedIds = await _catalogService.GetCollectionFeedIdsByCollectionAsync(cancellationToken);
            var selectedCollectionId = _selectedCatalogCollection?.CollectionId;
            var categoryFeedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var feed in feeds)
            {
                if (feed.CategoryId is { } categoryId)
                {
                    categoryFeedCounts[categoryId] = categoryFeedCounts.GetValueOrDefault(categoryId) + 1;
                }
            }

            CatalogCategories.Clear();
            CatalogCategoryOptions.Clear();
            var allCategoriesOption = new CatalogCategoryOption(null, $"All categories ({feeds.Count})");
            CatalogCategoryOptions.Add(allCategoriesOption);
            foreach (var category in categories.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                CatalogCategories.Add(category);
                var categoryFeedCount = categoryFeedCounts.GetValueOrDefault(category.Id);
                CatalogCategoryOptions.Add(new CatalogCategoryOption(
                    category.Id,
                    $"{category.Name} ({categoryFeedCount})"));
            }

            SelectedCatalogCategory = CatalogCategoryOptions.FirstOrDefault(option =>
                option.CategoryId == _selectedCatalogCategory?.CategoryId) ?? allCategoriesOption;

            CatalogCollections.Clear();
            CatalogCollectionOptions.Clear();
            var allCollectionsOption = new CatalogCollectionOption(
                null,
                $"All collections ({feeds.Count})",
                new HashSet<string>(StringComparer.Ordinal));
            CatalogCollectionOptions.Add(allCollectionsOption);
            foreach (var collection in collections)
            {
                CatalogCollections.Add(collection);
                var feedIdSet = collectionFeedIds.TryGetValue(collection.Id, out var feedIds)
                    ? feedIds.ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
                CatalogCollectionOptions.Add(new CatalogCollectionOption(
                    collection.Id,
                    $"{collection.Name} ({feedIdSet.Count})",
                    feedIdSet));
            }

            SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(option =>
                option.CollectionId == selectedCollectionId) ?? allCollectionsOption;

            var catalogFeedItems = new List<CatalogFeedListItem>(feeds.Count);
            foreach (var feed in feeds)
            {
                var catalogFeed = new CatalogFeedListItem(
                    feed.Id,
                    feed.Name,
                    feed.FeedUrl,
                    feed.Description,
                    feed.CategoryId is not null && categoryNames.TryGetValue(feed.CategoryId, out var categoryName)
                        ? categoryName
                        : null,
                    feed.CategoryId,
                    feed.WebsiteUrl);
                catalogFeed.IsSubscribed = subscribedFeedIds.Contains(feed.Id);
                catalogFeed.PropertyChanged += OnCatalogFeedPropertyChanged;
                catalogFeedItems.Add(catalogFeed);
            }

            using (CatalogFeedListView.DeferRefresh())
            {
                CatalogFeeds.ReplaceAll(catalogFeedItems);
            }

            NotifyCatalogFeedViewChanged();
            if (initializeCatalogManagement is not null)
            {
                await initializeCatalogManagement(cancellationToken);
            }

            IsCatalogLoaded = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            CatalogLoadErrorMessage = $"Could not load the feed catalog: {exception.Message}";
        }
        finally
        {
            IsCatalogLoading = false;
        }
    }

    public void UpdateSubscriptions(IReadOnlySet<string> subscribedFeedIds)
    {
        foreach (var feed in CatalogFeeds)
        {
            feed.IsSubscribed = subscribedFeedIds.Contains(feed.Id);
        }

        RefreshCatalogFeedView();
    }

    public CatalogFeedListItem[] GetVisibleSelectableCatalogFeeds() =>
        CatalogFeedListView.Cast<CatalogFeedListItem>()
            .Where(feed => feed.CanSelectForFollow)
            .ToArray();

    public CatalogFeedListItem[] GetVisibleSubscribedCatalogFeeds() =>
        CatalogFeedListView.Cast<CatalogFeedListItem>()
            .Where(feed => feed.IsSubscribed)
            .ToArray();

    public void RefreshCatalogFeedView()
    {
        CatalogFeedListView.Refresh();
        NotifyCatalogFeedViewChanged();
    }

    private bool IsCatalogFeedVisible(object item)
    {
        if (item is not CatalogFeedListItem feed ||
            (HideFollowedCatalogFeeds && feed.IsSubscribed) ||
            (SelectedCatalogCategory?.CategoryId is { } categoryId && feed.CategoryId != categoryId) ||
            (SelectedCatalogCollection?.CollectionId is not null &&
             !SelectedCatalogCollection.FeedIds.Contains(feed.Id)))
        {
            return false;
        }

        var searchTerms = CatalogSearchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (searchTerms.Length == 0)
        {
            return true;
        }

        var searchableText = string.Join(" ", new[]
        {
            feed.Name,
            feed.CategoryName,
            feed.Description,
            feed.FeedUrl,
            feed.WebsiteUrl
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return searchTerms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private void OnCatalogFeedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CatalogFeedListItem.IsSelectedForFollow))
        {
            OnPropertyChanged(nameof(SelectedCatalogFeedCount));
            OnPropertyChanged(nameof(HasSelectedCatalogFeeds));
            OnPropertyChanged(nameof(CanFollowSelectedCatalogFeeds));
            OnPropertyChanged(nameof(FollowSelectedCatalogFeedsLabel));
            OnPropertyChanged(nameof(VisibleCatalogFeedSelectionState));
        }

        if (e.PropertyName == nameof(CatalogFeedListItem.IsSubscribed))
        {
            OnPropertyChanged(nameof(CanUnfollowAllCatalogFeeds));
            OnPropertyChanged(nameof(VisibleCatalogFeedSelectionState));
        }
    }

    private void NotifyCatalogFeedViewChanged()
    {
        OnPropertyChanged(nameof(IsCatalogFilterActive));
        OnPropertyChanged(nameof(IsCatalogResultsEmpty));
        OnPropertyChanged(nameof(CatalogResultsSummary));
        OnPropertyChanged(nameof(CatalogResultsEmptyMessage));
        OnPropertyChanged(nameof(CanSelectVisibleCatalogFeeds));
        OnPropertyChanged(nameof(VisibleCatalogFeedSelectionState));
        OnPropertyChanged(nameof(CanUnfollowAllCatalogFeeds));
    }
}
