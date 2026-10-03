using RssReader.Domain;

namespace RssReader.Application;

public interface ICatalogStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default);
    Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default);
    Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default);
    Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default);
    Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default);
    Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default);
    Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default);
    Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default);
    Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default);
}