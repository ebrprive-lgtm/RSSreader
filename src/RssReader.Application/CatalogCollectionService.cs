using RssReader.Domain;

namespace RssReader.Application;

internal sealed class CatalogCollectionService(ICatalogStore store)
{
    public async Task<CatalogCollection> AddCollectionAsync(
        Profile actor,
        string name,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "collection");
        var collections = await store.GetCollectionsAsync(cancellationToken);
        if (collections.Any(collection => string.Equals(collection.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That collection already exists.");
        }

        var collection = new CatalogCollection(Guid.NewGuid().ToString("N"), normalizedName);
        await store.AddCollectionAsync(collection, cancellationToken);
        return collection;
    }

    public async Task<CatalogCollection> UpdateCollectionAsync(
        Profile actor,
        string collectionId,
        string name,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "collection");
        var collections = await store.GetCollectionsAsync(cancellationToken);
        var existingCollection = collections.FirstOrDefault(collection => collection.Id == collectionId)
            ?? throw new InvalidOperationException("That collection no longer exists.");
        if (collections.Any(collection => collection.Id != collectionId &&
            string.Equals(collection.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That collection already exists.");
        }

        var updatedCollection = existingCollection with { Name = normalizedName };
        await store.UpdateCollectionAsync(updatedCollection, cancellationToken);
        return updatedCollection;
    }

    public async Task DeleteCollectionAsync(
        Profile actor,
        string collectionId,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        await store.DeleteCollectionAsync(collectionId, cancellationToken);
    }

    public async Task AddFeedToCollectionAsync(
        Profile actor,
        string collectionId,
        string feedId,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
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
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        return store.RemoveFeedFromCollectionAsync(collectionId, feedId, cancellationToken);
    }
}
