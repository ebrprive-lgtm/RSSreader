using RssReader.Domain;

namespace RssReader.Application;

public enum FeedDuplicateKind
{
    ExactUrl,
    SameNameDifferentUrl
}

public sealed record FeedDuplicateCandidate(
    FeedDuplicateKind Kind,
    string ImportedName,
    string ImportedUrl,
    string ExistingName,
    string ExistingUrl);

public sealed record FeedImportSummary(
    int AddedCount,
    int SkippedCount,
    IReadOnlyList<FeedDuplicateCandidate> DuplicateCandidates);

public sealed record CatalogFeedHealthCheckResult(
    bool IsSuccessful,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null);

public sealed class CatalogService
{
    private readonly ICatalogStore _store;
    private readonly CatalogFeedService _feeds;
    private readonly CatalogCategoryService _categories;
    private readonly CatalogCollectionService _collections;

    public CatalogService(ICatalogStore store, IFeedDownloader? feedDownloader = null)
    {
        _store = store;
        _feeds = new CatalogFeedService(store, feedDownloader);
        _categories = new CatalogCategoryService(store);
        _collections = new CatalogCollectionService(store);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        _store.InitializeAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) =>
        _store.GetFeedsAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        _store.GetCategoriesAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) =>
        _store.GetCollectionsAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(
        string collectionId,
        CancellationToken cancellationToken = default) =>
        _store.GetCollectionFeedIdsAsync(collectionId, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(
        CancellationToken cancellationToken = default) =>
        _store.GetCollectionFeedIdsByCollectionAsync(cancellationToken);

    public Task<CatalogFeed> AddFeedAsync(
        Profile actor,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken = default,
        string? websiteUrl = null) =>
        _feeds.AddFeedAsync(actor, name, feedUrl, description, categoryId, cancellationToken, websiteUrl);

    public Task<CatalogFeed> UpdateFeedAsync(
        Profile actor,
        string feedId,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken = default,
        string? websiteUrl = null) =>
        _feeds.UpdateFeedAsync(actor, feedId, name, feedUrl, description, categoryId, cancellationToken, websiteUrl);

    public Task<CatalogFeedHealthCheckResult> CheckFeedHealthAsync(
        Profile actor,
        string feedId,
        CancellationToken cancellationToken = default) =>
        _feeds.CheckFeedHealthAsync(actor, feedId, cancellationToken);

    public Task<FeedImportSummary> ImportFeedsAsync(
        Profile actor,
        IEnumerable<OpmlFeed> importedFeeds,
        CancellationToken cancellationToken = default) =>
        _feeds.ImportFeedsAsync(actor, importedFeeds, cancellationToken);

    public Task<CatalogCategory> AddCategoryAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken = default) =>
        _categories.AddCategoryAsync(actor, name, cancellationToken);

    public Task<CatalogCategory> UpdateCategoryAsync(
        Profile actor,
        string categoryId,
        string name,
        CancellationToken cancellationToken = default) =>
        _categories.UpdateCategoryAsync(actor, categoryId, name, cancellationToken);

    public Task MergeCategoriesAsync(
        Profile actor,
        string sourceCategoryId,
        string targetCategoryId,
        CancellationToken cancellationToken = default) =>
        _categories.MergeCategoriesAsync(actor, sourceCategoryId, targetCategoryId, cancellationToken);

    public Task DeleteCategoryAsync(
        Profile actor,
        string categoryId,
        CancellationToken cancellationToken = default) =>
        _categories.DeleteCategoryAsync(actor, categoryId, cancellationToken);

    public Task<CatalogCollection> AddCollectionAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken = default) =>
        _collections.AddCollectionAsync(actor, name, cancellationToken);

    public Task<CatalogCollection> UpdateCollectionAsync(
        Profile actor,
        string collectionId,
        string name,
        CancellationToken cancellationToken = default) =>
        _collections.UpdateCollectionAsync(actor, collectionId, name, cancellationToken);

    public Task DeleteCollectionAsync(
        Profile actor,
        string collectionId,
        CancellationToken cancellationToken = default) =>
        _collections.DeleteCollectionAsync(actor, collectionId, cancellationToken);

    public Task AddFeedToCollectionAsync(
        Profile actor,
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default) =>
        _collections.AddFeedToCollectionAsync(actor, collectionId, feedId, cancellationToken);

    public Task RemoveFeedFromCollectionAsync(
        Profile actor,
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default) =>
        _collections.RemoveFeedFromCollectionAsync(actor, collectionId, feedId, cancellationToken);

    public Task DeleteFeedAsync(
        Profile actor,
        string feedId,
        CancellationToken cancellationToken = default) =>
        _feeds.DeleteFeedAsync(actor, feedId, cancellationToken);
}
