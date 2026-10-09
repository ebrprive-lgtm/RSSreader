using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed class CatalogCollectionMembershipEditor(
    Profile actor,
    CatalogService catalogService,
    Func<IReadOnlyList<CatalogFeedListItem>> getFeeds,
    Func<IReadOnlyList<CatalogCollection>> getCollections,
    Func<IReadOnlyList<CatalogCollectionFilterOption>> getCollectionOptions,
    Action<string> setErrorMessage,
    Func<Task> refresh)
{
    public IReadOnlySet<string> GetCollectionIdsForFeed(string feedId) =>
        getCollectionOptions()
            .Where(option => option.CollectionId is not null && option.FeedIds.Contains(feedId))
            .Select(option => option.CollectionId!)
            .ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> GetFeedIdsForCollection(string collectionId) =>
        getCollectionOptions()
            .FirstOrDefault(option => option.CollectionId == collectionId)?
            .FeedIds
            .ToHashSet(StringComparer.Ordinal)
        ?? new HashSet<string>(StringComparer.Ordinal);

    public async Task<bool> UpdateFeedCollectionsAsync(
        string feedId,
        IReadOnlyCollection<string> collectionIds)
    {
        setErrorMessage(string.Empty);
        var feeds = getFeeds();
        var collections = getCollections();
        if (feeds.All(feed => feed.Id != feedId))
        {
            setErrorMessage("That feed no longer exists in the catalog.");
            return false;
        }

        var requestedCollectionIds = collectionIds.ToHashSet(StringComparer.Ordinal);
        if (requestedCollectionIds.Any(id => collections.All(collection => collection.Id != id)))
        {
            setErrorMessage("One or more selected collections no longer exist.");
            return false;
        }

        var currentCollectionIds = GetCollectionIdsForFeed(feedId);
        var changes = collections
            .Select(collection => new CollectionMembershipChange(
                collection.Id,
                feedId,
                requestedCollectionIds.Contains(collection.Id)))
            .Where(change => currentCollectionIds.Contains(change.CollectionId) != change.IsMember)
            .ToArray();
        return await ApplyChangesAsync(changes);
    }

    public async Task<bool> UpdateCollectionFeedsAsync(
        string collectionId,
        IReadOnlyCollection<string> feedIds)
    {
        setErrorMessage(string.Empty);
        var feeds = getFeeds();
        var collections = getCollections();
        if (collections.All(collection => collection.Id != collectionId))
        {
            setErrorMessage("That collection no longer exists.");
            return false;
        }

        var requestedFeedIds = feedIds.ToHashSet(StringComparer.Ordinal);
        if (requestedFeedIds.Any(id => feeds.All(feed => feed.Id != id)))
        {
            setErrorMessage("One or more selected feeds no longer exist in the catalog.");
            return false;
        }

        var currentFeedIds = GetFeedIdsForCollection(collectionId);
        var changes = feeds
            .Select(feed => new CollectionMembershipChange(
                collectionId,
                feed.Id,
                requestedFeedIds.Contains(feed.Id)))
            .Where(change => currentFeedIds.Contains(change.FeedId) != change.IsMember)
            .ToArray();
        return await ApplyChangesAsync(changes);
    }

    private async Task<bool> ApplyChangesAsync(IReadOnlyCollection<CollectionMembershipChange> changes)
    {
        try
        {
            foreach (var change in changes)
            {
                if (change.IsMember)
                {
                    await catalogService.AddFeedToCollectionAsync(actor, change.CollectionId, change.FeedId);
                }
                else
                {
                    await catalogService.RemoveFeedFromCollectionAsync(actor, change.CollectionId, change.FeedId);
                }
            }
        }
        catch (ArgumentException exception)
        {
            setErrorMessage($"Could not update collection memberships: {exception.Message}");
            await refresh();
            return false;
        }
        catch (InvalidOperationException exception)
        {
            setErrorMessage($"Could not update collection memberships: {exception.Message}");
            await refresh();
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            setErrorMessage($"Could not update collection memberships: {exception.Message}");
            await refresh();
            return false;
        }

        await refresh();
        return true;
    }

    private sealed record CollectionMembershipChange(string CollectionId, string FeedId, bool IsMember);
}
