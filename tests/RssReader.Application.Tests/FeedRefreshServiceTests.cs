using System.Collections.Concurrent;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Application.Tests;

[TestClass]
public sealed class FeedRefreshServiceTests
{
    [TestMethod]
    public async Task RefreshIsolatesFeedFailuresAndUsesStableArticleIds()
    {
        var firstFeed = new CatalogFeed("feed-1", "Working feed", "https://example.com/working.xml", null, null);
        var secondFeed = new CatalogFeed("feed-2", "Broken feed", "https://example.com/broken.xml", null, null);
        var readerStore = new ReaderStoreStub(
        [
            new ProfileSubscription("profile-1", firstFeed.Id, firstFeed.Name, firstFeed.FeedUrl, "Unfiled"),
            new ProfileSubscription("profile-1", secondFeed.Id, secondFeed.Name, secondFeed.FeedUrl, "Unfiled")
        ]);
        var downloader = new FeedDownloaderStub(new Dictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>>
        {
            [firstFeed.Id] = () => [new DownloadedFeedItem("external-1", "Headline", "https://example.com/story", null, "Summary", "Content")],
            [secondFeed.Id] = () => throw new HttpRequestException("Feed unavailable")
        });
        var service = new FeedRefreshService(readerStore, new CatalogStoreStub([firstFeed, secondFeed]), downloader);

        var firstRefresh = await service.RefreshProfileAsync("profile-1");
        var secondRefresh = await service.RefreshProfileAsync("profile-1");

        Assert.AreEqual(2, firstRefresh.FeedsChecked);
        Assert.AreEqual(1, firstRefresh.ArticlesFetched);
        Assert.AreEqual(1, firstRefresh.Failures.Count);
        StringAssert.Contains(firstRefresh.Failures.Single(), "Broken feed");
        Assert.AreEqual(2, readerStore.SavedArticles.Count);
        Assert.AreEqual(1, readerStore.SavedArticles.Select(item => item.Article.Id).Distinct().Count());
        Assert.IsTrue(readerStore.SavedArticles.All(item => item.FeedId == "feed-1" && item.Article.FeedId == "feed-1"));
    }

    private sealed class FeedDownloaderStub(
        IReadOnlyDictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>> responses) : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) => Task.FromResult(responses[feed.Id]());
    }

    private sealed class CatalogStoreStub(IReadOnlyList<CatalogFeed> feeds) : ICatalogStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) => Task.FromResult(feeds);
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>([]);
        public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCollection>>([]);
        public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ReaderStoreStub(IReadOnlyList<ProfileSubscription> subscriptions) : IReaderStore
    {
        public ConcurrentBag<(string FeedId, FeedArticle Article)> SavedArticles { get; } = [];

        public Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfileSubscription>>(subscriptions.Where(item => item.ProfileId == profileId).ToArray());
        public Task SubscribeAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UnsubscribeAsync(string profileId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetFoldersAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task AddFolderAsync(string profileId, string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteFolderAsync(string profileId, string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetFeedFolderAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProfileFeedTag>>([]);
        public Task AddFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ArticleForProfile>>([]);
        public Task SaveArticlesAsync(string feedId, IReadOnlyList<FeedArticle> articles, CancellationToken cancellationToken = default)
        {
            foreach (var article in articles)
            {
                SavedArticles.Add((feedId, article));
            }

            return Task.CompletedTask;
        }
        public Task SetArticleReadAsync(string profileId, string articleId, bool isRead, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetArticleSavedAsync(string profileId, string articleId, bool isSaved, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}