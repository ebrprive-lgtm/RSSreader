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
    private readonly CatalogCollectionMembershipEditor _collectionMembershipEditor;
    private readonly CatalogTaxonomyManagement _taxonomyManagement;
    private string _feedName = string.Empty;
    private string _feedUrl = string.Empty;
    private string _feedWebsiteUrl = string.Empty;
    private string _feedDescription = string.Empty;
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

    public CatalogManagementViewModel(
        Profile actor,
        CatalogService catalogService,
        CatalogFeedPreviewService? catalogFeedPreviewService = null)
    {
        _actor = actor;
        _catalogService = catalogService;
        _catalogFeedPreviewService = catalogFeedPreviewService;
        _taxonomyManagement = new CatalogTaxonomyManagement(
            _actor,
            _catalogService,
            () => Categories,
            message => ErrorMessage = message,
            () => RefreshAsync(),
            message => ConfirmDeleteRequested?.Invoke(message) ?? Task.FromResult(false));
        _taxonomyManagement.PropertyChanged += OnTaxonomyManagementPropertyChanged;
        AddFeedCommand = new AsyncCommand(AddFeedAsync);
        CheckFeedHealthCommand = new AsyncCommand<CatalogFeedListItem>(CheckFeedHealthAsync);
        LoadStarterPackCommand = new AsyncCommand(LoadStarterPackAsync);
        DeleteFeedCommand = new AsyncCommand<CatalogFeedListItem>(DeleteFeedAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        DismissImportMessageCommand = new RelayCommand(() => ImportMessage = string.Empty);
        AddCategoryCommand = new AsyncCommand(_taxonomyManagement.AddCategoryAsync);
        MergeCategoryCommand = new AsyncCommand(_taxonomyManagement.MergeCategoryIntoExistingAsync);
        DeleteCategoryCommand = new AsyncCommand<CatalogCategory>(_taxonomyManagement.DeleteCategoryAsync);
        AddCollectionCommand = new AsyncCommand(_taxonomyManagement.AddCollectionAsync);
        DeleteCollectionCommand = new AsyncCommand<CatalogCollection>(_taxonomyManagement.DeleteCollectionAsync);
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
        _collectionMembershipEditor = new CatalogCollectionMembershipEditor(
            _actor,
            _catalogService,
            () => Feeds,
            () => Collections,
            () => CollectionFilterOptions,
            message => ErrorMessage = message,
            () => RefreshAsync());
    }

    public ObservableCollection<CatalogFeedListItem> Feeds { get; private set; } = [];
    internal Func<CancellationToken, Task>? CatalogRefreshRequested { get; set; }
    public ICollectionView VisibleFeeds { get; private set; }
    public ObservableCollection<CatalogCategory> Categories { get; } = [];
    public ObservableCollection<CatalogCollection> Collections { get; } = [];
    public ObservableCollection<CatalogCategoryFilterOption> CategoryFilterOptions { get; } = [];
    public ObservableCollection<CatalogCollectionFilterOption> CollectionFilterOptions { get; } = [];
    public ObservableCollection<CatalogHealthFilterOption> HealthFilterOptions { get; } = [];
    public Func<string, Task<bool>>? ConfirmDeleteRequested { get; set; }

    public AsyncCommand AddFeedCommand { get; }
    public AsyncCommand<CatalogFeedListItem> CheckFeedHealthCommand { get; }
    public AsyncCommand LoadStarterPackCommand { get; }
    public AsyncCommand<CatalogFeedListItem> DeleteFeedCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand DismissImportMessageCommand { get; }
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
        get => _taxonomyManagement.CategoryName;
        set => _taxonomyManagement.CategoryName = value;
    }

    public CatalogCategory? MergeTargetCategory => _taxonomyManagement.MergeTargetCategory;

    public bool CanMergeCategory => _taxonomyManagement.CanMergeCategory;
    public string MergeCategoryButtonLabel => _taxonomyManagement.MergeCategoryButtonLabel;

    public string CollectionName
    {
        get => _taxonomyManagement.CollectionName;
        set => _taxonomyManagement.CollectionName = value;
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string ImportMessage
    {
        get => _importMessage;
        private set
        {
            if (SetProperty(ref _importMessage, value))
            {
                OnPropertyChanged(nameof(HasImportMessage));
            }
        }
    }

    public bool HasImportMessage => !string.IsNullOrWhiteSpace(ImportMessage);

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
        _taxonomyManagement.ResetEntryForm();
        FeedName = string.Empty;
        FeedUrl = string.Empty;
        FeedWebsiteUrl = string.Empty;
        FeedDescription = string.Empty;
        SelectedCategory = null;
    }

    public void PrepareFeedEdit(CatalogFeedListItem feed)
    {
        ErrorMessage = string.Empty;
        _editingFeedId = feed.Id;
        _taxonomyManagement.ClearEditingState();
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
        _taxonomyManagement.PrepareCategoryEdit(category);
    }

    public void PrepareCollectionEdit(CatalogCollection collection)
    {
        ErrorMessage = string.Empty;
        _editingFeedId = null;
        _taxonomyManagement.PrepareCollectionEdit(collection);
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

    public async Task<string> GetRawFeedXmlAsync(
        CatalogFeedListItem feedItem,
        CancellationToken cancellationToken = default)
    {
        if (_catalogFeedPreviewService is null)
        {
            throw new InvalidOperationException("Raw feed XML is unavailable.");
        }

        var feed = (await _catalogService.GetFeedsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedItem.Id)
            ?? throw new InvalidOperationException("That feed no longer exists in the catalog.");
        return await _catalogFeedPreviewService.GetRawFeedXmlAsync(feed, cancellationToken);
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
        var selectedFeedId = SelectedFeed?.Id;
        var selectedManagementCollectionId = SelectedCollection?.Id;
        var categories = await _catalogService.GetCategoriesAsync(cancellationToken);
        var feeds = await _catalogService.GetFeedsAsync(cancellationToken);
        var collections = await _catalogService.GetCollectionsAsync(cancellationToken);
        var collectionFeedIds = await _catalogService.GetCollectionFeedIdsByCollectionAsync(cancellationToken);
        var snapshot = CatalogManagementSnapshot.Create(feeds, categories, collections, collectionFeedIds);
        Feeds = new ObservableCollection<CatalogFeedListItem>(snapshot.Feeds);

        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }
        _taxonomyManagement.NotifyCategoriesChanged();

        CategoryFilterOptions.Clear();
        foreach (var option in snapshot.CategoryFilterOptions)
        {
            CategoryFilterOptions.Add(option);
        }

        CollectionFilterOptions.Clear();
        foreach (var option in snapshot.CollectionFilterOptions)
        {
            CollectionFilterOptions.Add(option);
        }

        var allCategories = snapshot.CategoryFilterOptions[0];
        var allCollections = snapshot.CollectionFilterOptions[0];
        _selectedCategoryFilter = CategoryFilterOptions.FirstOrDefault(option => option.CategoryId == selectedCategoryId)
            ?? allCategories;
        _selectedCollectionFilter = CollectionFilterOptions.FirstOrDefault(option => option.CollectionId == selectedCollectionId)
            ?? allCollections;
        OnPropertyChanged(nameof(SelectedCategoryFilter));
        OnPropertyChanged(nameof(SelectedCollectionFilter));

        Collections.Clear();
        foreach (var collection in snapshot.Collections)
        {
            Collections.Add(collection);
        }

        SelectedFeed = Feeds.FirstOrDefault(feed => feed.Id == selectedFeedId);
        SelectedCollection = Collections.FirstOrDefault(collection => collection.Id == selectedManagementCollectionId);
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
        Filter = item => item is CatalogFeedListItem feed &&
            CatalogFeedFilter.IsVisible(
                feed,
                SearchQuery,
                ShowMetadataGapsOnly,
                SelectedCategoryFilter?.CategoryId,
                SelectedCollectionFilter,
                SelectedHealthFilter?.Filter ?? CatalogFeedHealthFilter.All),
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

    private void OnTaxonomyManagementPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(e.PropertyName);

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
            var summary = $"Added {result.AddedCount} feed(s); skipped {result.SkippedCount + parserSkippedCount}.";
            var duplicateReport = result.DuplicateCandidates.Select(candidate => candidate.Kind switch
            {
                FeedDuplicateKind.ExactUrl =>
                    $"Exact URL duplicate (skipped): {candidate.ImportedName} matches {candidate.ExistingName} ({candidate.ImportedUrl}).",
                FeedDuplicateKind.SameNameDifferentUrl =>
                    $"Same name, different URL (kept separately): {candidate.ImportedName} ({candidate.ImportedUrl}) and {candidate.ExistingName} ({candidate.ExistingUrl}).",
                _ => throw new ArgumentOutOfRangeException(nameof(candidate.Kind))
            });
            ImportMessage = string.Join(Environment.NewLine, new[] { summary }.Concat(duplicateReport));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task DeleteFeedAsync(CatalogFeedListItem feed)
    {
        var confirmation = ConfirmDeleteRequested;
        if (confirmation is null || !await confirmation(
            $"Remove '{feed.Name}' from the shared catalog? This removes its subscriptions and cached articles, including read/saved state, from every profile and removes its collection memberships. This cannot be undone."))
        {
            return;
        }

        await _catalogService.DeleteFeedAsync(_actor, feed.Id);
        await RefreshAsync();
    }

    private async Task CheckFeedHealthAsync(CatalogFeedListItem feed)
    {
        ErrorMessage = string.Empty;
        feed.IsHealthCheckInProgress = true;
        try
        {
            var result = await _catalogService.CheckFeedHealthAsync(_actor, feed.Id);
            await RefreshAsync();
            if (!result.IsSuccessful)
            {
                ErrorMessage = $"Could not check feed '{feed.Name}':{Environment.NewLine}" +
                    (result.ErrorMessage ?? "The feed check failed without additional diagnostic details.");
            }
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

    private async Task AddFeedToCollectionAsync()
    {
        if (SelectedFeed is null || SelectedCollection is null)
        {
            ErrorMessage = "Select a feed and collection.";
            return;
        }

        var collectionIds = GetCollectionIdsForFeed(SelectedFeed.Id).ToHashSet(StringComparer.Ordinal);
        collectionIds.Add(SelectedCollection.Id);
        await UpdateFeedCollectionsAsync(SelectedFeed.Id, collectionIds);
    }

    private async Task RemoveFeedFromCollectionAsync()
    {
        if (SelectedFeed is null || SelectedCollection is null)
        {
            ErrorMessage = "Select a feed and collection.";
            return;
        }

        var collectionIds = GetCollectionIdsForFeed(SelectedFeed.Id).ToHashSet(StringComparer.Ordinal);
        collectionIds.Remove(SelectedCollection.Id);
        await UpdateFeedCollectionsAsync(SelectedFeed.Id, collectionIds);
    }

    public IReadOnlySet<string> GetCollectionIdsForFeed(string feedId) =>
        _collectionMembershipEditor.GetCollectionIdsForFeed(feedId);

    public IReadOnlySet<string> GetFeedIdsForCollection(string collectionId) =>
        _collectionMembershipEditor.GetFeedIdsForCollection(collectionId);

    public Task<bool> UpdateFeedCollectionsAsync(
        string feedId,
        IReadOnlyCollection<string> collectionIds) =>
        _collectionMembershipEditor.UpdateFeedCollectionsAsync(feedId, collectionIds);

    public Task<bool> UpdateCollectionFeedsAsync(
        string collectionId,
        IReadOnlyCollection<string> feedIds) =>
        _collectionMembershipEditor.UpdateCollectionFeedsAsync(collectionId, feedIds);
}