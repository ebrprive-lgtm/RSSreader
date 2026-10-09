using RssReader.Domain;

namespace RssReader.Application;

internal sealed class CatalogFeedService(ICatalogStore store, IFeedDownloader? feedDownloader)
{
    public async Task<CatalogFeed> AddFeedAsync(
        Profile actor,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken,
        string? websiteUrl)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "feed");
        if (!CatalogServiceRules.TryNormalizeFeedUrl(feedUrl, out var normalizedUrl))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        string? normalizedWebsiteUrl = null;
        if (!string.IsNullOrWhiteSpace(websiteUrl) &&
            !CatalogServiceRules.TryNormalizeFeedUrl(websiteUrl, out normalizedWebsiteUrl))
        {
            throw new ArgumentException("Website URLs must use HTTP or HTTPS.", nameof(websiteUrl));
        }

        var existingFeeds = await store.GetFeedsAsync(cancellationToken);
        if (existingFeeds.Any(feed => string.Equals(feed.FeedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That feed URL is already in the catalog.");
        }

        if (await store.FeedUrlExistsAsync(normalizedUrl, cancellationToken: cancellationToken))
        {
            throw new InvalidOperationException("That feed URL is already used by a profile's personal feed.");
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
            normalizedUrl,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            categoryId,
            normalizedWebsiteUrl);
        await store.AddFeedAsync(feed, cancellationToken);
        return feed;
    }

    public async Task<CatalogFeed> UpdateFeedAsync(
        Profile actor,
        string feedId,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken,
        string? websiteUrl)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        var normalizedName = CatalogServiceRules.RequireName(name, "feed");
        if (!CatalogServiceRules.TryNormalizeFeedUrl(feedUrl, out var normalizedUrl))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        string? normalizedWebsiteUrl = null;
        if (!string.IsNullOrWhiteSpace(websiteUrl) &&
            !CatalogServiceRules.TryNormalizeFeedUrl(websiteUrl, out normalizedWebsiteUrl))
        {
            throw new ArgumentException("Website URLs must use HTTP or HTTPS.", nameof(websiteUrl));
        }

        var existingFeeds = await store.GetFeedsAsync(cancellationToken);
        var existingFeed = existingFeeds.FirstOrDefault(feed => feed.Id == feedId);
        if (existingFeed is null)
        {
            throw new InvalidOperationException("That feed no longer exists in the catalog.");
        }

        if (existingFeeds.Any(feed => feed.Id != feedId &&
            string.Equals(feed.FeedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That feed URL is already in the catalog.");
        }

        if (await store.FeedUrlExistsAsync(normalizedUrl, feedId, cancellationToken))
        {
            throw new InvalidOperationException("That feed URL is already used by another feed.");
        }

        if (categoryId is not null)
        {
            var categories = await store.GetCategoriesAsync(cancellationToken);
            if (categories.All(category => category.Id != categoryId))
            {
                throw new ArgumentException("The selected category does not exist.", nameof(categoryId));
            }
        }

        var updatedFeed = new CatalogFeed(
            feedId,
            normalizedName,
            normalizedUrl,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            categoryId,
            normalizedWebsiteUrl,
            existingFeed.LastHealthCheckedAt,
            existingFeed.LastHealthCheckSucceeded);
        await store.UpdateFeedAsync(updatedFeed, cancellationToken);
        return updatedFeed;
    }

    public async Task<CatalogFeedHealthCheckResult> CheckFeedHealthAsync(
        Profile actor,
        string feedId,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        if (feedDownloader is null)
        {
            throw new InvalidOperationException("Feed health checks are unavailable.");
        }

        var feed = (await store.GetFeedsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedId)
            ?? throw new InvalidOperationException("That feed no longer exists in the catalog.");

        var isSuccessful = false;
        string? errorMessage = null;
        try
        {
            await feedDownloader.DownloadAsync(feed, cancellationToken);
            isSuccessful = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            errorMessage = exception.ToString();
        }

        var checkedAt = DateTimeOffset.UtcNow;
        await store.UpdateFeedAsync(feed with
        {
            LastHealthCheckedAt = checkedAt,
            LastHealthCheckSucceeded = isSuccessful
        }, cancellationToken);
        return new CatalogFeedHealthCheckResult(isSuccessful, checkedAt, errorMessage);
    }

    public async Task<FeedImportSummary> ImportFeedsAsync(
        Profile actor,
        IEnumerable<OpmlFeed> importedFeeds,
        CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        ArgumentNullException.ThrowIfNull(importedFeeds);

        var categories = (await store.GetCategoriesAsync(cancellationToken)).ToList();
        var existingFeedsByUrl = new Dictionary<string, CatalogFeed>(StringComparer.OrdinalIgnoreCase);
        var existingFeedsByName = new Dictionary<string, List<CatalogFeed>>(StringComparer.OrdinalIgnoreCase);
        foreach (var feed in await store.GetFeedsAsync(cancellationToken))
        {
            if (CatalogServiceRules.TryNormalizeFeedUrl(feed.FeedUrl, out var normalizedUrl))
            {
                existingFeedsByUrl.TryAdd(normalizedUrl, feed);
            }

            if (!existingFeedsByName.TryGetValue(feed.Name.Trim(), out var sameNameFeeds))
            {
                sameNameFeeds = [];
                existingFeedsByName.Add(feed.Name.Trim(), sameNameFeeds);
            }

            sameNameFeeds.Add(feed);
        }

        var addedCount = 0;
        var skippedCount = 0;
        var duplicateCandidates = new List<FeedDuplicateCandidate>();
        foreach (var importedFeed in importedFeeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(importedFeed.Name) ||
                !CatalogServiceRules.TryNormalizeFeedUrl(importedFeed.FeedUrl, out var normalizedUrl))
            {
                skippedCount++;
                continue;
            }

            var normalizedName = CatalogServiceRules.RequireName(importedFeed.Name, "feed");
            if (existingFeedsByUrl.TryGetValue(normalizedUrl, out var urlMatch))
            {
                duplicateCandidates.Add(new FeedDuplicateCandidate(
                    FeedDuplicateKind.ExactUrl,
                    normalizedName,
                    normalizedUrl,
                    urlMatch.Name,
                    urlMatch.FeedUrl));
                skippedCount++;
                continue;
            }

            if (await store.FeedUrlExistsAsync(normalizedUrl, cancellationToken: cancellationToken))
            {
                duplicateCandidates.Add(new FeedDuplicateCandidate(
                    FeedDuplicateKind.ExactUrl,
                    normalizedName,
                    normalizedUrl,
                    "Personal profile feed",
                    normalizedUrl));
                skippedCount++;
                continue;
            }

            if (existingFeedsByName.TryGetValue(normalizedName, out var sameNameMatches))
            {
                duplicateCandidates.AddRange(sameNameMatches.Select(match => new FeedDuplicateCandidate(
                    FeedDuplicateKind.SameNameDifferentUrl,
                    normalizedName,
                    normalizedUrl,
                    match.Name,
                    match.FeedUrl)));
            }

            string? categoryId = null;
            if (!string.IsNullOrWhiteSpace(importedFeed.CategoryName))
            {
                var categoryName = importedFeed.CategoryName.Trim();
                var category = categories.FirstOrDefault(item =>
                    string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                {
                    category = new CatalogCategory(
                        Guid.NewGuid().ToString("N"),
                        CatalogServiceRules.RequireName(categoryName, "category"));
                    await store.AddCategoryAsync(category, cancellationToken);
                    categories.Add(category);
                }

                categoryId = category.Id;
            }

            var feed = new CatalogFeed(
                Guid.NewGuid().ToString("N"),
                normalizedName,
                normalizedUrl,
                string.IsNullOrWhiteSpace(importedFeed.Description) ? null : importedFeed.Description.Trim(),
                categoryId,
                CatalogServiceRules.TryNormalizeFeedUrl(importedFeed.WebsiteUrl, out var websiteUrl) ? websiteUrl : null);
            await store.AddFeedAsync(feed, cancellationToken);
            existingFeedsByUrl.Add(normalizedUrl, feed);
            if (!existingFeedsByName.TryGetValue(normalizedName, out sameNameMatches))
            {
                sameNameMatches = [];
                existingFeedsByName.Add(normalizedName, sameNameMatches);
            }

            sameNameMatches.Add(feed);
            addedCount++;
        }

        return new FeedImportSummary(addedCount, skippedCount, duplicateCandidates);
    }

    public async Task DeleteFeedAsync(Profile actor, string feedId, CancellationToken cancellationToken)
    {
        CatalogServiceRules.EnsureCatalogMaster(actor);
        await store.DeleteFeedAsync(feedId, cancellationToken);
    }
}
