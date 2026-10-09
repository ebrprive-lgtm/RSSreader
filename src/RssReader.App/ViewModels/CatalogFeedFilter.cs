using RssReader.Domain;

namespace RssReader.App.ViewModels;

internal static class CatalogFeedFilter
{
    public static bool IsVisible(
        CatalogFeedListItem feed,
        string searchQuery,
        bool showMetadataGapsOnly,
        string? categoryId,
        CatalogCollectionFilterOption? collectionFilter,
        CatalogFeedHealthFilter healthFilter)
    {
        if ((showMetadataGapsOnly && !feed.HasMetadataGaps) ||
            (categoryId is not null && feed.CategoryId != categoryId) ||
            (collectionFilter?.CollectionId is not null && !collectionFilter.FeedIds.Contains(feed.Id)))
        {
            return false;
        }

        if ((healthFilter == CatalogFeedHealthFilter.NotChecked && feed.LastHealthCheckedAt is not null) ||
            (healthFilter == CatalogFeedHealthFilter.Succeeded && feed.LastHealthCheckSucceeded != true) ||
            (healthFilter == CatalogFeedHealthFilter.Failed &&
             (feed.LastHealthCheckedAt is null || feed.LastHealthCheckSucceeded != false)))
        {
            return false;
        }

        var searchTerms = searchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (searchTerms.Length == 0)
        {
            return true;
        }

        var searchableText = string.Join(" ", new[]
        {
            feed.Name,
            feed.Description,
            feed.CategoryName,
            feed.FeedUrl,
            feed.WebsiteUrl
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return searchTerms.All(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
