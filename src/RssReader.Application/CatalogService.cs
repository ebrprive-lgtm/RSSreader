using RssReader.Domain;

namespace RssReader.Application;

public sealed record FeedImportSummary(int AddedCount, int SkippedCount);
public sealed record CatalogFeedHealthCheckResult(bool IsSuccessful, DateTimeOffset CheckedAt);

public sealed class CatalogService(ICatalogStore store, IFeedDownloader? feedDownloader = null)
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

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(
        CancellationToken cancellationToken = default) =>
        store.GetCollectionFeedIdsByCollectionAsync(cancellationToken);

    public async Task<CatalogFeed> AddFeedAsync(
        Profile actor,
        string name,
        string feedUrl,
        string? description,
        string? categoryId,
        CancellationToken cancellationToken = default,
        string? websiteUrl = null)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "feed");
        if (!TryNormalizeFeedUrl(feedUrl, out var normalizedUrl))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        string? normalizedWebsiteUrl = null;
        if (!string.IsNullOrWhiteSpace(websiteUrl) && !TryNormalizeFeedUrl(websiteUrl, out normalizedWebsiteUrl))
        {
            throw new ArgumentException("Website URLs must use HTTP or HTTPS.", nameof(websiteUrl));
        }

        var existingFeeds = await store.GetFeedsAsync(cancellationToken);
        if (existingFeeds.Any(feed => string.Equals(feed.FeedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase)))
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
        CancellationToken cancellationToken = default,
        string? websiteUrl = null)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "feed");
        if (!TryNormalizeFeedUrl(feedUrl, out var normalizedUrl))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        string? normalizedWebsiteUrl = null;
        if (!string.IsNullOrWhiteSpace(websiteUrl) && !TryNormalizeFeedUrl(websiteUrl, out normalizedWebsiteUrl))
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
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        if (feedDownloader is null)
        {
            throw new InvalidOperationException("Feed health checks are unavailable.");
        }

        var feed = (await store.GetFeedsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedId)
            ?? throw new InvalidOperationException("That feed no longer exists in the catalog.");

        var isSuccessful = false;
        try
        {
            await feedDownloader.DownloadAsync(feed, cancellationToken);
            isSuccessful = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed check is recorded as feed status rather than aborting catalog maintenance.
        }

        var checkedAt = DateTimeOffset.UtcNow;
        await store.UpdateFeedAsync(feed with
        {
            LastHealthCheckedAt = checkedAt,
            LastHealthCheckSucceeded = isSuccessful
        }, cancellationToken);
        return new CatalogFeedHealthCheckResult(isSuccessful, checkedAt);
    }

    public async Task<FeedImportSummary> ImportFeedsAsync(
        Profile actor,
        IEnumerable<OpmlFeed> importedFeeds,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        ArgumentNullException.ThrowIfNull(importedFeeds);

        var categories = (await store.GetCategoriesAsync(cancellationToken)).ToList();
        var existingUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var feed in await store.GetFeedsAsync(cancellationToken))
        {
            if (TryNormalizeFeedUrl(feed.FeedUrl, out var normalizedUrl))
            {
                existingUrls.Add(normalizedUrl);
            }
        }

        var addedCount = 0;
        var skippedCount = 0;
        foreach (var importedFeed in importedFeeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(importedFeed.Name) ||
                !TryNormalizeFeedUrl(importedFeed.FeedUrl, out var normalizedUrl) ||
                !existingUrls.Add(normalizedUrl))
            {
                skippedCount++;
                continue;
            }

            var normalizedName = RequireName(importedFeed.Name, "feed");
            string? categoryId = null;
            if (!string.IsNullOrWhiteSpace(importedFeed.CategoryName))
            {
                var categoryName = importedFeed.CategoryName.Trim();
                var category = categories.FirstOrDefault(item =>
                    string.Equals(item.Name, categoryName, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                {
                    category = new CatalogCategory(Guid.NewGuid().ToString("N"), RequireName(categoryName, "category"));
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
                TryNormalizeFeedUrl(importedFeed.WebsiteUrl, out var websiteUrl) ? websiteUrl : null);
            await store.AddFeedAsync(feed, cancellationToken);
            addedCount++;
        }

        return new FeedImportSummary(addedCount, skippedCount);
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

    public async Task<CatalogCategory> UpdateCategoryAsync(
        Profile actor,
        string categoryId,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        var normalizedName = RequireName(name, "category");
        var categories = await store.GetCategoriesAsync(cancellationToken);
        var existingCategory = categories.FirstOrDefault(category => category.Id == categoryId)
            ?? throw new InvalidOperationException("That category no longer exists.");
        if (categories.Any(category => category.Id != categoryId &&
            string.Equals(category.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That category already exists.");
        }

        var updatedCategory = existingCategory with { Name = normalizedName };
        await store.UpdateCategoryAsync(updatedCategory, cancellationToken);
        return updatedCategory;
    }

    public async Task MergeCategoriesAsync(
        Profile actor,
        string sourceCategoryId,
        string targetCategoryId,
        CancellationToken cancellationToken = default)
    {
        EnsureCatalogMaster(actor);
        if (string.Equals(sourceCategoryId, targetCategoryId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Choose a different category to merge into.");
        }

        var categories = await store.GetCategoriesAsync(cancellationToken);
        if (categories.All(category => category.Id != sourceCategoryId) ||
            categories.All(category => category.Id != targetCategoryId))
        {
            throw new InvalidOperationException("The source or target category no longer exists.");
        }

        await store.MergeCategoriesAsync(sourceCategoryId, targetCategoryId, cancellationToken);
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

    private static bool TryNormalizeFeedUrl(string? feedUrl, out string normalizedUrl)
    {
        if (Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            normalizedUrl = uri.AbsoluteUri;
            return true;
        }

        normalizedUrl = string.Empty;
        return false;
    }
}