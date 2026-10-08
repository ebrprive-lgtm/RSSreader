using RssReader.Domain;

namespace RssReader.Application;

public interface ICatalogStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogFeed>> GetFeedsForProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        GetFeedsAsync(cancellationToken);
    Task<CatalogFeed> AddProfileFeedAsync(
        string profileId,
        CatalogFeed feed,
        CancellationToken cancellationToken = default) =>
        Task.FromException<CatalogFeed>(new NotSupportedException("Profile-owned feeds are not supported by this catalog store."));
    Task<bool> FeedUrlExistsAsync(
        string feedUrl,
        string? exceptFeedId = null,
        CancellationToken cancellationToken = default) =>
        FeedUrlExistsCoreAsync(this, feedUrl, exceptFeedId, cancellationToken);
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(
        CancellationToken cancellationToken = default);
    Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
    Task UpdateFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
    Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default);
    Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default);
    Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default);
    Task MergeCategoriesAsync(string sourceCategoryId, string targetCategoryId, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default);
    Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default);
    Task UpdateCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default);
    Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default);
    Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default);

    private static async Task<bool> FeedUrlExistsCoreAsync(
        ICatalogStore store,
        string feedUrl,
        string? exceptFeedId,
        CancellationToken cancellationToken)
    {
        var feeds = await store.GetFeedsAsync(cancellationToken);
        return feeds.Any(feed =>
            feed.Id != exceptFeedId &&
            string.Equals(feed.FeedUrl, feedUrl, StringComparison.OrdinalIgnoreCase));
    }
}