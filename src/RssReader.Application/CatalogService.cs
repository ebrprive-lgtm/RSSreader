using RssReader.Domain;

namespace RssReader.Application;

public sealed class CatalogService(ICatalogStore store)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) =>
        store.GetFeedsAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        store.GetCategoriesAsync(cancellationToken);

    public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) =>
        store.GetCollectionsAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(
        string collectionId,
        CancellationToken cancellationToken = default) =>
        store.GetCollectionFeedIdsAsync(collectionId, cancellationToken);

    public async Task<CatalogFeed> AddFeedAsync(
        Profile actor,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "feed");
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        var existingFeeds = await store.GetFeedsAsync(cancellationToken);
        if (existingFeeds.Any(feed => string.Equals(feed.FeedUrl, uri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That feed URL is already in the catalog.");
        }

        if (categoryId is not null)
        {
            var categories = await store.GetCategoriesAsync(cancellationToken);
            if (categories.All(category => category.Id != categoryId))
            {
                throw new ArgumentException("The selected category does not exist.", nameof(categoryId));
            }
        }

        var feed = new CatalogFeed(
            Guid.NewGuid().ToString("N"),
            normalizedName,
            uri.AbsoluteUri,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            categoryId);
        await store.AddFeedAsync(feed, cancellationToken);
        return feed;
    }

    public async Task<CatalogCategory> AddCategoryAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "category");
        var categories = await store.GetCategoriesAsync(cancellationToken);
        if (categories.Any(category => string.Equals(category.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That category already exists.");
        }

        var category = new CatalogCategory(Guid.NewGuid().ToString("N"), normalizedName);
        await store.AddCategoryAsync(category, cancellationToken);
        return category;
    }

    public async Task<CatalogCollection> AddCollectionAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "collection");
        var collections = await store.GetCollectionsAsync(cancellationToken);
        if (collections.Any(collection => string.Equals(collection.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That collection already exists.");
        }

        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), normalizedName);
        await store.AddCollectionAsync(collection, cancellationToken);
        return collection;
    }

    public async Task DeleteFeedAsync(Profile actor, string feedId, CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        await store.DeleteFeedAsync(feedId, cancellationToken);
    }

    public async Task DeleteCategoryAsync(Profile actor, string categoryId, CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        await store.DeleteCategoryAsync(categoryId, cancellationToken);
    }

    public async Task DeleteCollectionAsync(Profile actor, string collectionId, CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        await store.DeleteCollectionAsync(collectionId, cancellationToken);
    }

    public async Task AddFeedToCollectionAsync(
        Profile actor,
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        var collections = await store.GetCollectionsAsync(cancellationToken);
        var feeds = await store.GetFeedsAsync(cancellationToken);
        if (collections.All(collection => collection.Id != collectionId) || feeds.All(feed => feed.Id != feedId))
        {
            throw new ArgumentException("The collection or feed does not exist.");
        }

        await store.AddFeedToCollectionAsync(collectionId, feedId, cancellationToken);
    }

    public Task RemoveFeedFromCollectionAsync(
        Profile actor,
        string collectionId,
        string feedId,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        return store.RemoveFeedFromCollectionAsync(collectionId, feedId, cancellationToken);
    }

    private static void EnsureCatalogMaster(Profile actor)
    {
        if (!actor.IsCatalogMaster || actor.Id != Profile.CreateCatalogMaster().Id)
        {
            throw new UnauthorizedAccessException("Only Catalog Master can modify the shared feed catalog.");
        }
    }

    private static string RequireName(string name, string entryType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A {entryType} name is required.", nameof(name));
        }

        return name.Trim();
    }
}