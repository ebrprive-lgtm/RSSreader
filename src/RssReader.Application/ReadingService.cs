using RssReader.Domain;

namespace RssReader.Application;

public sealed class ReadingService(IReaderStore store, ICatalogStore catalogStore)
{
    public async Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        await store.GetSubscriptionsAsync(profileId, cancellationToken);

    public Task<IReadOnlyList<string>> GetFoldersAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        store.GetFoldersAsync(profileId, cancellationToken);

    public Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        store.GetFeedTagsAsync(profileId, cancellationToken);

    public Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        store.GetFeedRefreshStatesAsync(profileId, cancellationToken);

    public Task ClearFeedRefreshFailureAsync(
        Profile profile,
        string feedId,
        CancellationToken cancellationToken = default) =>
        store.ClearFeedRefreshFailureAsync(profile.Id, feedId, cancellationToken);

    public Task ClearFeedRefreshFailuresAsync(
        Profile profile,
        CancellationToken cancellationToken = default) =>
        store.ClearFeedRefreshFailuresAsync(profile.Id, cancellationToken);

    public async Task SubscribeAsync(
        Profile profile,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        var normalizedFolderName = RequireLabel(folderName, nameof(folderName));
        var feeds = await catalogStore.GetFeedsForProfileAsync(profile.Id, cancellationToken);
        if (feeds.All(feed => feed.Id != feedId))
        {
            throw new ArgumentException("The feed does not exist in the catalog.", nameof(feedId));
        }

        var folders = await store.GetFoldersAsync(profile.Id, cancellationToken);
        var existingFolder = folders.FirstOrDefault(folder =>
            string.Equals(folder, normalizedFolderName, StringComparison.OrdinalIgnoreCase));
        if (existingFolder is null)
        {
            throw new ArgumentException("The folder does not exist in this profile.", nameof(folderName));
        }

        await store.SubscribeAsync(profile.Id, feedId, existingFolder, cancellationToken);
    }

    public Task UnsubscribeAsync(Profile profile, string feedId, CancellationToken cancellationToken = default)
    {
        return store.UnsubscribeAsync(profile.Id, feedId, cancellationToken);
    }

    public async Task AddFolderAsync(Profile profile, string name, CancellationToken cancellationToken = default)
    {
        var normalized = RequireLabel(name, nameof(name));
        if (string.Equals(normalized, "All", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("All is reserved for the aggregate feed view.", nameof(name));
        }

        await store.AddFolderAsync(profile.Id, normalized, cancellationToken);
    }

    public async Task DeleteFolderAsync(Profile profile, string name, CancellationToken cancellationToken = default)
    {
        await store.DeleteFolderAsync(profile.Id, name, cancellationToken);
    }

    public async Task SetFeedFolderAsync(
        Profile profile,
        string feedId,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await store.GetSubscriptionsAsync(profile.Id, cancellationToken);
        if (subscriptions.All(subscription => subscription.FeedId != feedId))
        {
            throw new ArgumentException("The feed is not subscribed in this profile.", nameof(feedId));
        }

        var normalizedFolderName = RequireLabel(folderName, nameof(folderName));
        var folders = await store.GetFoldersAsync(profile.Id, cancellationToken);
        var existingFolder = folders.FirstOrDefault(folder =>
            string.Equals(folder, normalizedFolderName, StringComparison.OrdinalIgnoreCase));
        if (existingFolder is null)
        {
            throw new ArgumentException("The folder does not exist in this profile.", nameof(folderName));
        }

        await store.SetFeedFolderAsync(profile.Id, feedId, existingFolder, cancellationToken);
    }

    public async Task AddFeedTagAsync(
        Profile profile,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await store.GetSubscriptionsAsync(profile.Id, cancellationToken);
        if (subscriptions.All(subscription => subscription.FeedId != feedId))
        {
            throw new ArgumentException("The feed is not subscribed in this profile.", nameof(feedId));
        }

        await store.AddFeedTagAsync(profile.Id, feedId, RequireLabel(tagName, nameof(tagName)), cancellationToken);
    }

    public Task RemoveFeedTagAsync(
        Profile profile,
        string feedId,
        string tagName,
        CancellationToken cancellationToken = default)
    {
        return store.RemoveFeedTagAsync(profile.Id, feedId, tagName, cancellationToken);
    }

    public async Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(
        string profileId,
        ArticleFilter filter,
        CancellationToken cancellationToken = default)
    {
        var articles = await store.GetArticlesAsync(profileId, cancellationToken);
        var subscriptions = await store.GetSubscriptionsAsync(profileId, cancellationToken);
        var subscriptionLookup = subscriptions.ToDictionary(subscription => subscription.FeedId);
        var tags = await store.GetFeedTagsAsync(profileId, cancellationToken);
        var taggedFeedIds = filter.TagName is null
            ? null
            : tags.Where(tag => string.Equals(tag.Name, filter.TagName, StringComparison.OrdinalIgnoreCase))
                .Select(tag => tag.FeedId)
                .ToHashSet(StringComparer.Ordinal);
        var query = filter.SearchText?.Trim();

        return articles
            .Where(item => filter.FeedId is null || item.Article.FeedId == filter.FeedId)
            .Where(item => filter.Topic is null || item.Article.Categories.Any(category =>
                string.Equals(category.Term, filter.Topic.Term, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(category.Scheme, filter.Topic.Scheme, StringComparison.OrdinalIgnoreCase)))
            .Where(item => filter.FolderName is null ||
                (subscriptionLookup.TryGetValue(item.Article.FeedId, out var subscription) &&
                 string.Equals(subscription.FolderName, filter.FolderName, StringComparison.OrdinalIgnoreCase)))
            .Where(item => taggedFeedIds is null || taggedFeedIds.Contains(item.Article.FeedId))
            .Where(item => filter.IsRead is null || item.IsRead == filter.IsRead)
            .Where(item => filter.IsSaved is null || item.IsSaved == filter.IsSaved)
            .Where(item => string.IsNullOrEmpty(query) ||
                item.Article.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Source.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (item.Article.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (item.Article.Content?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(item => item.Article.PublishedAt)
            .ToArray();
    }

    public Task MarkReadAsync(Profile profile, string articleId, bool isRead, CancellationToken cancellationToken = default)
    {
        return store.SetArticleReadAsync(profile.Id, articleId, isRead, cancellationToken);
    }

    public Task SetSavedAsync(Profile profile, string articleId, bool isSaved, CancellationToken cancellationToken = default)
    {
        return store.SetArticleSavedAsync(profile.Id, articleId, isSaved, cancellationToken);
    }

    private static string RequireLabel(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A name is required.", parameterName);
        }

        return value.Trim();
    }
}