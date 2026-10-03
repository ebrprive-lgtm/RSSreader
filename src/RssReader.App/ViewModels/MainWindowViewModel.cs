using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed record CatalogCategoryOption(string? CategoryId, string Name);
public sealed record CatalogCollectionOption(string? CollectionId, string Name, IReadOnlySet<string> FeedIds);

public sealed class MainWindowViewModel : ObservableObject
{
    private const int MaximumCachedCatalogFeedPreviews = 20;
    private readonly List<ArticleRowViewModel> _allArticles;
    private readonly CatalogService? _catalogService;
    private readonly CatalogFeedPreviewService? _catalogFeedPreviewService;
    private readonly ReadingService? _readingService;
    private readonly FeedRefreshService? _feedRefreshService;
    private readonly Dictionary<string, CatalogFeedPreview> _catalogFeedPreviewCache = new(StringComparer.Ordinal);
    private readonly Queue<string> _catalogFeedPreviewCacheOrder = new();
    private readonly HashSet<string> _pendingInitialRefreshFeedIds = new(StringComparer.Ordinal);
    private ProfilePreferences _profilePreferences;
    private Dictionary<string, string[]> _feedIdsByFolder = new(StringComparer.OrdinalIgnoreCase);
    private string _activeRoute;
    private string _searchQuery = string.Empty;
    private string _catalogSearchQuery = string.Empty;
    private string _quickQuery = string.Empty;
    private string _statusMessage = string.Empty;
    private string _catalogFeedPreviewErrorMessage = string.Empty;
    private string _catalogLoadErrorMessage = string.Empty;
    private CatalogCategoryOption? _selectedCatalogCategory;
    private CatalogCollectionOption? _selectedCatalogCollection;
    private CatalogFeedListItem? _previewingCatalogFeed;
    private CatalogFeedPreview? _activeCatalogFeedPreview;
    private CancellationTokenSource? _catalogFeedPreviewCancellation;
    private ArticleRowViewModel? _selectedArticle;
    private bool _isSidebarPinned = true;
    private bool _isRefreshing;
    private bool _unreadOnly;
    private bool _savedOnly;
    private bool _isCardsView = true;
    private bool _isMagazineView;
    private bool _isSortByDate;
    private bool _hideFollowedCatalogFeeds;
    private bool _isCatalogFeedPreviewLoading;
    private bool _isCatalogLoading;
    private bool _isCatalogLoaded;
    private bool _hasAppliedStartPage;
    private bool _hasLoadedInitialReaderData;
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
        FeedLinks = readingService is null
            ?
            [
                new("All", "All", "\uE8A5", "5"),
                new("folder:Gaming", "Gaming", "\uE8B7", "2", indentLevel: 1),
                new("folder:tech", "tech", "\uE8B7", "3", indentLevel: 1)
            ]
            : [new("All", "All", "\uE8A5")];
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
        CatalogFeeds = [];
        CatalogCategories = [];
        CatalogCategoryOptions = [];
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
            ? new CatalogManagementViewModel(profile, catalogService)
            : null;
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
        VisibleArticles = [];
        ArticleListView = new ListCollectionView(VisibleArticles);
        ConfigureArticleListView();

        NavigateCommand = new RelayCommand<SidebarLink>(NavigateTo);
        SelectArticleCommand = new RelayCommand<ArticleRowViewModel>(OpenArticle);
        BackToListCommand = new RelayCommand(() => SelectedArticle = null);
        ToggleSavedCommand = new RelayCommand(ToggleSaved);
        ToggleReadCommand = new RelayCommand(ToggleRead);
        SwitchProfileCommand = new RelayCommand(() => ProfileSwitchRequested?.Invoke());
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ClearCatalogFiltersCommand = new RelayCommand(ClearCatalogFilters);
        RetryCatalogLoadCommand = new AsyncCommand(() => LoadCatalogAsync(CancellationToken.None), () => !IsCatalogLoading);
        FollowSelectedCatalogFeedsCommand = new AsyncCommand(FollowSelectedCatalogFeedsAsync);
        ClearSelectedCatalogFeedsCommand = new RelayCommand(ClearSelectedCatalogFeeds, () => HasSelectedCatalogFeeds);
        PreviewCatalogFeedCommand = new RelayCommand<CatalogFeedListItem>(feed => _ = PreviewCatalogFeedAsync(feed));
        RefreshCatalogFeedPreviewCommand = new RelayCommand(
            () =>
            {
                if (PreviewingCatalogFeed is { } feed)
                {
                    _ = PreviewCatalogFeedAsync(feed, forceRefresh: true);
                }
            },
            () => PreviewingCatalogFeed is not null && !IsCatalogFeedPreviewLoading);
        CloseCatalogFeedPreviewCommand = new RelayCommand(CloseCatalogFeedPreview);
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
    public ICollectionView ArticleListView { get; }
    public ObservableCollection<SidebarLink> QuickTargets { get; }
    public ObservableCollection<CatalogFeedListItem> CatalogFeeds { get; }
    public ObservableCollection<CatalogCategory> CatalogCategories { get; }
    public ObservableCollection<CatalogCategoryOption> CatalogCategoryOptions { get; }
    public ObservableCollection<CatalogCollectionOption> CatalogCollectionOptions { get; }
    public ICollectionView CatalogFeedListView { get; }
    public ObservableCollection<CatalogCollection> CatalogCollections { get; }
    public ObservableCollection<string> FolderNames { get; }
    public CatalogManagementViewModel? CatalogManagement { get; }

