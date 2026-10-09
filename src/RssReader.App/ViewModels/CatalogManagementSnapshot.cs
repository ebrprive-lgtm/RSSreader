using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal sealed record CatalogManagementSnapshot(
    IReadOnlyList<CatalogFeedListItem> Feeds,
    IReadOnlyList<CatalogCategoryFilterOption> CategoryFilterOptions,
    IReadOnlyList<CatalogCollectionFilterOption> CollectionFilterOptions,
    IReadOnlyList<CatalogCollection> Collections)
{
    public static CatalogManagementSnapshot Create(
        IReadOnlyList<CatalogFeed> feeds,
        IReadOnlyList<CatalogCategory> categories,
        IReadOnlyList<CatalogCollection> collections,
        IReadOnlyDictionary<string, IReadOnlyList<string>> collectionFeedIds)
    {
        var categoryNames = categories.ToDictionary(category => category.Id, category => category.Name);
        var sameNameCounts = feeds
            .GroupBy(feed => feed.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var categoryFeedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var feed in feeds)
        {
            if (feed.CategoryId is { } categoryId)
            {
                categoryFeedCounts[categoryId] = categoryFeedCounts.GetValueOrDefault(categoryId) + 1;
            }
        }

        var feedItems = feeds.Select(feed => new CatalogFeedListItem(
            feed.Id,
            feed.Name,
            feed.FeedUrl,
            feed.Description,
            feed.CategoryId is not null && categoryNames.TryGetValue(feed.CategoryId, out var categoryName)
                ? categoryName
                : null,
            feed.CategoryId,
            feed.WebsiteUrl,
            sameNameCounts[feed.Name],
            feed.LastHealthCheckedAt,
            feed.LastHealthCheckSucceeded)).ToArray();

        var categoryOptions = new List<CatalogCategoryFilterOption>
        {
            new(null, $"All categories ({feeds.Count})")
        };
        categoryOptions.AddRange(categories
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .Select(category => new CatalogCategoryFilterOption(
                category.Id,
                $"{category.Name} ({categoryFeedCounts.GetValueOrDefault(category.Id)})")));

        var collectionOptions = new List<CatalogCollectionFilterOption>
        {
            new(null, $"All collections ({feeds.Count})", new HashSet<string>(StringComparer.Ordinal))
        };
        var orderedCollections = collections.OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        collectionOptions.AddRange(orderedCollections.Select(collection =>
        {
            var feedIdSet = collectionFeedIds.TryGetValue(collection.Id, out var feedIds)
                ? feedIds.ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            return new CatalogCollectionFilterOption(
                collection.Id,
                $"{collection.Name} ({feedIdSet.Count})",
                feedIdSet);
        }));

        return new CatalogManagementSnapshot(feedItems, categoryOptions, collectionOptions, orderedCollections);
    }
}
