using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed record CatalogCategoryOption(string? CategoryId, string Name);
public sealed record CatalogCollectionOption(string? CollectionId, string Name, IReadOnlySet<string> FeedIds);
public sealed record ArticleTopicOption(string? Term, string? Scheme, string Name, int Count)
{
    public string DisplayName => $"{Name} ({Count:N0})";
}

public sealed class MainWindowViewModel : ObservableObject
{
    private const int MaximumCachedCatalogFeedPreviews = 20;
    private readonly List<ArticleRowViewModel> _allArticles;
    private readonly BulkObservableCollection<CatalogFeedListItem> _catalogFeeds = [];
    private readonly BulkObservableCollection<SidebarLink> _feedLinks = [];
    private readonly BulkObservableCollection<ArticleRowViewModel> _visibleArticles = [];
    private readonly CatalogService? _catalogService;
    private readonly CatalogFeedPreviewService? _catalogFeedPreviewService;
    private readonly ReadingService? _readingService;
    private readonly FeedRefreshService? _feedRefreshService;
    private readonly Dictionary<string, CatalogFeedPreview> _catalogFeedPreviewCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _catalogFeedPreviewCacheOrder = new();
    private readonly HashSet<string> _pendingInitialRefreshFeedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedSidebarFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _subscribedFeedIds = new(StringComparer.Ordinal);
    private ProfilePreferences _profilePreferences;
    private Dictionary<string, string[]> _feedIdsByFolder = new(StringComparer.OrdinalIgnoreCase);
    private string _activeRoute;
    private string _searchQuery = string.Empty;
    private string _catalogSearchQuery = string.Empty;
    private string _quickQuery = string.Empty;
    private string _statusMessage = string.Empty;
    private string _catalogLoadErrorMessage = string.Empty;
    private CatalogCategoryOption? _selectedCatalogCategory;
    private CatalogCollectionOption? _selectedCatalogCollection;
    private ArticleTopicOption? _selectedArticleTopic;
    private ArticleRowViewModel? _selectedArticle;
    private bool _isSidebarPinned = true;
    private bool _isRefreshing;
    private bool _unreadOnly;
    private bool _savedOnly;
    private bool _isCardsView = true;
    private bool _isMagazineView;
    private bool _isSortByDate;
    private bool _hideFollowedCatalogFeeds;
    private bool _isCatalogLoading;
    private bool _isCatalogLoaded;
    private bool _hasAppliedStartPage;
    private bool _hasLoadedInitialReaderData;
    private bool _updatingArticleTopicOptions;
    private int _folderArticlesPerFeedLimit;

    public MainWindowViewModel(Profile profile) : this(profile, null, null, null)
    {
    }

    public MainWindowViewModel(Profile profile, CatalogService? catalogService) : this(profile, catalogService, null, null)
    {
    }

    public MainWindowViewModel(
        Profile profile,
        CatalogService? catalogService,
        ReadingService? readingService,
        FeedRefreshService? feedRefreshService,
        ProfilePreferences? profilePreferences = null,
        CatalogFeedPreviewService? catalogFeedPreviewService = null)
    {
        ActiveProfile = profile;
        _catalogService = catalogService;
        _catalogFeedPreviewService = catalogFeedPreviewService;
        _readingService = readingService;
        _feedRefreshService = feedRefreshService;
        _profilePreferences = profilePreferences ?? new ProfilePreferences();
        _isCardsView = _profilePreferences.Presentation == ProfileArticlePresentation.Cards;
        _isMagazineView = _profilePreferences.Presentation == ProfileArticlePresentation.Magazine;
        _isSortByDate = _profilePreferences.Sort == ProfileArticleSort.Newest;
        _unreadOnly = _profilePreferences.HideReadArticles;
        _folderArticlesPerFeedLimit = _profilePreferences.FolderArticleLimitPerFeed;
        _activeRoute = profile.IsCatalogMaster
            ? "Manage catalog"
            : _profilePreferences.StartPage == ProfileStartPage.All ? "All" : "Today";

        PrimaryLinks =
        [
            new("Today", "Today", "\uE787"),
            new("Follow sources", "Follow sources", "\uE774"),
            new("Search", "Search", "\uE721"),
            new("Go to...", "Go to...", "\uE8AD")
        ];
        ReadingLinks =
        [
            new("Read later", "Read later", "\uE734"),
            new("Recently read", "Recently read", "\uE823")
        ];
        FeedLinks = _feedLinks;
        _feedLinks.ReplaceAll(readingService is null
            ?
            [
                new SidebarLink("All", "All", "\uE8A5", "5"),
                new SidebarLink("folder:Gaming", "Gaming", string.Empty, "(2)", indentLevel: 1),
                new SidebarLink("folder:tech", "tech", string.Empty, "(3)", indentLevel: 1)
            ]
            : [new SidebarLink("All", "All", "\uE8A5")]);
        TagLinks = readingService is null
            ?
            [
                new("tag:Reviews", "Reviews", "\uE8D2"),
                new("tag:Analysis", "Analysis", "\uE8D2")
            ]
            : [];
        AdminLinks = profile.IsCatalogMaster
            ? [new SidebarLink("Manage catalog", "Manage catalog", "\uE713")]
            : [];
        QuickTargets = [];
        CatalogCategories = [];
        CatalogCategoryOptions = [];
        ArticleTopicOptions = [];
        var allCategoriesOption = new CatalogCategoryOption(null, $"All categories ({CatalogFeeds.Count})");
        CatalogCategoryOptions.Add(allCategoriesOption);
        _selectedCatalogCategory = allCategoriesOption;
        CatalogCollectionOptions = [];
        var allCollectionsOption = new CatalogCollectionOption(null, $"All collections ({CatalogFeeds.Count})", new HashSet<string>(StringComparer.Ordinal));
        CatalogCollectionOptions.Add(allCollectionsOption);
        _selectedCatalogCollection = allCollectionsOption;
        CatalogFeedListView = new ListCollectionView(CatalogFeeds);
        CatalogFeedListView.Filter = IsCatalogFeedVisible;
        CatalogFeedListView.SortDescriptions.Add(
            new SortDescription(nameof(CatalogFeedListItem.Name), ListSortDirection.Ascending));
        CatalogCollections = [];
        FolderNames = [];
        CatalogManagement = profile.IsCatalogMaster && catalogService is not null
            ? new CatalogManagementViewModel(profile, catalogService, catalogFeedPreviewService)
            : null;
        if (CatalogManagement is not null)
        {
            CatalogManagement.CatalogRefreshRequested = RefreshCatalogAfterManagementChangeAsync;
        }
        ApplyQuickFilter();

        var now = DateTimeOffset.Now;
        _allArticles = readingService is null ?
        [
            new("The next generation of handheld gaming is here", "The Verge", now.AddMinutes(-42), "Gaming", ["Reviews"], "A closer look at the latest handheld hardware and what it means for portable play."),
            new("A new chapter for the world of Hyrule", "Nintendo Life", now.AddHours(-3), "Gaming", ["Analysis"], "The latest announcement brings new details about the upcoming adventure."),
            new("Windows gets a quieter, faster update path", "Ars Technica", now.AddHours(-7), "tech", ["Analysis"], "A technical overview of the changes rolling out to desktop users." , isRead: true),
            new("The small tools making a big difference", "The Register", now.AddDays(-1), "tech", ["Reviews"], "A roundup of focused utilities for a more productive desktop." , isSaved: true),
            new("Why open standards still matter", "Wired", now.AddDays(-2), "tech", ["Analysis"], "A look at interoperability and the long life of open formats." , isRead: true, isSaved: true)
        ] : [];
        VisibleArticles = _visibleArticles;
        FolderSortedArticleListView = CreateArticleListView(sortByDate: false);
        DateSortedArticleListView = CreateArticleListView(sortByDate: true);
        ArticleListView = CreateArticleListView(sortByDate: false);

        NavigateCommand = new RelayCommand<SidebarLink>(NavigateTo);
        ActivateSidebarLinkCommand = new RelayCommand<SidebarLink>(ActivateSidebarLink);
        SelectArticleCommand = new RelayCommand<ArticleRowViewModel>(OpenArticle);
        NavigateToSelectedArticleFeedCommand = new RelayCommand(NavigateToSelectedArticleFeed);
        BackToListCommand = new RelayCommand(() => SelectedArticle = null);
        ToggleSavedCommand = new RelayCommand(ToggleSaved);
        ToggleReadCommand = new RelayCommand(ToggleRead);
        SwitchProfileCommand = new RelayCommand(() => ProfileSwitchRequested?.Invoke());
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ClearCatalogFiltersCommand = new RelayCommand(ClearCatalogFilters);
        ClearArticleTopicCommand = new RelayCommand(ClearArticleTopicFilter);
        RetryCatalogLoadCommand = new AsyncCommand(() => LoadCatalogAsync(CancellationToken.None), () => !IsCatalogLoading);
        FollowSelectedCatalogFeedsCommand = new AsyncCommand(FollowSelectedCatalogFeedsAsync);
        ClearSelectedCatalogFeedsCommand = new RelayCommand(ClearSelectedCatalogFeeds, () => HasSelectedCatalogFeeds);
        ToggleVisibleCatalogFeedSelectionCommand = new RelayCommand(ToggleVisibleCatalogFeedSelection);
        UnfollowAllCatalogFeedsCommand = new AsyncCommand(UnfollowAllCatalogFeedsAsync);
        DeleteFolderCommand = new AsyncCommand<SidebarLink>(
            DeleteFolderAsync,
            link => _readingService is not null && link.IsFolder);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing && !IsCatalogMaster);
        ToggleSubscriptionCommand = new AsyncCommand<CatalogFeedListItem>(ToggleSubscriptionAsync);