    public RelayCommand<SidebarLink> NavigateCommand { get; }
    public RelayCommand<ArticleRowViewModel> SelectArticleCommand { get; }
    public RelayCommand BackToListCommand { get; }
    public RelayCommand ToggleSavedCommand { get; }
    public RelayCommand ToggleReadCommand { get; }
    public RelayCommand SwitchProfileCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand ClearCatalogFiltersCommand { get; }
    public AsyncCommand RetryCatalogLoadCommand { get; }
    public AsyncCommand FollowSelectedCatalogFeedsCommand { get; }
    public RelayCommand ClearSelectedCatalogFeedsCommand { get; }
    public RelayCommand<CatalogFeedListItem> PreviewCatalogFeedCommand { get; }
    public RelayCommand RefreshCatalogFeedPreviewCommand { get; }
    public RelayCommand CloseCatalogFeedPreviewCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public AsyncCommand<CatalogFeedListItem> ToggleSubscriptionCommand { get; }

    public Task<string> GetRawFeedContentAsync(string feedId, CancellationToken cancellationToken = default) =>
        _feedRefreshService is null
            ? throw new InvalidOperationException("Raw feed content is unavailable.")
            : _feedRefreshService.GetRawFeedContentAsync(ActiveProfile.Id, feedId, cancellationToken);

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

