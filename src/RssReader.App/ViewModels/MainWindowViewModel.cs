using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly List<ArticleRowViewModel> _allArticles;
    private readonly CatalogService? _catalogService;
    private readonly ReadingService? _readingService;
    private readonly FeedRefreshService? _feedRefreshService;
    private ProfilePreferences _profilePreferences;
    private string _activeRoute;
    private string _searchQuery = string.Empty;
    private string _quickQuery = string.Empty;
    private string _statusMessage = string.Empty;
    private ArticleRowViewModel? _selectedArticle;
    private bool _isSidebarExpanded = true;
    private bool _isRefreshing;
    private bool _unreadOnly;
    private bool _savedOnly;
    private bool _isCardsView = true;
    private bool _isMagazineView;
    private bool _isSortByDate;
    private bool _hasAppliedStartPage;
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
        ProfilePreferences? profilePreferences = null)
    {
        ActiveProfile = profile;
        _catalogService = catalogService;
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
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsRefreshing && !IsCatalogMaster);
        ToggleSubscriptionCommand = new AsyncCommand<CatalogFeedListItem>(ToggleSubscriptionAsync);

        UpdateSelectedLinks();
        ApplyArticleFilters();
    }

    public Profile ActiveProfile { get; }
    public string ActiveProfileName => ActiveProfile.Name;
    public bool IsCatalogMaster => ActiveProfile.IsCatalogMaster;
    public bool IsSidebarExpanded => _isSidebarExpanded;
    public GridLength SidebarColumnWidth => new(IsSidebarExpanded ? 286 : 64);
    public double SidebarMinimumWidth => IsSidebarExpanded ? 220 : 64;

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
    public RelayCommand RefreshCommand { get; }
    public AsyncCommand<CatalogFeedListItem> ToggleSubscriptionCommand { get; }

    public event Action? ProfileSwitchRequested;

    public Func<
        IReadOnlyList<string>,
        IReadOnlyList<string>,
        Func<string, Task>,
        Task<string?>>? FolderSelectionRequested { get; set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_catalogService is not null)
        {
            var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
            var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
            var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
            var collections = await _catalogService.GetCollectionsAsync(cancellationToken);

            CatalogCategories.Clear();
            foreach (var category in categories)
            {
                CatalogCategories.Add(category);
            }

            CatalogCollections.Clear();
            foreach (var collection in collections)
            {
                CatalogCollections.Add(collection);
            }

            CatalogFeeds.Clear();
            foreach (var feed in feeds)
            {
                CatalogFeeds.Add(new CatalogFeedListItem(
                    feed.Id,
                    feed.Name,
                    feed.FeedUrl,
                    feed.Description,
                    feed.CategoryId is not null && categoryNames.TryGetValue(feed.CategoryId, out var categoryName)
                        ? categoryName
                        : null));
            }

            if (CatalogManagement is not null)
            {
                await CatalogManagement.InitializeAsync(cancellationToken);
            }
        }

        if (_readingService is not null && !IsCatalogMaster)
        {
            await LoadProfileReaderDataAsync(cancellationToken);
        }
    }

    public void ApplyPreferences(ProfilePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _profilePreferences = preferences;
        _folderArticlesPerFeedLimit = preferences.FolderArticleLimitPerFeed;
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
    public bool IsArticleCountVisible => IsArticleListVisible;
    public bool IsArticleListVisible => SelectedArticle is null && !IsCatalogBrowserVisible && !IsCatalogAdminVisible && !IsGoToRoute;
    public bool IsReadingViewVisible => SelectedArticle is not null;
    public bool IsCatalogBrowserVisible => ActiveRoute == "Follow sources";
    public bool IsCatalogAdminVisible => IsCatalogMaster && ActiveRoute == "Manage catalog";
    public bool IsArticleListEmpty => VisibleArticles.Count == 0;

    private void NavigateTo(SidebarLink link)
    {
        SelectedArticle = null;
        ActiveRoute = link.Route;
        UpdateSelectedLinks();
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

        UpdateSelectedLinks();
        ApplyQuickFilter();
        ApplyArticleFilters();
    }

    private async Task RefreshAsync()
    {
        if (_feedRefreshService is null || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        StatusMessage = "Refreshing feeds...";
        try
        {
            var summary = await _feedRefreshService.RefreshProfileAsync(ActiveProfile.Id);
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
            }
            else
            {
                if (FolderSelectionRequested is null)
                {
                    return;
                }

                var suggestions = CatalogFeeds
                    .Select(item => item.CategoryName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!.Trim())
                    .Where(name => !FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (suggestions.Length == 0)
                {
                    suggestions = new[] { "News", "Technology", "Gaming", "Culture" }
                        .Where(name => !FolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                }

                var folderName = await FolderSelectionRequested(
                    FolderNames.ToArray(),
                    suggestions,
                    CreateFolderFromPickerAsync);
                if (string.IsNullOrWhiteSpace(folderName))
                {
                    return;
                }

                await _readingService.SubscribeAsync(ActiveProfile, feed.Id, folderName);
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
        _isSidebarExpanded = !_isSidebarExpanded;
        OnPropertyChanged(nameof(IsSidebarExpanded));
        OnPropertyChanged(nameof(SidebarColumnWidth));
        OnPropertyChanged(nameof(SidebarMinimumWidth));
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