using RssReader.Domain;

namespace RssReader.Application;

public interface IReaderStore
{
    Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(string profileId, CancellationToken cancellationToken = default);
    Task SubscribeAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default);
    Task UnsubscribeAsync(string profileId, string feedId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetFoldersAsync(string profileId, CancellationToken cancellationToken = default);
    Task AddFolderAsync(string profileId, string name, CancellationToken cancellationToken = default);
    Task DeleteFolderAsync(string profileId, string name, CancellationToken cancellationToken = default);
    Task SetFeedFolderAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(string profileId, CancellationToken cancellationToken = default);
    Task AddFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default);
    Task RemoveFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(string profileId, CancellationToken cancellationToken = default);
    Task<int> SaveArticlesAsync(string feedId, IReadOnlyList<FeedArticle> articles, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(string profileId, CancellationToken cancellationToken = default);
    Task ClearFeedRefreshFailureAsync(string profileId, string feedId, CancellationToken cancellationToken = default);
    Task ClearFeedRefreshFailuresAsync(string profileId, CancellationToken cancellationToken = default);
    Task RecordFeedRefreshAttemptAsync(string profileId, string feedId, DateTimeOffset attemptedAt, CancellationToken cancellationToken = default);
    Task RecordFeedRefreshResultAsync(string profileId, string feedId, DateTimeOffset? successfulAt, string? failure, CancellationToken cancellationToken = default);
    Task SetArticleReadAsync(string profileId, string articleId, bool isRead, CancellationToken cancellationToken = default);
    Task SetArticleSavedAsync(string profileId, string articleId, bool isSaved, CancellationToken cancellationToken = default);
}