    public CatalogFeedListItem? PreviewingCatalogFeed
    {
        get => _previewingCatalogFeed;
        private set
        {
            if (SetProperty(ref _previewingCatalogFeed, value))
            {
                OnPropertyChanged(nameof(IsCatalogFeedPreviewVisible));
                OnPropertyChanged(nameof(CatalogFeedPreviewCloseLabel));
                RefreshCatalogFeedPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public CatalogFeedPreview? ActiveCatalogFeedPreview
    {
        get => _activeCatalogFeedPreview;
        private set
        {
            if (SetProperty(ref _activeCatalogFeedPreview, value))
            {
                OnPropertyChanged(nameof(IsCatalogFeedPreviewEmpty));
            }
        }
    }

    public string CatalogFeedPreviewErrorMessage
    {
        get => _catalogFeedPreviewErrorMessage;
        private set
        {
            if (SetProperty(ref _catalogFeedPreviewErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasCatalogFeedPreviewError));
                OnPropertyChanged(nameof(IsCatalogFeedPreviewEmpty));
            }
        }
    }

    public bool IsCatalogFeedPreviewLoading
    {
        get => _isCatalogFeedPreviewLoading;
        private set
        {
            if (SetProperty(ref _isCatalogFeedPreviewLoading, value))
            {
                OnPropertyChanged(nameof(CatalogFeedPreviewCloseLabel));
                OnPropertyChanged(nameof(IsCatalogFeedPreviewEmpty));
                RefreshCatalogFeedPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsCatalogFeedPreviewAvailable => _catalogFeedPreviewService is not null;
    public bool IsCatalogFeedPreviewVisible => PreviewingCatalogFeed is not null;
    public bool HasCatalogFeedPreviewError => !string.IsNullOrWhiteSpace(CatalogFeedPreviewErrorMessage);
    public bool IsCatalogFeedPreviewEmpty =>
        IsCatalogFeedPreviewVisible &&
        !IsCatalogFeedPreviewLoading &&
        !HasCatalogFeedPreviewError &&
        (ActiveCatalogFeedPreview is null || ActiveCatalogFeedPreview.Items.Count == 0);
    public string CatalogFeedPreviewCloseLabel => IsCatalogFeedPreviewLoading ? "Cancel preview" : "Close preview";

    public Func<
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        Func<string, Task>,
        Task<string?>>? FolderSelectionRequested { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogService is not null)
        {
            await LoadCatalogAsync(cancellationToken);
        }

        if (_readingService is not null && !IsCatalogMaster)
        {
            await LoadProfileReaderDataAsync(cancellationToken);
        }
    }

    private async Task LoadCatalogAsync(CancellationToken cancellationToken)
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
            var selectedCollectionId = _selectedCatalogCollection?.CollectionId;

            CatalogCategories.Clear();
            CatalogCategoryOptions.Clear();
            var allCategoriesOption = new CatalogCategoryOption(null, $"All categories ({feeds.Count})");
            CatalogCategoryOptions.Add(allCategoriesOption);
            foreach (var category in categories.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                CatalogCategories.Add(category);
                var categoryFeedCount = feeds.Count(feed => feed.CategoryId == category.Id);
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
                var collectionFeedIds = await _catalogService.GetCollectionFeedIdsAsync(collection.Id, cancellationToken);
                var feedIdSet = collectionFeedIds.ToHashSet(StringComparer.Ordinal);
                CatalogCollectionOptions.Add(new CatalogCollectionOption(
                    collection.Id,
                    $"{collection.Name} ({feedIdSet.Count})",
                    feedIdSet));
            }

            SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(option =>
                option.CollectionId == selectedCollectionId) ?? allCollectionsOption;

            CatalogFeeds.Clear();
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
                catalogFeed.PropertyChanged += OnCatalogFeedPropertyChanged;
                CatalogFeeds.Add(catalogFeed);
            }

            RefreshCatalogFeedView();

            if (CatalogManagement is not null)
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
        using (ArticleListView.DeferRefresh())
        {
            ArticleListView.GroupDescriptions.Clear();
            ArticleListView.SortDescriptions.Clear();
            if (_isSortByDate)
            {
                ArticleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.PublishedAt), ListSortDirection.Descending));
                ArticleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.Folder), ListSortDirection.Ascending));
            }
            else
            {
                ArticleListView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ArticleRowViewModel.Folder)));
                ArticleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.Folder), ListSortDirection.Ascending));
                ArticleListView.SortDescriptions.Add(new SortDescription(nameof(ArticleRowViewModel.PublishedAt), ListSortDirection.Descending));
            }
        }
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
        if (e.PropertyName != nameof(CatalogFeedListItem.IsSelectedForFollow))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCatalogFeedCount));
        OnPropertyChanged(nameof(HasSelectedCatalogFeeds));
        OnPropertyChanged(nameof(CanFollowSelectedCatalogFeeds));
        OnPropertyChanged(nameof(FollowSelectedCatalogFeedsLabel));
        ClearSelectedCatalogFeedsCommand.NotifyCanExecuteChanged();
    }

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

    private void RefreshCatalogFeedView()
    {
        CatalogFeedListView.Refresh();
        OnPropertyChanged(nameof(IsCatalogFilterActive));
        OnPropertyChanged(nameof(IsCatalogResultsEmpty));
        OnPropertyChanged(nameof(CatalogResultsSummary));
        OnPropertyChanged(nameof(CatalogResultsEmptyMessage));
    }

    private void ClearCatalogFilters()
    {
        CatalogSearchQuery = string.Empty;
        SelectedCatalogCategory = CatalogCategoryOptions.FirstOrDefault(option => option.CategoryId is null);
        SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(option => option.CollectionId is null);
        HideFollowedCatalogFeeds = false;
    }

    public async Task PreviewCatalogFeedAsync(CatalogFeedListItem feed, bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (_catalogFeedPreviewService is null)
        {
            return;
        }

        var previousCancellation = _catalogFeedPreviewCancellation;
        var cancellation = new CancellationTokenSource();
        _catalogFeedPreviewCancellation = cancellation;
        previousCancellation?.Cancel();
        PreviewingCatalogFeed = feed;
        ActiveCatalogFeedPreview = null;
        CatalogFeedPreviewErrorMessage = string.Empty;
        IsCatalogFeedPreviewLoading = true;

        try
        {
            CatalogFeedPreview preview;
            if (!forceRefresh && _catalogFeedPreviewCache.TryGetValue(feed.Id, out var cachedPreview))
            {
                preview = cachedPreview;
            }
            else
            {
                var catalogFeed = new CatalogFeed(
                    feed.Id,
                    feed.Name,
                    feed.FeedUrl,
                    feed.Description,
                    feed.CategoryId);
                var items = await _catalogFeedPreviewService.GetPreviewItemsAsync(catalogFeed, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                preview = new CatalogFeedPreview(feed.Name, feed.CategoryName, feed.Description, items);
                CacheCatalogFeedPreview(feed.Id, preview);
            }

            if (ReferenceEquals(_catalogFeedPreviewCancellation, cancellation))
            {
                ActiveCatalogFeedPreview = preview;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_catalogFeedPreviewCancellation, cancellation))
            {
                CatalogFeedPreviewErrorMessage = $"Could not load this feed preview: {exception.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_catalogFeedPreviewCancellation, cancellation))
            {
                _catalogFeedPreviewCancellation = null;
                IsCatalogFeedPreviewLoading = false;
            }

            cancellation.Dispose();
        }
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

    private void CloseCatalogFeedPreview()
    {
        var cancellation = _catalogFeedPreviewCancellation;
        _catalogFeedPreviewCancellation = null;
        cancellation?.Cancel();
        PreviewingCatalogFeed = null;
        ActiveCatalogFeedPreview = null;
        CatalogFeedPreviewErrorMessage = string.Empty;
        IsCatalogFeedPreviewLoading = false;
    }

    private void NavigateTo(SidebarLink link)
    {
        if (link.Route != "Follow sources")
        {
            CloseCatalogFeedPreview();
        }

        SelectedArticle = null;
        ActiveRoute = link.Route;
        UpdateSelectedLinks();
        _ = RefreshRouteFeedsAsync(link.Route);
    }

    private void OpenArticle(ArticleRowViewModel article)
    {
        article.IsRead = true;
        SelectedArticle = article;
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
        var subscriptions = await _readingService!.GetSubscriptionsAsync(ActiveProfile.Id, cancellationToken);
        var folders = await _readingService.GetFoldersAsync(ActiveProfile.Id, cancellationToken);
        var tags = await _readingService.GetFeedTagsAsync(ActiveProfile.Id, cancellationToken);
        var articles = await _readingService.GetArticlesAsync(ActiveProfile.Id, new ArticleFilter(), cancellationToken);
        var subscriptionsByFeed = subscriptions.ToDictionary(item => item.FeedId, StringComparer.Ordinal);
        _feedIdsByFolder = subscriptions
            .GroupBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.FeedId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var tagsByFeed = tags.GroupBy(item => item.FeedId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Name).ToArray(), StringComparer.Ordinal);

        FolderNames.Clear();
        foreach (var folder in folders)
        {
            FolderNames.Add(folder);
        }

        FeedLinks.Clear();
        FeedLinks.Add(new SidebarLink("All", "All", "\uE8A5"));
        foreach (var folder in folders)
        {
            FeedLinks.Add(new SidebarLink($"folder:{folder}", folder, "\uE8B7", indentLevel: 1));
            foreach (var subscription in subscriptions.Where(item =>
                         string.Equals(item.FolderName, folder, StringComparison.OrdinalIgnoreCase)))
            {
                FeedLinks.Add(new SidebarLink(
                    $"feed:{subscription.FeedId}",
                    subscription.FeedName,
                    "\uE774",
                    indentLevel: 2));
            }
        }

        if (!_hasAppliedStartPage)
        {
            _hasAppliedStartPage = true;
            ActiveRoute = GetStartPageRoute();
        }

        TagLinks.Clear();
        foreach (var tagName in tags.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name))
        {
            TagLinks.Add(new SidebarLink($"tag:{tagName}", tagName, "\uE8D2"));
        }

        foreach (var feed in CatalogFeeds)
        {
            feed.IsSubscribed = subscriptionsByFeed.ContainsKey(feed.Id);
        }

        RefreshCatalogFeedView();

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
                item.Article.ImageUrl);
            row.PropertyChanged += OnArticlePropertyChanged;
            _allArticles.Add(row);
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

            await LoadProfileReaderDataAsync(CancellationToken.None);
            StatusMessage = summary.Failures.Count == 0
                ? $"Checked {summary.FeedsChecked} feeds; fetched {summary.ArticlesFetched} articles."
                : $"Fetched {summary.ArticlesFetched} articles; {summary.Failures.Count} feed(s) failed. {string.Join(" ", summary.Failures)}";
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

        await _readingService.AddFolderAsync(ActiveProfile, folderName);
        await LoadProfileReaderDataAsync(CancellationToken.None);
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
                    (article.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (UnreadOnly)
        {
            articles = articles.Where(article => !article.IsRead);
        }
        if (SavedOnly)
        {
            articles = articles.Where(article => article.IsSaved);
        }
        if (IsSortByFolder && ActiveRoute.StartsWith("folder:", StringComparison.Ordinal))
        {
            articles = articles
                .GroupBy(article => article.FeedId, StringComparer.Ordinal)
                .SelectMany(feedArticles => feedArticles
                    .OrderByDescending(article => article.PublishedAt)
                    .Take(_folderArticlesPerFeedLimit));
        }

        VisibleArticles.Clear();
        foreach (var article in articles)
        {
            VisibleArticles.Add(article);
        }

        OnPropertyChanged(nameof(IsArticleListEmpty));
    }

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
}