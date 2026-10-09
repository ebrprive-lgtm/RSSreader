using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
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
    public const double DefaultSidebarWidth = 286;
    public const double MinimumSidebarWidth = 220;
    public const double MaximumSidebarWidth = 480;
    private readonly List<ArticleRowViewModel> _allArticles;
    private readonly CatalogBrowserState _catalogBrowser;
    private readonly SidebarFeedNavigation _sidebarFeedNavigation;
    private readonly BulkObservableCollection<ArticleRowViewModel> _visibleArticles = [];
    private readonly CatalogService? _catalogService;
    private readonly CatalogFeedPreviewService? _catalogFeedPreviewService;
    private readonly ReadingService? _readingService;
    private readonly FeedRefreshService? _feedRefreshService;
    private readonly ProfileFeedService? _profileFeedService;
    private readonly ProfileService? _profileService;
    private readonly SemaphoreSlim _profilePreferencesSaveGate = new(1, 1);
    private readonly CatalogFeedPreviewCache _catalogFeedPreviewCache = new();
    private readonly ReaderNavigationHistory _navigationHistory = new();
    private readonly HashSet<string> _pendingInitialRefreshFeedIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _subscribedFeedIds = new(StringComparer.Ordinal);
    private readonly FeedRefreshStatus _feedRefreshStatus = new();
    private readonly BulkObservableCollection<FeedRefreshWarning> _feedRefreshWarnings = [];
    private ProfilePreferences _profilePreferences;
    private string _activeRoute;
    private string _searchQuery = string.Empty;
    private string _statusMessage = string.Empty;
    private ArticleTopicOption? _selectedArticleTopic;
    private ArticleRowViewModel? _selectedArticle;
    private bool _isSidebarPinned = true;
    private bool _unreadOnly;
    private bool _savedOnly;
    private bool _isCardsView = true;
    private bool _isMagazineView;
    private bool _isSortByDate;
    private double _sidebarWidth = DefaultSidebarWidth;
    private bool _hasAppliedStartPage;
    private bool _hasLoadedInitialReaderData;
    private bool _isFeedWarningsPopupOpen;
    private bool _updatingArticleTopicOptions;
    private int _articleListScrollToTopRequest;
    private int _selectedFeedWarningIndex = -1;
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
        CatalogFeedPreviewService? catalogFeedPreviewService = null,
        ProfileFeedService? profileFeedService = null,
        ProfileService? profileService = null)
    {
        ActiveProfile = profile;
        _hasAppliedStartPage = profile.IsCatalogMaster;
        _catalogService = catalogService;
        _catalogFeedPreviewService = catalogFeedPreviewService;
        _readingService = readingService;
        _feedRefreshService = feedRefreshService;
        _profileFeedService = profileFeedService;
        _profileService = profileService;
        _profilePreferences = profilePreferences ?? new ProfilePreferences();
        _catalogBrowser = new CatalogBrowserState(
            catalogService,
            readingService is not null,
            _profilePreferences.HideFollowedCatalogFeeds);
        _catalogBrowser.PropertyChanged += OnCatalogBrowserPropertyChanged;
        _sidebarFeedNavigation = new SidebarFeedNavigation(readingService is null);
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
            new("Search", "Search", "\uE721")
        ];
        ReadingLinks =
        [
            new("Read later", "Read later", "\uE734"),
            new("Recently read", "Recently read", "\uE823")
        ];
        FeedLinks = _sidebarFeedNavigation.FeedLinks;
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
        ArticleTopicOptions = [];
        FolderNames = [];
        CatalogManagement = profile.IsCatalogMaster && catalogService is not null
            ? new CatalogManagementViewModel(profile, catalogService, catalogFeedPreviewService)
            : null;
        if (CatalogManagement is not null)
        {
            CatalogManagement.CatalogRefreshRequested = RefreshCatalogAfterManagementChangeAsync;
        }
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
        ArticleListView = CreateArticleListView(sortByDate: _isSortByDate);

        NavigateCommand = new RelayCommand<SidebarLink>(link => NavigateTo(link));
        ActivateSidebarLinkCommand = new RelayCommand<SidebarLink>(ActivateSidebarLink);
        SelectArticleCommand = new RelayCommand<ArticleRowViewModel>(OpenArticle);
        SelectArticleTopicCommand = new RelayCommand<ArticleTopicChip>(
            SelectArticleTopic,
            CanSelectArticleTopic);
        SelectFeedTagCommand = new RelayCommand<ArticleFeedTagChip>(SelectFeedTag, CanSelectFeedTag);
        PreviousArticleCommand = new RelayCommand(() => NavigateToAdjacentArticle(-1), () => CanNavigateToAdjacentArticle(-1));
        NextArticleCommand = new RelayCommand(() => NavigateToAdjacentArticle(1), () => CanNavigateToAdjacentArticle(1));
        NavigateToSelectedArticleFeedCommand = new RelayCommand(NavigateToSelectedArticleFeed);
        BackCommand = new RelayCommand(GoBack, () => CanGoBack);
        ToggleSavedCommand = new RelayCommand(ToggleSaved);
        ToggleReadCommand = new RelayCommand(ToggleRead);
        SwitchProfileCommand = new RelayCommand(() => ProfileSwitchRequested?.Invoke());
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ClearCatalogFiltersCommand = new RelayCommand(ClearCatalogFilters);
        ClearSearchQueryCommand = new RelayCommand(
            () => SearchQuery = string.Empty,
            () => HasSearchQuery);
        ClearArticleTopicCommand = new RelayCommand(ClearArticleTopicFilter);
        RetryCatalogLoadCommand = new AsyncCommand(() => LoadCatalogAsync(CancellationToken.None), () => !IsCatalogLoading);
        FollowSelectedCatalogFeedsCommand = new AsyncCommand(FollowSelectedCatalogFeedsAsync);
        ToggleVisibleCatalogFeedSelectionCommand = new RelayCommand(ToggleVisibleCatalogFeedSelection);
        UnfollowAllCatalogFeedsCommand = new AsyncCommand(UnfollowAllCatalogFeedsAsync);
        UnfollowFeedCommand = new AsyncCommand<SidebarLink>(
            UnfollowFeedAsync,
            link => _readingService is not null && link.IsFeedEntry);
        DeleteFolderCommand = new AsyncCommand<SidebarLink>(
            DeleteFolderAsync,
            link => _readingService is not null && link.IsFolder);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing);
        RetryFailedFeedsCommand = new RelayCommand(
            () => _ = RetryFailedFeedsAsync(),
            () => !IsRefreshing && HasVisibleFailedRefreshFeeds);
        ToggleFeedWarningsPopupCommand = new RelayCommand(
            () => IsFeedWarningsPopupOpen = !IsFeedWarningsPopupOpen,
            () => HasFeedWarnings);
        CloseFeedWarningsPopupCommand = new RelayCommand(() => IsFeedWarningsPopupOpen = false);
        PreviousFeedWarningCommand = new RelayCommand(
            SelectPreviousFeedWarning,
            () => _selectedFeedWarningIndex > 0);
        NextFeedWarningCommand = new RelayCommand(
            SelectNextFeedWarning,
            () => _selectedFeedWarningIndex >= 0 &&
                  _selectedFeedWarningIndex < _feedRefreshWarnings.Count - 1);
        DismissCurrentFeedWarningCommand = new AsyncCommand(DismissCurrentFeedWarningAsync);
        DismissAllFeedWarningsCommand = new AsyncCommand(DismissAllFeedWarningsAsync);
        RetryCurrentFeedWarningCommand = new AsyncCommand(RetryCurrentFeedWarningAsync);
        ToggleSubscriptionCommand = new AsyncCommand<CatalogFeedListItem>(ToggleSubscriptionAsync);

        UpdateSelectedLinks();
        ApplyArticleFilters();
    }

    public Profile ActiveProfile { get; }
    public string ActiveProfileName => ActiveProfile.Name;
    public bool IsCatalogMaster => ActiveProfile.IsCatalogMaster;
    public bool IsPersonalFeedManagementVisible => _profileFeedService is not null;
    public bool IsSidebarPinned => _isSidebarPinned;
    public bool IsSidebarExpanded => true;
    public GridLength SidebarColumnWidth
    {
        get => new(IsSidebarPinned ? _sidebarWidth : 0);
        set
        {
            if (IsSidebarPinned && value.IsAbsolute)
            {
                SetSidebarPanelWidth(value.Value);
            }
        }
    }
    public double SidebarPanelWidth => _sidebarWidth;
    public double SidebarColumnMinWidth => IsSidebarPinned ? MinimumSidebarWidth : 0;
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
    public ObservableCollection<CatalogFeedListItem> CatalogFeeds => _catalogBrowser.CatalogFeeds;
    public ObservableCollection<CatalogCategory> CatalogCategories => _catalogBrowser.CatalogCategories;
    public ObservableCollection<CatalogCategoryOption> CatalogCategoryOptions => _catalogBrowser.CatalogCategoryOptions;
    public ObservableCollection<CatalogCollectionOption> CatalogCollectionOptions => _catalogBrowser.CatalogCollectionOptions;
    public ObservableCollection<ArticleTopicOption> ArticleTopicOptions { get; }
    public ICollectionView CatalogFeedListView => _catalogBrowser.CatalogFeedListView;
    public ObservableCollection<CatalogCollection> CatalogCollections => _catalogBrowser.CatalogCollections;
    public ObservableCollection<string> FolderNames { get; }
    public ObservableCollection<FeedRefreshWarning> FeedRefreshWarnings => _feedRefreshWarnings;
    public CatalogManagementViewModel? CatalogManagement { get; }

    public RelayCommand<SidebarLink> NavigateCommand { get; }
    public RelayCommand<SidebarLink> ActivateSidebarLinkCommand { get; }
    public RelayCommand<ArticleRowViewModel> SelectArticleCommand { get; }
    public RelayCommand<ArticleTopicChip> SelectArticleTopicCommand { get; }
    public RelayCommand<ArticleFeedTagChip> SelectFeedTagCommand { get; }
    public RelayCommand PreviousArticleCommand { get; }
    public RelayCommand NextArticleCommand { get; }
    public RelayCommand NavigateToSelectedArticleFeedCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand ToggleSavedCommand { get; }
    public RelayCommand ToggleReadCommand { get; }
    public RelayCommand SwitchProfileCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand ClearCatalogFiltersCommand { get; }
    public RelayCommand ClearSearchQueryCommand { get; }
    public RelayCommand ClearArticleTopicCommand { get; }
    public AsyncCommand RetryCatalogLoadCommand { get; }
    public AsyncCommand FollowSelectedCatalogFeedsCommand { get; }
    public RelayCommand ToggleVisibleCatalogFeedSelectionCommand { get; }
    public AsyncCommand UnfollowAllCatalogFeedsCommand { get; }
    public AsyncCommand<SidebarLink> UnfollowFeedCommand { get; }
    public AsyncCommand<SidebarLink> DeleteFolderCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand RetryFailedFeedsCommand { get; }
    public RelayCommand ToggleFeedWarningsPopupCommand { get; }
    public RelayCommand CloseFeedWarningsPopupCommand { get; }
    public RelayCommand PreviousFeedWarningCommand { get; }
    public RelayCommand NextFeedWarningCommand { get; }
    public AsyncCommand DismissCurrentFeedWarningCommand { get; }
    public AsyncCommand DismissAllFeedWarningsCommand { get; }
    public AsyncCommand RetryCurrentFeedWarningCommand { get; }
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
        get => _catalogBrowser.CatalogSearchQuery;
        set => _catalogBrowser.CatalogSearchQuery = value;
    }

    public CatalogCategoryOption? SelectedCatalogCategory
    {
        get => _catalogBrowser.SelectedCatalogCategory;
        set => _catalogBrowser.SelectedCatalogCategory = value;
    }

    public CatalogCollectionOption? SelectedCatalogCollection
    {
        get => _catalogBrowser.SelectedCatalogCollection;
        set => _catalogBrowser.SelectedCatalogCollection = value;
    }

    public string SelectedCatalogCollectionCuratorLabel => _catalogBrowser.SelectedCatalogCollectionCuratorLabel;

    public bool HideFollowedCatalogFeeds
    {
        get => _catalogBrowser.HideFollowedCatalogFeeds;
        set
        {
            if (_catalogBrowser.HideFollowedCatalogFeeds != value)
            {
                _catalogBrowser.HideFollowedCatalogFeeds = value;
                _profilePreferences = _profilePreferences with { HideFollowedCatalogFeeds = value };
                _ = PersistProfilePreferencesAsync();
            }
        }
    }

    public bool IsCatalogFilterActive => _catalogBrowser.IsCatalogFilterActive;
    public bool IsCatalogResultsEmpty => _catalogBrowser.IsCatalogResultsEmpty;
    public string CatalogResultsSummary => _catalogBrowser.CatalogResultsSummary;
    public string CatalogResultsEmptyMessage => _catalogBrowser.CatalogResultsEmptyMessage;
    public bool IsCatalogLoading => _catalogBrowser.IsCatalogLoading;
    public bool IsCatalogLoaded => _catalogBrowser.IsCatalogLoaded;
    public string CatalogLoadErrorMessage => _catalogBrowser.CatalogLoadErrorMessage;
    public bool HasCatalogLoadError => _catalogBrowser.HasCatalogLoadError;
    public int SelectedCatalogFeedCount => _catalogBrowser.SelectedCatalogFeedCount;
    public bool HasSelectedCatalogFeeds => _catalogBrowser.HasSelectedCatalogFeeds;
    public bool CanFollowSelectedCatalogFeeds => _catalogBrowser.CanFollowSelectedCatalogFeeds;
    public string FollowSelectedCatalogFeedsLabel => _catalogBrowser.FollowSelectedCatalogFeedsLabel;
    public bool CanSelectVisibleCatalogFeeds => _catalogBrowser.CanSelectVisibleCatalogFeeds;
    public bool? VisibleCatalogFeedSelectionState => _catalogBrowser.VisibleCatalogFeedSelectionState;
    public bool CanUnfollowAllCatalogFeeds => _catalogBrowser.CanUnfollowAllCatalogFeeds;

    public Func<int, Task<bool>>? ConfirmUnfollowAllRequested { get; set; }
    public Func<string, Task<bool>>? ConfirmUnfollowRequested { get; set; }
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
            if (_readingService is not null)
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

    public async Task<CatalogFeed> AddPersonalFeedAsync(
        string name,
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        var service = _profileFeedService ?? throw new InvalidOperationException("Personal feeds are unavailable.");
        var feed = await service.AddFeedAsync(ActiveProfile, name, feedUrl, cancellationToken);
        await LoadProfileReaderDataAsync(cancellationToken);
        StatusMessage = $"Added {feed.Name} to your feeds. Refresh to load its articles.";
        return feed;
    }

    public async Task<ProfileFeedImportSummary> ImportPersonalFeedsAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var service = _profileFeedService ?? throw new InvalidOperationException("Personal feeds are unavailable.");
        var result = await service.ImportOpmlAsync(ActiveProfile, stream, cancellationToken);
        await LoadProfileReaderDataAsync(cancellationToken);
        StatusMessage = $"Imported {result.AddedCount} feed(s); {result.DuplicateCount} duplicate(s), {result.SkippedCount} skipped.";
        return result;
    }

    public Task<string> ExportPersonalFeedsAsync(CancellationToken cancellationToken = default)
    {
        var service = _profileFeedService ?? throw new InvalidOperationException("Personal feeds are unavailable.");
        return service.ExportOpmlAsync(ActiveProfile.Id, cancellationToken);
    }

    private Task RefreshCatalogAfterManagementChangeAsync(CancellationToken cancellationToken) =>
        LoadCatalogAsync(cancellationToken, initializeCatalogManagement: false);

    private Task LoadCatalogAsync(CancellationToken cancellationToken, bool initializeCatalogManagement = true)
    {
        var catalogManagement = initializeCatalogManagement ? CatalogManagement : null;
        return _catalogBrowser.LoadAsync(
            cancellationToken,
            _subscribedFeedIds,
            catalogManagement is null ? null : catalogManagement.InitializeAsync);
    }

    public void ApplyPreferences(ProfilePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var destinationRoute = GetStartPageRoute(preferences.StartPage);
        if (SelectedArticle is null &&
            !string.Equals(ActiveRoute, destinationRoute, StringComparison.Ordinal))
        {
            RecordCurrentNavigationState();
        }

        _profilePreferences = preferences with
        {
            HideFollowedCatalogFeeds = _catalogBrowser.HideFollowedCatalogFeeds
        };
        _folderArticlesPerFeedLimit = preferences.FolderArticleLimitPerFeed;
        OnPropertyChanged(nameof(RefreshFeedsWhenOpened));
        OnPropertyChanged(nameof(AutoRefreshIntervalMinutes));
        OnPropertyChanged(nameof(IsRawFeedButtonVisible));
        OnPropertyChanged(nameof(LimitArticleWidth));
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
                OnPropertyChanged(nameof(IsArticleSearchBoxVisible));
                OnPropertyChanged(nameof(IsArticleListVisible));
                OnPropertyChanged(nameof(IsCatalogBrowserVisible));
                OnPropertyChanged(nameof(IsArticleCountVisible));
                OnPropertyChanged(nameof(IsCatalogAdminVisible));
                ApplyArticleFilters();
            }
        }
    }

    public int ArticleListScrollToTopRequest
    {
        get => _articleListScrollToTopRequest;
        private set => SetProperty(ref _articleListScrollToTopRequest, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(HasSearchQuery));
                ClearSearchQueryCommand.NotifyCanExecuteChanged();
                ApplyArticleFilters();
            }
        }
    }
    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasFeedWarnings => _feedRefreshWarnings.Count > 0;
    public int FeedWarningCount => _feedRefreshWarnings.Count;
    public FeedRefreshWarning? SelectedFeedWarning =>
        _selectedFeedWarningIndex >= 0 && _selectedFeedWarningIndex < _feedRefreshWarnings.Count
            ? _feedRefreshWarnings[_selectedFeedWarningIndex]
            : null;
    public string FeedWarningPositionLabel =>
        _feedRefreshWarnings.Count == 0
            ? "0 of 0"
            : $"{_selectedFeedWarningIndex + 1} of {_feedRefreshWarnings.Count}";
    public bool HasSelectedFeedWarningFeed => SelectedFeedWarning?.FeedId is not null;

    public bool IsFeedWarningsPopupOpen
    {
        get => _isFeedWarningsPopupOpen;
        set => SetProperty(ref _isFeedWarningsPopupOpen, value && HasFeedWarnings);
    }

    public string? RefreshFailureMessage
    {
        get => _feedRefreshStatus.FailureMessage;
        private set
        {
            if (_feedRefreshStatus.SetFailureMessage(value))
            {
                OnPropertyChanged(nameof(RefreshFailureMessage));
                OnPropertyChanged(nameof(HasRefreshFailure));
                NotifyRefreshFailureContextChanged();
                UpdateFeedWarnings();
            }
        }
    }

    public bool HasRefreshFailure => !string.IsNullOrWhiteSpace(RefreshFailureMessage);
    public bool HasFailedRefreshFeeds => _feedRefreshStatus.FailedFeedIds.Count > 0;
    public string? VisibleRefreshFailureMessage
    {
        get
        {
            var relevantFailedFeedIds = GetRelevantFailedRefreshFeedIds();
            if (relevantFailedFeedIds.Length == 0)
            {
                return _feedRefreshStatus.FailedFeedIds.Count == 0 ? RefreshFailureMessage : null;
            }

            var failures = relevantFailedFeedIds
                .Select(_feedRefreshStatus.GetFailureMessage)
                .Where(message => !string.IsNullOrWhiteSpace(message));
            return string.Join(" ", failures);
        }
    }
    public bool HasVisibleRefreshFailure => !string.IsNullOrWhiteSpace(VisibleRefreshFailureMessage);
    public bool HasVisibleFailedRefreshFeeds => GetRelevantFailedRefreshFeedIds().Length > 0;

    public bool IsRefreshing
    {
        get => _feedRefreshStatus.IsRefreshing;
        private set
        {
            if (_feedRefreshStatus.SetIsRefreshing(value))
            {
                OnPropertyChanged(nameof(IsRefreshing));
                RefreshCommand.NotifyCanExecuteChanged();
                RetryFailedFeedsCommand.NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsArticleSearchBoxVisible));
                OnPropertyChanged(nameof(IsArticleCountVisible));
                OnPropertyChanged(nameof(IsReadingViewVisible));
                SelectArticleTopicCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsRawFeedButtonVisible));
                OnPropertyChanged(nameof(HasSelectedArticleFeed));
                OnPropertyChanged(nameof(SelectedArticleRefreshStatusMessage));
                OnPropertyChanged(nameof(HasSelectedArticleRefreshState));
                NotifyRefreshFailureContextChanged();
                PreviousArticleCommand.NotifyCanExecuteChanged();
                NextArticleCommand.NotifyCanExecuteChanged();
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
    public bool RefreshFeedsWhenOpened => _profilePreferences.RefreshFeedsWhenOpened;
    public int AutoRefreshIntervalMinutes => _profilePreferences.AutoRefreshIntervalMinutes;
    public bool LimitArticleWidth => _profilePreferences.LimitArticleWidth;
    public bool IsArticleCountVisible => IsArticleListVisible;
    public bool IsArticleListVisible => SelectedArticle is null && !IsCatalogBrowserVisible && !IsCatalogAdminVisible;
    public bool IsArticleSearchBoxVisible => IsArticleListVisible && !IsSearchRoute;
    public bool IsReadingViewVisible => SelectedArticle is not null;
    public bool CanGoBack => _navigationHistory.Count > 0;
    public bool IsRawFeedButtonVisible => _profilePreferences.ShowRawFeedButton && SelectedArticle?.FeedId is not null;
    public bool HasSelectedArticleFeed => SelectedArticle?.FeedId is not null;
    public string? SelectedArticleRefreshStatusMessage
        => _feedRefreshStatus.GetSelectedArticleStatusMessage(SelectedArticle?.FeedId);

    public bool HasSelectedArticleRefreshState => SelectedArticleRefreshStatusMessage is not null;
    public bool IsCatalogBrowserVisible => ActiveRoute == "Follow sources";
    public bool IsCatalogAdminVisible => IsCatalogMaster && ActiveRoute == "Manage catalog";
    public bool IsArticleListEmpty => VisibleArticles.Count == 0;
    public string ArticleListEmptyMessage => ActiveRoute switch
    {
        "Search" when string.IsNullOrWhiteSpace(SearchQuery) => "Enter a search term to find articles.",
        "Search" => "No articles match this search.",
        "Read later" => "No saved articles.",
        "Recently read" => "No recently read articles.",
        _ when ActiveRoute.StartsWith("feed:", StringComparison.Ordinal) => "No articles in this feed.",
        _ when ActiveRoute.StartsWith("folder:", StringComparison.Ordinal) => "No articles in this folder.",
        _ when ActiveRoute.StartsWith("tag:", StringComparison.Ordinal) => "No articles with this feed tag.",
        _ => "No articles in this view."
    };

    private string[] GetSuggestedFolders(IEnumerable<string?> relatedCategoryNames)
    {
        var categoryNames = new List<string?>();
        if (SelectedCatalogCategory?.CategoryId is { } selectedCategoryId &&
            CatalogCategories.FirstOrDefault(category => category.Id == selectedCategoryId) is { } selectedCategory)
        {
            categoryNames.Add(selectedCategory.Name);
        }

        categoryNames.AddRange(relatedCategoryNames);
        categoryNames.AddRange(CatalogFeeds.Select(item => item.CategoryName));

        var suggestions = categoryNames
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

    private void ToggleVisibleCatalogFeedSelection()
    {
        var selectableFeeds = _catalogBrowser.GetVisibleSelectableCatalogFeeds();
        var shouldSelect = _catalogBrowser.VisibleCatalogFeedSelectionState != true;
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
                GetSuggestedFolders(selectedFeeds.Select(feed => feed.CategoryName)),
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
            var visibleFeeds = _catalogBrowser.GetVisibleSubscribedCatalogFeeds();
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

    private async Task UnfollowFeedAsync(SidebarLink feedLink)
    {
        if (_readingService is null ||
            ConfirmUnfollowRequested is null ||
            !feedLink.IsFeedEntry ||
            !feedLink.Route.StartsWith("feed:", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (!await ConfirmUnfollowRequested(feedLink.Label))
            {
                return;
            }

            var feedId = feedLink.Route["feed:".Length..];
            await _readingService.UnsubscribeAsync(ActiveProfile, feedId);
            _pendingInitialRefreshFeedIds.Remove(feedId);
            await LoadProfileReaderDataAsync(CancellationToken.None);
            StatusMessage = $"Unfollowed {feedLink.Label}.";
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

    private void OnCatalogBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    private void ClearCatalogFilters()
    {
        CatalogSearchQuery = string.Empty;
        SelectedCatalogCategory = CatalogCategoryOptions.FirstOrDefault(option => option.CategoryId is null);
        SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(option => option.CollectionId is null);
        HideFollowedCatalogFeeds = false;
    }

    private async Task PersistProfilePreferencesAsync()
    {
        if (_profileService is null)
        {
            return;
        }

        await _profilePreferencesSaveGate.WaitAsync();
        try
        {
            await _profileService.SavePreferencesAsync(ActiveProfile.Id, _profilePreferences);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not save the Hide followed preference: {exception.Message}";
        }
        finally
        {
            _profilePreferencesSaveGate.Release();
        }
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
        _catalogFeedPreviewCache.Store(feed.Id, preview);
        return preview;
    }

    public Task<string> LoadCatalogFeedRawXmlAsync(
        CatalogFeedListItem feed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        if (_catalogFeedPreviewService is null)
        {
            throw new InvalidOperationException("Raw feed XML is unavailable.");
        }

        var catalogFeed = new CatalogFeed(
            feed.Id,
            feed.Name,
            feed.FeedUrl,
            feed.Description,
            feed.CategoryId,
            feed.WebsiteUrl);
        return _catalogFeedPreviewService.GetRawFeedXmlAsync(catalogFeed, cancellationToken);
    }

    private void NavigateTo(SidebarLink link, bool recordHistory = true)
    {
        if (recordHistory && (ActiveRoute != link.Route || SelectedArticle is not null))
        {
            RecordCurrentNavigationState();
        }

        SelectedArticle = null;
        ActiveRoute = link.Route;
        UpdateSelectedLinks();
        if (link.IsFolder || link.IsFeedEntry)
        {
            ArticleListScrollToTopRequest++;
        }

        _ = RefreshRouteFeedsAsync(link.Route);
    }

    private void ActivateSidebarLink(SidebarLink link)
    {
        if (ActiveRoute != link.Route || SelectedArticle is not null)
        {
            RecordCurrentNavigationState();
        }

        if (link.IsFolder)
        {
            _sidebarFeedNavigation.ToggleFolder(link);
        }

        NavigateTo(link, recordHistory: false);
    }

    private void OpenArticle(ArticleRowViewModel article)
    {
        if (!IsSameArticle(SelectedArticle, article))
        {
            RecordCurrentNavigationState();
        }

        article.IsRead = true;
        SelectedArticle = article;
    }

    private static bool IsSameArticle(ArticleRowViewModel? current, ArticleRowViewModel target) =>
        ReferenceEquals(current, target) ||
        (current?.ArticleId is { } currentId &&
         string.Equals(currentId, target.ArticleId, StringComparison.Ordinal));

    private void RecordCurrentNavigationState()
    {
        _navigationHistory.Push(new NavigationHistoryEntry(
            ActiveRoute,
            SelectedArticle?.ArticleId,
            SelectedArticle,
            SearchQuery,
            SelectedArticleTopic?.Term,
            SelectedArticleTopic?.Scheme,
            UnreadOnly,
            SavedOnly,
            IsCardsView,
            IsMagazineView,
            IsSortByDate,
            CatalogSearchQuery,
            SelectedCatalogCategory?.CategoryId,
            SelectedCatalogCollection?.CollectionId,
            HideFollowedCatalogFeeds,
            _sidebarFeedNavigation.ExpandedFolderNames.ToArray()));

        NotifyNavigationHistoryChanged();
    }

    private void GoBack()
    {
        if (_navigationHistory.Pop() is not { } previous)
        {
            return;
        }

        NotifyNavigationHistoryChanged();
        RestoreNavigationState(previous);
    }

    private void RestoreNavigationState(NavigationHistoryEntry state)
    {
        var previousRoute = ActiveRoute;
        var route = IsNavigationRouteAvailable(state.ActiveRoute) ? state.ActiveRoute : "All";
        SelectedArticle = null;
        ActiveRoute = route;
        SearchQuery = state.SearchQuery;
        UnreadOnly = state.UnreadOnly;
        SavedOnly = state.SavedOnly;
        IsSortByDate = state.IsSortByDate;
        IsSortByFolder = !state.IsSortByDate;
        SetArticleViewModeIfSelected(true, state.IsCardsView, state.IsMagazineView);
        CatalogSearchQuery = state.CatalogSearchQuery;
        SelectedCatalogCategory = CatalogCategoryOptions.FirstOrDefault(
            option => option.CategoryId == state.SelectedCatalogCategoryId) ??
            CatalogCategoryOptions.FirstOrDefault(option => option.CategoryId is null);
        SelectedCatalogCollection = CatalogCollectionOptions.FirstOrDefault(
            option => option.CollectionId == state.SelectedCatalogCollectionId) ??
            CatalogCollectionOptions.FirstOrDefault(option => option.CollectionId is null);
        HideFollowedCatalogFeeds = state.HideFollowedCatalogFeeds;

        _sidebarFeedNavigation.RestoreExpandedFolders(state.ExpandedFolderNames);

        ApplyArticleFilters();
        SelectedArticleTopic = state.SelectedArticleTopicTerm is null
            ? ArticleTopicOptions.FirstOrDefault(option => option.Term is null)
            : ArticleTopicOptions.FirstOrDefault(option =>
                string.Equals(option.Term, state.SelectedArticleTopicTerm, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(option.Scheme, state.SelectedArticleTopicScheme, StringComparison.OrdinalIgnoreCase))
              ?? ArticleTopicOptions.FirstOrDefault(option => option.Term is null);
        UpdateSelectedLinks();

        var selectedArticle = state.SelectedArticleId is null
            ? state.SelectedArticleReference
            : _allArticles.FirstOrDefault(article => article.ArticleId == state.SelectedArticleId);
        if (selectedArticle is not null && VisibleArticles.Contains(selectedArticle))
        {
            SelectedArticle = selectedArticle;
        }

        if (!string.Equals(previousRoute, route, StringComparison.Ordinal))
        {
            _ = RefreshRouteFeedsAsync(route);
        }
    }

    private bool IsNavigationRouteAvailable(string route) =>
        string.Equals(route, "All", StringComparison.Ordinal) ||
        PrimaryLinks
            .Concat(ReadingLinks)
            .Concat(FeedLinks)
            .Concat(TagLinks)
            .Concat(AdminLinks)
            .Any(link => string.Equals(link.Route, route, StringComparison.Ordinal));

    private void NotifyNavigationHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        BackCommand.NotifyCanExecuteChanged();
        SelectArticleTopicCommand.NotifyCanExecuteChanged();
    }

    private bool CanSelectArticleTopic(ArticleTopicChip topic) =>
        topic.IsSelectable && (SelectedArticle is null || CanGoBack);

    private void SelectArticleTopic(ArticleTopicChip topic)
    {
        if (topic.Term is not { } term)
        {
            return;
        }

        if (SelectedArticle is not null)
        {
            if (_navigationHistory.Pop() is not { } previous)
            {
                return;
            }

            NotifyNavigationHistoryChanged();
            RestoreNavigationState(previous with
            {
                SelectedArticleTopicTerm = term,
                SelectedArticleTopicScheme = topic.Scheme
            });
            return;
        }

        SelectedArticleTopic = ArticleTopicOptions.FirstOrDefault(option =>
            string.Equals(option.Term, term, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(option.Scheme, topic.Scheme, StringComparison.OrdinalIgnoreCase))
            ?? new ArticleTopicOption(term, topic.Scheme, topic.Name, 0);
    }

    private bool CanSelectFeedTag(ArticleFeedTagChip tag) => tag.IsSelectable;

    private void SelectFeedTag(ArticleFeedTagChip tag)
    {
        var tagLink = TagLinks.FirstOrDefault(link =>
            string.Equals(link.Label, tag.Name, StringComparison.OrdinalIgnoreCase));
        if (tagLink is null)
        {
            throw new InvalidOperationException($"The user tag '{tag.Name}' is not available.");
        }

        NavigateTo(tagLink);
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

    private ArticleRowViewModel[] GetArticleNavigationSequence() => IsSortByDate
        ? VisibleArticles
            .OrderByDescending(article => article.PublishedAt)
            .ToArray()
        : VisibleArticles
            .OrderBy(article => article.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(article => article.PublishedAt)
            .ToArray();

    private bool CanNavigateToAdjacentArticle(int offset)
    {
        if (SelectedArticle is null)
        {
            return false;
        }

        var sequence = GetArticleNavigationSequence();
        var selectedIndex = Array.IndexOf(sequence, SelectedArticle);
        return selectedIndex >= 0 && selectedIndex + offset >= 0 && selectedIndex + offset < sequence.Length;
    }

    private void NavigateToAdjacentArticle(int offset)
    {
        if (!CanNavigateToAdjacentArticle(offset))
        {
            return;
        }

        var sequence = GetArticleNavigationSequence();
        var selectedIndex = Array.IndexOf(sequence, SelectedArticle);
        OpenArticle(sequence[selectedIndex + offset]);
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
        await LoadFeedRefreshStatesAsync(cancellationToken);
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

        _catalogBrowser.UpdateSubscriptions(_subscribedFeedIds);

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
        ApplyArticleFilters();
    }

    private async Task LoadFeedRefreshStatesAsync(CancellationToken cancellationToken)
    {
        if (_readingService is null)
        {
            return;
        }

        var states = await _readingService.GetFeedRefreshStatesAsync(ActiveProfile.Id, cancellationToken);
        _feedRefreshStatus.ReplaceStates(states);
        var failedStates = states.Where(state => !string.IsNullOrWhiteSpace(state.LastFailure)).ToArray();
        SetFailedRefreshFeedIds(failedStates.Select(state => state.FeedId));
        RefreshFailureMessage = failedStates.Length == 0
            ? null
            : string.Join(" ", failedStates.Select(state => state.LastFailure));
        OnPropertyChanged(nameof(SelectedArticleRefreshStatusMessage));
        OnPropertyChanged(nameof(HasSelectedArticleRefreshState));
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

    private async Task RetryFailedFeedsAsync()
    {
        var refreshService = _feedRefreshService;
        var failedFeedIds = GetRelevantFailedRefreshFeedIds();
        if (refreshService is null || failedFeedIds.Length == 0)
        {
            return;
        }

        await RefreshUsingAsync(() => refreshService.RefreshFeedsAsync(ActiveProfile.Id, failedFeedIds));
    }

    private async Task RefreshRouteFeedsAsync(string route)
    {
        if (_feedRefreshService is null || IsRefreshing)
        {
            return;
        }

        string[] feedIds;
        if (route.StartsWith("feed:", StringComparison.Ordinal))
        {
            feedIds = [route["feed:".Length..]];
        }
        else if (route.StartsWith("folder:", StringComparison.Ordinal) &&
                 _sidebarFeedNavigation.FindFolderFeedIds(route["folder:".Length..]) is { } folderFeedIds)
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
        RefreshFailureMessage = null;
        SetFailedRefreshFeedIds([]);
        try
        {
            var summary = await refresh();
            await LoadFeedRefreshStatesAsync(CancellationToken.None);
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
                : $"Fetched {summary.ArticlesFetched} articles; New {summary.ArticlesAdded}; {summary.Failures.Count} feed(s) failed.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Refresh failed: {exception.Message}";
            RefreshFailureMessage = exception.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SetFailedRefreshFeedIds(IEnumerable<string> feedIds)
    {
        _feedRefreshStatus.ReplaceFailedFeedIds(feedIds);
        OnPropertyChanged(nameof(HasFailedRefreshFeeds));
        NotifyRefreshFailureContextChanged();
        UpdateFeedWarnings();
    }

    private void UpdateFeedWarnings()
    {
        var selectedFeedId = SelectedFeedWarning?.FeedId;
        var warnings = _feedRefreshStatus.FailedFeedIds
            .Select(CreateFeedRefreshWarning)
            .OfType<FeedRefreshWarning>()
            .OrderBy(warning => warning.FeedName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (warnings.Count == 0 && !string.IsNullOrWhiteSpace(RefreshFailureMessage))
        {
            warnings.Add(new FeedRefreshWarning(null, "Feed refresh", RefreshFailureMessage));
        }

        _feedRefreshWarnings.ReplaceAll(warnings);
        var selectedIndex = selectedFeedId is null
            ? -1
            : warnings.FindIndex(warning => warning.FeedId == selectedFeedId);
        _selectedFeedWarningIndex = selectedIndex >= 0
            ? selectedIndex
            : warnings.Count == 0
                ? -1
                : Math.Clamp(_selectedFeedWarningIndex, 0, warnings.Count - 1);
        if (warnings.Count == 0)
        {
            IsFeedWarningsPopupOpen = false;
        }

        OnPropertyChanged(nameof(HasFeedWarnings));
        OnPropertyChanged(nameof(FeedWarningCount));
        NotifySelectedFeedWarningChanged();
        ToggleFeedWarningsPopupCommand.NotifyCanExecuteChanged();
    }

    private FeedRefreshWarning? CreateFeedRefreshWarning(string feedId)
    {
        var message = _feedRefreshStatus.GetFailureMessage(feedId);
        return string.IsNullOrWhiteSpace(message)
            ? null
            : new FeedRefreshWarning(feedId, GetFeedName(feedId), message);
    }

    private string GetFeedName(string feedId) =>
        _sidebarFeedNavigation.FeedLinks.FirstOrDefault(link =>
            string.Equals(link.Route, $"feed:{feedId}", StringComparison.Ordinal))?.Label ??
        _catalogBrowser.CatalogFeeds.FirstOrDefault(feed =>
            string.Equals(feed.Id, feedId, StringComparison.Ordinal))?.Name ??
        feedId;

    private void NotifySelectedFeedWarningChanged()
    {
        OnPropertyChanged(nameof(SelectedFeedWarning));
        OnPropertyChanged(nameof(FeedWarningPositionLabel));
        OnPropertyChanged(nameof(HasSelectedFeedWarningFeed));
        PreviousFeedWarningCommand.NotifyCanExecuteChanged();
        NextFeedWarningCommand.NotifyCanExecuteChanged();
    }

    private void SelectPreviousFeedWarning()
    {
        if (_selectedFeedWarningIndex > 0)
        {
            _selectedFeedWarningIndex--;
            NotifySelectedFeedWarningChanged();
        }
    }

    private void SelectNextFeedWarning()
    {
        if (_selectedFeedWarningIndex >= 0 &&
            _selectedFeedWarningIndex < _feedRefreshWarnings.Count - 1)
        {
            _selectedFeedWarningIndex++;
            NotifySelectedFeedWarningChanged();
        }
    }

    private async Task DismissCurrentFeedWarningAsync()
    {
        if (SelectedFeedWarning is not { } warning)
        {
            return;
        }

        if (warning.FeedId is not { } feedId)
        {
            RefreshFailureMessage = null;
            UpdateFeedWarnings();
            StatusMessage = "Dismissed the feed refresh warning.";
            return;
        }

        if (_readingService is null)
        {
            StatusMessage = "Feed warning dismissal is unavailable.";
            return;
        }

        try
        {
            await _readingService.ClearFeedRefreshFailureAsync(ActiveProfile, feedId);
            await LoadFeedRefreshStatesAsync(CancellationToken.None);
            StatusMessage = $"Dismissed the warning for {warning.FeedName}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not dismiss the warning for {warning.FeedName}: {exception.Message}";
        }
    }

    private async Task DismissAllFeedWarningsAsync()
    {
        if (_readingService is null)
        {
            StatusMessage = "Feed warning dismissal is unavailable.";
            return;
        }

        try
        {
            await _readingService.ClearFeedRefreshFailuresAsync(ActiveProfile);
            await LoadFeedRefreshStatesAsync(CancellationToken.None);
            RefreshFailureMessage = null;
            StatusMessage = "Dismissed all feed warnings.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not dismiss feed warnings: {exception.Message}";
        }
    }

    private async Task RetryCurrentFeedWarningAsync()
    {
        if (_feedRefreshService is null || SelectedFeedWarning?.FeedId is not { } feedId)
        {
            return;
        }

        await RefreshUsingAsync(
            () => _feedRefreshService.RefreshFeedsAsync(ActiveProfile.Id, [feedId]));
    }

    private string[] GetRelevantFailedRefreshFeedIds()
    {
        IEnumerable<string> displayedFeedIds;
        if (SelectedArticle is { } selectedArticle)
        {
            displayedFeedIds = selectedArticle.FeedId is { } selectedFeedId
                ? [selectedFeedId]
                : [];
        }
        else if (IsArticleListVisible)
        {
            displayedFeedIds = VisibleArticles
                .Select(article => article.FeedId)
                .OfType<string>();
        }
        else
        {
            displayedFeedIds = [];
        }

        var displayedFeedIdSet = displayedFeedIds.ToHashSet(StringComparer.Ordinal);
        return _feedRefreshStatus.FailedFeedIds
            .Where(displayedFeedIdSet.Contains)
            .ToArray();
    }

    private void NotifyRefreshFailureContextChanged()
    {
        OnPropertyChanged(nameof(VisibleRefreshFailureMessage));
        OnPropertyChanged(nameof(HasVisibleRefreshFailure));
        OnPropertyChanged(nameof(HasVisibleFailedRefreshFeeds));
        RetryFailedFeedsCommand.NotifyCanExecuteChanged();
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
                    GetSuggestedFolders([feed.CategoryName]),
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
        => _sidebarFeedNavigation.Update(folders, subscriptions);

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
        OnPropertyChanged(nameof(SidebarColumnMinWidth));
        OnPropertyChanged(nameof(SidebarHeaderMargin));
    }

    public void SetSidebarPanelWidth(double width)
    {
        if (!double.IsFinite(width))
        {
            return;
        }

        var constrainedWidth = Math.Clamp(width, MinimumSidebarWidth, MaximumSidebarWidth);
        if (SetProperty(ref _sidebarWidth, constrainedWidth, nameof(SidebarPanelWidth)))
        {
            OnPropertyChanged(nameof(SidebarColumnWidth));
        }
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
        var result = ArticleListFilter.Apply(
            _allArticles,
            ActiveRoute,
            SearchQuery,
            UnreadOnly,
            SavedOnly,
            SelectedArticleTopic,
            IsSortByFolder,
            _folderArticlesPerFeedLimit);
        UpdateArticleTopicOptions(result);
        _visibleArticles.ReplaceAll(result.VisibleArticles);

        OnPropertyChanged(nameof(IsArticleListEmpty));
        OnPropertyChanged(nameof(ArticleListEmptyMessage));
        NotifyRefreshFailureContextChanged();
        PreviousArticleCommand.NotifyCanExecuteChanged();
        NextArticleCommand.NotifyCanExecuteChanged();
    }

    private void UpdateArticleTopicOptions(ArticleListFilterResult result)
    {
        _updatingArticleTopicOptions = true;
        try
        {
            ArticleTopicOptions.Clear();
            foreach (var option in result.TopicOptions)
            {
                ArticleTopicOptions.Add(option);
            }

            _selectedArticleTopic = result.SelectedTopic;
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

    private string GetStartPageRoute() => GetStartPageRoute(_profilePreferences.StartPage);

    private string GetStartPageRoute(ProfileStartPage startPage) => startPage switch
    {
        ProfileStartPage.All => "All",
        ProfileStartPage.FirstFolder => FeedLinks.FirstOrDefault(link =>
            link.Route.StartsWith("folder:", StringComparison.Ordinal))?.Route ?? "Today",
        _ => "Today"
    };

}