        UpdateSelectedLinks();
        ApplyArticleFilters();
    }

    public Profile ActiveProfile { get; }
    public string ActiveProfileName => ActiveProfile.Name;
    public bool IsCatalogMaster => ActiveProfile.IsCatalogMaster;
    public bool IsSidebarPinned => _isSidebarPinned;
    public bool IsSidebarExpanded => true;
    public GridLength SidebarColumnWidth => new(IsSidebarPinned ? 286 : 0);
    public double SidebarPanelWidth => 286;
    public int SidebarColumnSpan => IsSidebarPinned ? 1 : 2;
    public Thickness SidebarHeaderMargin => IsSidebarPinned ? new Thickness(0) : new Thickness(42, 0, 0, 0);

    public ObservableCollection<SidebarLink> PrimaryLinks { get; }
    public ObservableCollection<SidebarLink> ReadingLinks { get; }
    public ObservableCollection<SidebarLink> FeedLinks { get; }
    public ObservableCollection<SidebarLink> TagLinks { get; }
    public ObservableCollection<SidebarLink> AdminLinks { get; }
    public ObservableCollection<ArticleRowViewModel> VisibleArticles { get; }
    public ICollectionView ArticleListView { get; private set; }
    public ICollectionView FolderSortedArticleListView { get; }
    public ICollectionView DateSortedArticleListView { get; }
    public ObservableCollection<SidebarLink> QuickTargets { get; }
    public ObservableCollection<CatalogFeedListItem> CatalogFeeds => _catalogFeeds;
    public ObservableCollection<CatalogCategory> CatalogCategories { get; }
    public ObservableCollection<CatalogCategoryOption> CatalogCategoryOptions { get; }
    public ObservableCollection<CatalogCollectionOption> CatalogCollectionOptions { get; }
    public ObservableCollection<ArticleTopicOption> ArticleTopicOptions { get; }
    public ICollectionView CatalogFeedListView { get; }
    public ObservableCollection<CatalogCollection> CatalogCollections { get; }
    public ObservableCollection<string> FolderNames { get; }
    public CatalogManagementViewModel? CatalogManagement { get; }

    public RelayCommand<SidebarLink> NavigateCommand { get; }
    public RelayCommand<SidebarLink> ActivateSidebarLinkCommand { get; }
    public RelayCommand<ArticleRowViewModel> SelectArticleCommand { get; }
    public RelayCommand NavigateToSelectedArticleFeedCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public RelayCommand ToggleSavedCommand { get; }
    public RelayCommand ToggleReadCommand { get; }
    public RelayCommand SwitchProfileCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand ClearCatalogFiltersCommand { get; }
    public RelayCommand ClearArticleTopicCommand { get; }
    public AsyncCommand RetryCatalogLoadCommand { get; }
    public AsyncCommand FollowSelectedCatalogFeedsCommand { get; }
    public RelayCommand ClearSelectedCatalogFeedsCommand { get; }
    public RelayCommand ToggleVisibleCatalogFeedSelectionCommand { get; }
    public AsyncCommand UnfollowAllCatalogFeedsCommand { get; }
    public AsyncCommand<SidebarLink> DeleteFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public AsyncCommand<CatalogFeedListItem> ToggleSubscriptionCommand { get; }

    public Task<string> GetRawArticleContentAsync(
        ArticleRowViewModel article,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(article);
        if (_feedRefreshService is null)
        {
            throw new InvalidOperationException("Raw feed content is unavailable.");
        }

        if (article.FeedId is not { } feedId)
        {
            throw new InvalidOperationException("The selected article is not associated with a feed.");
        }

        return _feedRefreshService.GetRawArticleContentAsync(
            ActiveProfile.Id,
            feedId,
            article.ExternalId,
            article.Link,
            article.Title,
            cancellationToken);
    }

    public async Task<(IReadOnlyList<string> AvailableTags, IReadOnlyList<string> AssignedTags)> GetFeedTagEditorDataAsync(
        string feedId,
        CancellationToken cancellationToken = default)
    {
        var readingService = _readingService ?? throw new InvalidOperationException("Feed tags are unavailable.");
        var subscriptions = await readingService.GetSubscriptionsAsync(ActiveProfile.Id, cancellationToken);
        if (subscriptions.All(subscription => subscription.FeedId != feedId))
        {
            throw new ArgumentException("The feed is not subscribed in this profile.", nameof(feedId));
        }

        var tags = await readingService.GetFeedTagsAsync(ActiveProfile.Id, cancellationToken);
        var availableTags = tags
            .Select(tag => tag.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var assignedTags = tags
            .Where(tag => tag.FeedId == feedId)
            .Select(tag => tag.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (availableTags, assignedTags);
    }

    public async Task UpdateFeedTagsAsync(
        string feedId,
        IEnumerable<string> tagNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tagNames);
        var readingService = _readingService ?? throw new InvalidOperationException("Feed tags are unavailable.");
        var subscriptions = await readingService.GetSubscriptionsAsync(ActiveProfile.Id, cancellationToken);
        if (subscriptions.All(subscription => subscription.FeedId != feedId))
        {
            throw new ArgumentException("The feed is not subscribed in this profile.", nameof(feedId));
        }

        var assignments = await readingService.GetFeedTagsAsync(ActiveProfile.Id, cancellationToken);
        var existingNames = assignments
            .Where(tag => tag.FeedId == feedId)
            .Select(tag => tag.Name)
            .ToArray();
        var desiredNames = tagNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var desiredNameSet = desiredNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNameSet = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingName in existingNames.Where(name => !desiredNameSet.Contains(name)))
        {
            await readingService.RemoveFeedTagAsync(ActiveProfile, feedId, existingName, cancellationToken);
        }

        foreach (var desiredName in desiredNames.Where(name => !existingNameSet.Contains(name)))
        {
            await readingService.AddFeedTagAsync(ActiveProfile, feedId, desiredName, cancellationToken);
        }

        await LoadProfileReaderDataAsync(cancellationToken);
    }

    public event Action? ProfileSwitchRequested;

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
    public bool CanFollowSelectedCatalogFeeds => _readingService is not null && HasSelectedCatalogFeeds;
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
        _readingService is not null && GetVisibleSubscribedCatalogFeeds().Length > 0;

    public Func<int, Task<bool>>? ConfirmUnfollowAllRequested { get; set; }
    public Func<string, int, Task<bool>>? ConfirmDeleteFolderRequested { get; set; }

    public bool IsCatalogFeedPreviewAvailable => _catalogFeedPreviewService is not null;

    public Func<
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        Func<string, Task>,
        Task<string?>>? FolderSelectionRequested { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var showLoadingStatus = string.IsNullOrEmpty(StatusMessage);
        if (showLoadingStatus)
        {
            StatusMessage = "Loading your library...";
        }

        try
        {
            if (_readingService is not null && !IsCatalogMaster)
            {
                await LoadProfileReaderDataAsync(cancellationToken);
            }

            if (_catalogService is not null)
            {
                await LoadCatalogAsync(cancellationToken);
            }
        }
        finally
        {
            if (showLoadingStatus && StatusMessage == "Loading your library...")
            {
                StatusMessage = string.Empty;
            }
        }
    }

    private Task RefreshCatalogAfterManagementChangeAsync(CancellationToken cancellationToken) =>
        LoadCatalogAsync(cancellationToken, initializeCatalogManagement: false);

    private async Task LoadCatalogAsync(CancellationToken cancellationToken, bool initializeCatalogManagement = true)
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
                catalogFeed.IsSubscribed = _subscribedFeedIds.Contains(feed.Id);
                catalogFeed.PropertyChanged += OnCatalogFeedPropertyChanged;
                catalogFeedItems.Add(catalogFeed);
            }

            using (CatalogFeedListView.DeferRefresh())
            {
                _catalogFeeds.ReplaceAll(catalogFeedItems);
            }

            NotifyCatalogFeedViewChanged();

            if (initializeCatalogManagement && CatalogManagement is not null)
            {
                await CatalogManagement.InitializeAsync(cancellationToken);
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

    public void ApplyPreferences(ProfilePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _profilePreferences = preferences;
        _folderArticlesPerFeedLimit = preferences.FolderArticleLimitPerFeed;
        OnPropertyChanged(nameof(RefreshFeedsWhenOpened));
        OnPropertyChanged(nameof(AutoRefreshIntervalMinutes));
        OnPropertyChanged(nameof(IsRawFeedButtonVisible));
        SetArticleViewModeIfSelected(
            true,
            preferences.Presentation == ProfileArticlePresentation.Cards,
            preferences.Presentation == ProfileArticlePresentation.Magazine);
        if (preferences.Sort == ProfileArticleSort.Newest)
        {
            IsSortByDate = true;
        }
        else
        {
            IsSortByFolder = true;
        }

        UnreadOnly = preferences.HideReadArticles;
        ActiveRoute = GetStartPageRoute();
        UpdateSelectedLinks();
        ApplyArticleFilters();
    }

    public string ActiveRoute
    {
        get => _activeRoute;
        private set
        {
            if (SetProperty(ref _activeRoute, value))
            {
                OnPropertyChanged(nameof(WorkspaceTitle));
                OnPropertyChanged(nameof(IsSearchRoute));
                OnPropertyChanged(nameof(IsGoToRoute));
                OnPropertyChanged(nameof(IsArticleListVisible));
                OnPropertyChanged(nameof(IsCatalogBrowserVisible));
                OnPropertyChanged(nameof(IsArticleCountVisible));
                OnPropertyChanged(nameof(IsCatalogAdminVisible));
                ApplyArticleFilters();
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyArticleFilters();
            }
        }
    }

    public ArticleTopicOption? SelectedArticleTopic
    {
        get => _selectedArticleTopic;
        set
        {
            if (_updatingArticleTopicOptions)
            {
                _selectedArticleTopic = value;
                return;
            }

            if (SetProperty(ref _selectedArticleTopic, value))
            {
                OnPropertyChanged(nameof(IsArticleTopicFilterActive));
                ApplyArticleFilters();
            }
        }
    }

    public bool IsArticleTopicFilterActive => SelectedArticleTopic?.Term is not null;

    public string QuickQuery
    {
        get => _quickQuery;
        set
        {
            if (SetProperty(ref _quickQuery, value))
            {
                ApplyQuickFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool UnreadOnly
    {
        get => _unreadOnly;
        set
        {
            if (SetProperty(ref _unreadOnly, value))
            {
                ApplyArticleFilters();
            }
        }
    }

    public bool SavedOnly
    {
        get => _savedOnly;
        set
        {
            if (SetProperty(ref _savedOnly, value))
            {
                ApplyArticleFilters();
            }
        }
    }

    public bool IsCardsView
    {
        get => _isCardsView;
        set => SetArticleViewModeIfSelected(value, isCardsView: true, isMagazineView: false);
    }

    public bool IsListView
    {
        get => !_isCardsView && !_isMagazineView;
        set => SetArticleViewModeIfSelected(value, isCardsView: false, isMagazineView: false);
    }

    public bool IsMagazineView
    {
        get => _isMagazineView;
        set => SetArticleViewModeIfSelected(value, isCardsView: false, isMagazineView: true);
    }

    public bool IsSortByDate
    {
        get => _isSortByDate;
        set
        {
            if (value && SetProperty(ref _isSortByDate, true))
            {
                OnPropertyChanged(nameof(IsSortByFolder));
                ConfigureArticleListView();
                ApplyArticleFilters();
            }
        }
    }

    public bool IsSortByFolder
    {
        get => !_isSortByDate;
        set
        {
            if (value && SetProperty(ref _isSortByDate, false))
            {
                OnPropertyChanged(nameof(IsSortByDate));
                ConfigureArticleListView();
                ApplyArticleFilters();
            }
        }
    }

    private void ConfigureArticleListView()
    {
        ArticleListView = CreateArticleListView(_isSortByDate);
        OnPropertyChanged(nameof(ArticleListView));
    }

    private ICollectionView CreateArticleListView(bool sortByDate)
    {
        var articleListView = new ListCollectionView(VisibleArticles);
        using (articleListView.DeferRefresh())
        {
            if (sortByDate)
            {
                articleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.PublishedAt), ListSortDirection.Descending));
                articleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.Folder), ListSortDirection.Ascending));
            }
            else
            {
                articleListView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ArticleRowViewModel.Folder)));
                articleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.Folder), ListSortDirection.Ascending));
                articleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.PublishedAt), ListSortDirection.Descending));
            }
        }

        return articleListView;
    }

    private void SetArticleViewModeIfSelected(bool isSelected, bool isCardsView, bool isMagazineView)
    {
        if (!isSelected || (_isCardsView == isCardsView && _isMagazineView == isMagazineView))
        {
            return;
        }

        _isCardsView = isCardsView;
        _isMagazineView = isMagazineView;
        OnPropertyChanged(nameof(IsCardsView));
        OnPropertyChanged(nameof(IsListView));
        OnPropertyChanged(nameof(IsMagazineView));
    }

    public ArticleRowViewModel? SelectedArticle
    {
        get => _selectedArticle;
        private set
        {
            if (SetProperty(ref _selectedArticle, value))
            {
                OnPropertyChanged(nameof(IsArticleListVisible));
                OnPropertyChanged(nameof(IsArticleCountVisible));
                OnPropertyChanged(nameof(IsReadingViewVisible));
                OnPropertyChanged(nameof(IsRawFeedButtonVisible));
                OnPropertyChanged(nameof(HasSelectedArticleFeed));
            }
        }
    }

    public string WorkspaceTitle => ActiveRoute switch
    {
        "All" => "All",
        _ when ActiveRoute.StartsWith("folder:", StringComparison.Ordinal) => ActiveRoute["folder:".Length..],
        _ when ActiveRoute.StartsWith("tag:", StringComparison.Ordinal) => ActiveRoute["tag:".Length..],
        _ when ActiveRoute.StartsWith("feed:", StringComparison.Ordinal) =>
            FeedLinks.FirstOrDefault(link => link.Route == ActiveRoute)?.Label ?? "Feed",
        _ => ActiveRoute
    };

    public bool IsSearchRoute => ActiveRoute == "Search";
    public bool IsGoToRoute => ActiveRoute == "Go to...";
    public bool RefreshFeedsWhenOpened => _profilePreferences.RefreshFeedsWhenOpened;
    public int AutoRefreshIntervalMinutes => _profilePreferences.AutoRefreshIntervalMinutes;
    public bool IsArticleCountVisible => IsArticleListVisible;
    public bool IsArticleListVisible => SelectedArticle is null && !IsCatalogBrowserVisible && !IsCatalogAdminVisible && !IsGoToRoute;
    public bool IsReadingViewVisible => SelectedArticle is not null;
    public bool IsRawFeedButtonVisible => _profilePreferences.ShowRawFeedButton && SelectedArticle?.FeedId is not null;
    public bool HasSelectedArticleFeed => SelectedArticle?.FeedId is not null;
    public bool IsCatalogBrowserVisible => ActiveRoute == "Follow sources";
    public bool IsCatalogAdminVisible => IsCatalogMaster && ActiveRoute == "Manage catalog";
    public bool IsArticleListEmpty => VisibleArticles.Count == 0;

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
            ClearSelectedCatalogFeedsCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(CatalogFeedListItem.IsSubscribed))
        {
            OnPropertyChanged(nameof(CanUnfollowAllCatalogFeeds));
            OnPropertyChanged(nameof(VisibleCatalogFeedSelectionState));
        }
    }

    private CatalogFeedListItem[] GetVisibleSelectableCatalogFeeds() =>
        CatalogFeedListView.Cast<CatalogFeedListItem>()
            .Where(feed => feed.CanSelectForFollow)
            .ToArray();

    private CatalogFeedListItem[] GetVisibleSubscribedCatalogFeeds() =>
        CatalogFeedListView.Cast<CatalogFeedListItem>()
            .Where(feed => feed.IsSubscribed)
            .ToArray();

    private string[] GetSuggestedFolders()
    {
        var suggestions = CatalogFeeds
            .Select(item => item.CategoryName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Where(name => !FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return suggestions.Length > 0
            ? suggestions
            : new[] { "News", "Technology", "Gaming", "Culture" }
                .Where(name => !FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToArray();
    }

    private void ClearSelectedCatalogFeeds()
    {
        foreach (var feed in CatalogFeeds.Where(feed => feed.IsSelectedForFollow))
        {
            feed.IsSelectedForFollow = false;
        }
    }

    private void ToggleVisibleCatalogFeedSelection()
    {
        var selectableFeeds = GetVisibleSelectableCatalogFeeds();
        var shouldSelect = VisibleCatalogFeedSelectionState != true;
        foreach (var feed in selectableFeeds)
        {
            feed.IsSelectedForFollow = shouldSelect;
        }
    }

    private async Task FollowSelectedCatalogFeedsAsync()
    {
        var selectedFeeds = CatalogFeeds
            .Where(feed => feed.IsSelectedForFollow && !feed.IsSubscribed)
            .ToArray();
        if (_readingService is null || selectedFeeds.Length == 0 || FolderSelectionRequested is null)
        {
            return;
        }

        try
        {
            var folderName = await FolderSelectionRequested(
                FolderNames.ToArray(),
                GetSuggestedFolders(),
                CreateFolderFromPickerAsync);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            var failedFeedNames = new List<string>();
            var followedCount = 0;
            foreach (var feed in selectedFeeds)
            {
                try
                {
                    await _readingService.SubscribeAsync(ActiveProfile, feed.Id, folderName);
                    _pendingInitialRefreshFeedIds.Add(feed.Id);
                    feed.IsSelectedForFollow = false;
                    followedCount++;
                }
                catch
                {
                    failedFeedNames.Add(feed.Name);
                }
            }

            if (followedCount > 0)
            {
                await LoadProfileReaderDataAsync(CancellationToken.None);
            }

            StatusMessage = failedFeedNames.Count == 0
                ? $"Following {followedCount} feeds in {folderName}."
                : $"Followed {followedCount} feeds; {failedFeedNames.Count} failed: {string.Join(", ", failedFeedNames)}.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task UnfollowAllCatalogFeedsAsync()
    {
        if (_readingService is null || ConfirmUnfollowAllRequested is null)
        {
            return;
        }

        try
        {
            var visibleFeeds = GetVisibleSubscribedCatalogFeeds();
            if (visibleFeeds.Length == 0 || !await ConfirmUnfollowAllRequested(visibleFeeds.Length))
            {
                return;
            }

            var failedFeedNames = new List<string>();
            var unfollowedCount = 0;
            foreach (var feed in visibleFeeds)
            {
                try
                {
                    await _readingService.UnsubscribeAsync(ActiveProfile, feed.Id);
                    _pendingInitialRefreshFeedIds.Remove(feed.Id);
                    unfollowedCount++;
                }
                catch
                {
                    failedFeedNames.Add(feed.Name);
                }
            }

            if (unfollowedCount > 0)
            {
                await LoadProfileReaderDataAsync(CancellationToken.None);
            }

            var feedLabel = unfollowedCount == 1 ? "feed" : "feeds";
            StatusMessage = failedFeedNames.Count == 0
                ? $"Unfollowed {unfollowedCount} {feedLabel}."
                : $"Unfollowed {unfollowedCount} {feedLabel}; {failedFeedNames.Count} failed: {string.Join(", ", failedFeedNames)}.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task DeleteFolderAsync(SidebarLink folderLink)
    {
        if (_readingService is null || ConfirmDeleteFolderRequested is null || !folderLink.IsFolder)
        {
            return;
        }

        try
        {
            var folderSubscriptions = (await _readingService.GetSubscriptionsAsync(ActiveProfile.Id))
                .Where(subscription => string.Equals(
                    subscription.FolderName,
                    folderLink.Label,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (!await ConfirmDeleteFolderRequested(folderLink.Label, folderSubscriptions.Length))
            {
                return;
            }

            await _readingService.DeleteFolderAsync(ActiveProfile, folderLink.Label);
            foreach (var subscription in folderSubscriptions)
            {
                _pendingInitialRefreshFeedIds.Remove(subscription.FeedId);
            }

            await LoadProfileReaderDataAsync(CancellationToken.None);
            if (ActiveRoute == folderLink.Route)
            {
                ActiveRoute = "All";
            }

            UpdateSelectedLinks();
            var feedLabel = folderSubscriptions.Length == 1 ? "feed" : "feeds";
            StatusMessage = $"Deleted folder {folderLink.Label} and unfollowed {folderSubscriptions.Length} {feedLabel}.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RefreshCatalogFeedView()
    {
        CatalogFeedListView.Refresh();
        NotifyCatalogFeedViewChanged();
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

    private void ClearCatalogFilters()
    {
        CatalogSearchQuery = string.Empty;
        SelectedCatalogCategory = CatalogCategoryOptions.FirstOrDefault(option => option.CategoryId is null);
        SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(option => option.CollectionId is null);
        HideFollowedCatalogFeeds = false;
    }

    public async Task<CatalogFeedPreview> LoadCatalogFeedPreviewAsync(
        CatalogFeedListItem feed,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (_catalogFeedPreviewService is null)
        {
            throw new InvalidOperationException("Feed preview is unavailable.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!forceRefresh && _catalogFeedPreviewCache.TryGetValue(feed.Id, out var cachedPreview))
        {
            return cachedPreview;
        }

        var catalogFeed = new CatalogFeed(
            feed.Id,
            feed.Name,
            feed.FeedUrl,
            feed.Description,
            feed.CategoryId,
            feed.WebsiteUrl);
        var items = await _catalogFeedPreviewService.GetPreviewItemsAsync(catalogFeed, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var preview = new CatalogFeedPreview(feed.Name, feed.CategoryName, feed.Description, items);
        CacheCatalogFeedPreview(feed.Id, preview);
        return preview;
    }

    private void CacheCatalogFeedPreview(string feedId, CatalogFeedPreview preview)
    {
        if (!_catalogFeedPreviewCache.ContainsKey(feedId))
        {
            if (_catalogFeedPreviewCache.Count >= MaximumCachedCatalogFeedPreviews)
            {
                var oldestFeedId = _catalogFeedPreviewCacheOrder.Dequeue();
                _catalogFeedPreviewCache.Remove(oldestFeedId);
            }

            _catalogFeedPreviewCacheOrder.Enqueue(feedId);
        }

        _catalogFeedPreviewCache[feedId] = preview;
    }

    private void NavigateTo(SidebarLink link)
    {
        SelectedArticle = null;
        ActiveRoute = link.Route;
        UpdateSelectedLinks();
        _ = RefreshRouteFeedsAsync(link.Route);
    }

    private void ActivateSidebarLink(SidebarLink link)
    {
        if (link.IsFolder)
        {
            link.IsExpanded = !link.IsExpanded;
            if (link.IsExpanded)
            {
                _expandedSidebarFolders.Add(link.Label);
            }
            else
            {
                _expandedSidebarFolders.Remove(link.Label);
            }
        }

        NavigateTo(link);
    }

    private void OpenArticle(ArticleRowViewModel article)
    {
        article.IsRead = true;
        SelectedArticle = article;
    }

    private void NavigateToSelectedArticleFeed()
    {
        if (SelectedArticle?.FeedId is not { } feedId)
        {
            return;
        }

        var feedLink = FeedLinks.FirstOrDefault(link =>
            string.Equals(link.Route, $"feed:{feedId}", StringComparison.Ordinal));
        if (feedLink is not null)
        {
            NavigateTo(feedLink);
        }
    }

    private void ToggleSaved()
    {
        if (SelectedArticle is not null)
        {
            SelectedArticle.IsSaved = !SelectedArticle.IsSaved;
            ApplyArticleFilters();
        }
    }

    private void ToggleRead()
    {
        if (SelectedArticle is not null)
        {
            SelectedArticle.IsRead = !SelectedArticle.IsRead;
            ApplyArticleFilters();
        }
    }

    private async Task LoadProfileReaderDataAsync(CancellationToken cancellationToken)
    {
        var readingService = _readingService ?? throw new InvalidOperationException("Reader data is unavailable.");
        var selectedArticleId = SelectedArticle?.ArticleId;
        var folders = await readingService.GetFoldersAsync(ActiveProfile.Id, cancellationToken);
        FolderNames.Clear();
        foreach (var folder in folders)
        {
            FolderNames.Add(folder);
        }

        UpdateFolderNavigation(folders, []);
        if (SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext)
        {
            await Task.Yield();
        }

        var subscriptions = await readingService.GetSubscriptionsAsync(ActiveProfile.Id, cancellationToken);
        UpdateFolderNavigation(folders, subscriptions);
        var tags = await readingService.GetFeedTagsAsync(ActiveProfile.Id, cancellationToken);
        var articles = await readingService.GetArticlesAsync(ActiveProfile.Id, new ArticleFilter(), cancellationToken);
        var subscriptionsByFeed = subscriptions.ToDictionary(item => item.FeedId, StringComparer.Ordinal);
        var tagsByFeed = tags.GroupBy(item => item.FeedId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Name).ToArray(), StringComparer.Ordinal);
        _subscribedFeedIds.Clear();
        foreach (var subscription in subscriptions)
        {
            _subscribedFeedIds.Add(subscription.FeedId);
        }

        if (!_hasAppliedStartPage)
        {
            _hasAppliedStartPage = true;
            ActiveRoute = GetStartPageRoute();
        }

        TagLinks.Clear();
        foreach (var tagGroup in tags
                     .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var tagName = tagGroup.First().Name;
            var feedCount = tagGroup.Select(item => item.FeedId).Distinct(StringComparer.Ordinal).Count();
            TagLinks.Add(new SidebarLink($"tag:{tagName}", tagName, "\uE8D2", $"({feedCount})"));
        }

        if (ActiveRoute.StartsWith("tag:", StringComparison.Ordinal) &&
            !TagLinks.Any(link => string.Equals(
                link.Label,
                ActiveRoute["tag:".Length..],
                StringComparison.OrdinalIgnoreCase)))
        {
            ActiveRoute = "All";
        }

        foreach (var feed in CatalogFeeds)
        {
            feed.IsSubscribed = subscriptionsByFeed.ContainsKey(feed.Id);
        }

        RefreshCatalogFeedView();
        OnPropertyChanged(nameof(CanUnfollowAllCatalogFeeds));

        _allArticles.Clear();
        foreach (var item in articles)
        {
            var row = new ArticleRowViewModel(
                item.Article.Title,
                item.Source,
                item.Article.PublishedAt ?? DateTimeOffset.Now,
                item.FolderName,
                tagsByFeed.GetValueOrDefault(item.Article.FeedId, []),
                item.Article.Summary ?? string.Empty,
                item.IsRead,
                item.IsSaved,
                item.Article.Id,
                item.Article.FeedId,
                item.Article.Link,
                item.Article.Content,
                item.Article.ImageUrl,
                item.Article.ExternalId,
                subscriptionsByFeed.GetValueOrDefault(item.Article.FeedId)?.FeedUrl,
                item.Article.Categories,
                subscriptionsByFeed.GetValueOrDefault(item.Article.FeedId)?.WebsiteUrl,
                item.Article.Author);
            row.PropertyChanged += OnArticlePropertyChanged;
            _allArticles.Add(row);
        }

        if (selectedArticleId is not null)
        {
            SelectedArticle = _allArticles.FirstOrDefault(article => article.ArticleId == selectedArticleId);
        }

        if (!_hasLoadedInitialReaderData)
        {
            foreach (var subscription in subscriptions.Where(subscription =>
                         !_allArticles.Any(article => article.FeedId == subscription.FeedId)))
            {
                _pendingInitialRefreshFeedIds.Add(subscription.FeedId);
            }

            _hasLoadedInitialReaderData = true;
        }

        UpdateSelectedLinks();
        ApplyQuickFilter();
        ApplyArticleFilters();
    }

    private async Task RefreshAsync()
    {
        if (_feedRefreshService is null)
        {
            return;
        }

        await RefreshUsingAsync(
            () => _feedRefreshService.RefreshProfileAsync(ActiveProfile.Id),
            refreshesAllFeeds: true);
    }

    public Task RefreshNowAsync() => RefreshAsync();

    private async Task RefreshRouteFeedsAsync(string route)
    {
        if (_feedRefreshService is null || IsCatalogMaster || IsRefreshing)
        {
            return;
        }

        string[] feedIds;
        if (route.StartsWith("feed:", StringComparison.Ordinal))
        {
            feedIds = [route["feed:".Length..]];
        }
        else if (route.StartsWith("folder:", StringComparison.Ordinal) &&
                 _feedIdsByFolder.TryGetValue(route["folder:".Length..], out var folderFeedIds))
        {
            feedIds = folderFeedIds;
        }
        else
        {
            return;
        }

        var pendingFeedIds = feedIds.Where(_pendingInitialRefreshFeedIds.Contains).ToArray();
        var feedIdsToRefresh = _profilePreferences.RefreshFeedsWhenOpened
            ? feedIds
            : pendingFeedIds;
        if (feedIdsToRefresh.Length == 0)
        {
            return;
        }

        foreach (var feedId in pendingFeedIds)
        {
            _pendingInitialRefreshFeedIds.Remove(feedId);
        }

        await RefreshUsingAsync(() => _feedRefreshService.RefreshFeedsAsync(ActiveProfile.Id, feedIdsToRefresh));
    }

    private async Task RefreshUsingAsync(
        Func<Task<FeedRefreshSummary>> refresh,
        bool refreshesAllFeeds = false)
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "Refreshing feeds...";
        try
        {
            var summary = await refresh();
            if (refreshesAllFeeds)
            {
                _pendingInitialRefreshFeedIds.Clear();
            }

            if (summary.ArticlesAdded > 0)
            {
                await LoadProfileReaderDataAsync(CancellationToken.None);
            }

            StatusMessage = summary.Failures.Count == 0
                ? $"Checked {summary.FeedsChecked} feeds; fetched {summary.ArticlesFetched} articles; New {summary.ArticlesAdded}."
                : $"Fetched {summary.ArticlesFetched} articles; New {summary.ArticlesAdded}; {summary.Failures.Count} feed(s) failed. {string.Join(" ", summary.Failures)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Refresh failed: {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private async Task ToggleSubscriptionAsync(CatalogFeedListItem feed)
    {
        if (_readingService is null)
        {
            return;
        }

        try
        {
            if (feed.IsSubscribed)
            {
                await _readingService.UnsubscribeAsync(ActiveProfile, feed.Id);
                _pendingInitialRefreshFeedIds.Remove(feed.Id);
            }
            else
            {
                if (FolderSelectionRequested is null)
                {
                    return;
                }

                var folderName = await FolderSelectionRequested(
                    FolderNames.ToArray(),
                    GetSuggestedFolders(),
                    CreateFolderFromPickerAsync);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    return;
                }

                await _readingService.SubscribeAsync(ActiveProfile, feed.Id, folderName);
                _pendingInitialRefreshFeedIds.Add(feed.Id);
            }

            await LoadProfileReaderDataAsync(CancellationToken.None);
            StatusMessage = feed.IsSubscribed ? $"Following {feed.Name}." : $"Unfollowed {feed.Name}.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task CreateFolderFromPickerAsync(string folderName)
    {
        if (_readingService is null)
        {
            throw new InvalidOperationException("Folder creation is unavailable.");
        }

        var folders = await _readingService.GetFoldersAsync(ActiveProfile.Id);
        if (!folders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await _readingService.AddFolderAsync(ActiveProfile, folderName);
                folders = await _readingService.GetFoldersAsync(ActiveProfile.Id);
            }
            catch
            {
                folders = await _readingService.GetFoldersAsync(ActiveProfile.Id);
                if (!folders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                {
                    throw;
                }
            }
        }

        FolderNames.Clear();
        foreach (var folder in folders)
        {
            FolderNames.Add(folder);
        }

        var subscriptions = await _readingService.GetSubscriptionsAsync(ActiveProfile.Id);
        UpdateFolderNavigation(folders, subscriptions);
    }

    private void UpdateFolderNavigation(
        IReadOnlyList<string> folders,
        IReadOnlyList<ProfileSubscription> subscriptions)
    {
        _feedIdsByFolder = folders.ToDictionary(
            folder => folder,
            folder => subscriptions
                .Where(subscription => string.Equals(
                    subscription.FolderName,
                    folder,
                    StringComparison.OrdinalIgnoreCase))
                .Select(subscription => subscription.FeedId)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);

        var feedLinks = new List<SidebarLink> { new("All", "All", "\uE8A5") };
        _expandedSidebarFolders.IntersectWith(folders);
        foreach (var folder in folders)
        {
            var folderSubscriptions = subscriptions.Where(item =>
                    string.Equals(item.FolderName, folder, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var folderLink = new SidebarLink(
                $"folder:{folder}",
                folder,
                string.Empty,
                $"({folderSubscriptions.Length})",
                indentLevel: 1,
                parentFolder: null)
            {
                IsExpanded = _expandedSidebarFolders.Contains(folder)
            };
            feedLinks.Add(folderLink);
            foreach (var subscription in folderSubscriptions)
            {
                feedLinks.Add(new SidebarLink(
                    $"feed:{subscription.FeedId}",
                    subscription.FeedName,
                    "\uE774",
                    indentLevel: 2,
                    parentFolder: folderLink));
            }
        }

        _feedLinks.ReplaceAll(feedLinks);
    }

    private async void OnArticlePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ArticleRowViewModel article || article.ArticleId is null || _readingService is null)
        {
            return;
        }

        try
        {
            if (e.PropertyName == nameof(ArticleRowViewModel.IsRead))
            {
                await _readingService.MarkReadAsync(ActiveProfile, article.ArticleId, article.IsRead);
            }
            else if (e.PropertyName == nameof(ArticleRowViewModel.IsSaved))
            {
                await _readingService.SetSavedAsync(ActiveProfile, article.ArticleId, article.IsSaved);
            }
            else
            {
                return;
            }

            ApplyArticleFilters();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ToggleSidebar()
    {
        _isSidebarPinned = !_isSidebarPinned;
        OnPropertyChanged(nameof(IsSidebarPinned));
        OnPropertyChanged(nameof(SidebarColumnWidth));
        OnPropertyChanged(nameof(SidebarColumnSpan));
        OnPropertyChanged(nameof(SidebarHeaderMargin));
    }

    private void UpdateSelectedLinks()
    {
        foreach (var link in PrimaryLinks.Concat(ReadingLinks).Concat(FeedLinks).Concat(TagLinks).Concat(AdminLinks))
        {
            link.IsSelected = link.Route == ActiveRoute;
        }
    }

    private void ApplyArticleFilters()
    {
        IEnumerable<ArticleRowViewModel> articles = _allArticles;
        if (ActiveRoute == "Read later")
        {
            articles = articles.Where(article => article.IsSaved);
        }
        else if (ActiveRoute == "Recently read")
        {
            articles = articles.Where(article => article.IsRead);
        }
        else if (ActiveRoute.StartsWith("folder:", StringComparison.Ordinal))
        {
            var folder = ActiveRoute["folder:".Length..];
            articles = articles.Where(article => string.Equals(article.Folder, folder, StringComparison.OrdinalIgnoreCase));
        }
        else if (ActiveRoute.StartsWith("tag:", StringComparison.Ordinal))
        {
            var tag = ActiveRoute["tag:".Length..];
            articles = articles.Where(article => article.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }
        else if (ActiveRoute.StartsWith("feed:", StringComparison.Ordinal))
        {
            var feedId = ActiveRoute["feed:".Length..];
            articles = articles.Where(article => article.FeedId == feedId);
        }

        if (ActiveRoute == "Search")
        {
            var query = SearchQuery.Trim();
            articles = string.IsNullOrEmpty(query)
                ? []
                : articles.Where(article =>
                    article.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    article.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    article.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (article.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    article.Topics.Any(topic =>
                        topic.Term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (topic.Label?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        if (UnreadOnly)
        {
            articles = articles.Where(article => !article.IsRead);
        }
        if (SavedOnly)
        {
            articles = articles.Where(article => article.IsSaved);
        }

        var topicScope = articles.ToArray();
        UpdateArticleTopicOptions(topicScope);
        if (SelectedArticleTopic is { Term: { } topicTerm } selectedTopic)
        {
            articles = topicScope.Where(article => article.Topics.Any(topic =>
                string.Equals(topic.Term, topicTerm, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(topic.Scheme, selectedTopic.Scheme, StringComparison.OrdinalIgnoreCase)));
        }

        if (IsSortByFolder && ActiveRoute.StartsWith("folder:", StringComparison.Ordinal))
        {
            articles = articles
                .GroupBy(article => article.FeedId, StringComparer.Ordinal)
                .SelectMany(feedArticles => feedArticles
                    .OrderByDescending(article => article.PublishedAt)
                    .Take(_folderArticlesPerFeedLimit));
        }

        _visibleArticles.ReplaceAll(articles);

        OnPropertyChanged(nameof(IsArticleListEmpty));
    }

    private void UpdateArticleTopicOptions(IReadOnlyList<ArticleRowViewModel> articles)
    {
        var previousSelection = _selectedArticleTopic;
        var topicCounts = new Dictionary<string, (string Term, string? Scheme, string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var article in articles)
        {
            var seenOnArticle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var topic in article.Topics)
            {
                var term = topic.Term.Trim();
                if (term.Length == 0)
                {
                    continue;
                }

                var scheme = string.IsNullOrWhiteSpace(topic.Scheme) ? null : topic.Scheme.Trim();
                var identity = $"{term}\0{scheme}";
                if (!seenOnArticle.Add(identity))
                {
                    continue;
                }

                if (topicCounts.TryGetValue(identity, out var existing))
                {
                    topicCounts[identity] = (existing.Term, existing.Scheme, existing.Name, existing.Count + 1);
                }
                else
                {
                    var name = string.IsNullOrWhiteSpace(topic.Label) ? term : topic.Label.Trim();
                    topicCounts.Add(identity, (term, scheme, name, 1));
                }
            }
        }

        var options = topicCounts.Values
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(topic => topic.Term, StringComparer.OrdinalIgnoreCase)
            .Select(topic => new ArticleTopicOption(topic.Term, topic.Scheme, topic.Name, topic.Count))
            .ToList();
        var allTopicsOption = new ArticleTopicOption(null, null, "All topics", articles.Count);
        options.Insert(0, allTopicsOption);
        _updatingArticleTopicOptions = true;
        try
        {
            ArticleTopicOptions.Clear();
            foreach (var option in options)
            {
                ArticleTopicOptions.Add(option);
            }

            var selectedOption = previousSelection?.Term is null
                ? allTopicsOption
                : options.FirstOrDefault(option =>
                    string.Equals(option.Term, previousSelection.Term, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(option.Scheme, previousSelection.Scheme, StringComparison.OrdinalIgnoreCase))
                  ?? allTopicsOption;
            _selectedArticleTopic = selectedOption;
            OnPropertyChanged(nameof(SelectedArticleTopic));
            OnPropertyChanged(nameof(IsArticleTopicFilterActive));
        }
        finally
        {
            _updatingArticleTopicOptions = false;
        }
    }

    private void ClearArticleTopicFilter() =>
        SelectedArticleTopic = ArticleTopicOptions.FirstOrDefault(option => option.Term is null);

    private string GetStartPageRoute() => _profilePreferences.StartPage switch
    {
        ProfileStartPage.All => "All",
        ProfileStartPage.FirstFolder => FeedLinks.FirstOrDefault(link =>
            link.Route.StartsWith("folder:", StringComparison.Ordinal))?.Route ?? "Today",
        _ => "Today"
    };

    private void ApplyQuickFilter()
    {
        var query = QuickQuery.Trim();
        var routes = PrimaryLinks
            .Concat(ReadingLinks)
            .Concat(FeedLinks)
            .Concat(TagLinks)
            .Concat(AdminLinks)
            .Where(link => link.Route is not "Search" and not "Go to...");
        if (!string.IsNullOrEmpty(query))
        {
            routes = routes.Where(link => link.Label.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        QuickTargets.Clear();
        foreach (var route in routes)
        {
            QuickTargets.Add(route);
        }
    }

    private sealed class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}