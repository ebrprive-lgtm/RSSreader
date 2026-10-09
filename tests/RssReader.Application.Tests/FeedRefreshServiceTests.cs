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
        var timeoutFeed = new CatalogFeed("feed-3", "Slow feed", "https://example.com/slow.xml", null, null);
        var invalidXmlFeed = new CatalogFeed("feed-4", "Invalid XML feed", "https://example.com/invalid.xml", null, null);
        var readerStore = new ReaderStoreStub(
        [
            new ProfileSubscription("profile-1", firstFeed.Id, firstFeed.Name, firstFeed.FeedUrl, "Unfiled"),
            new ProfileSubscription("profile-1", secondFeed.Id, secondFeed.Name, secondFeed.FeedUrl, "Unfiled"),
            new ProfileSubscription("profile-1", timeoutFeed.Id, timeoutFeed.Name, timeoutFeed.FeedUrl, "Unfiled"),
            new ProfileSubscription("profile-1", invalidXmlFeed.Id, invalidXmlFeed.Name, invalidXmlFeed.FeedUrl, "Unfiled")
        ]);
        var downloader = new FeedDownloaderStub(new Dictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>>
        {
            [firstFeed.Id] = () =>
            [
                new DownloadedFeedItem(
                    "external-1",
                    "Headline",
                    "https://example.com/story",
                    null,
                    "Summary",
                    "Content",
                    Categories: [new ArticleCategory("Press Releases")],
                    Author: "Example Author")
            ],
            [secondFeed.Id] = () => throw new HttpRequestException("Feed unavailable"),
            [timeoutFeed.Id] = () => throw new TimeoutException("Feed timed out"),
            [invalidXmlFeed.Id] = () => throw new System.Xml.XmlException("Invalid feed XML")
        });
        var service = new FeedRefreshService(
            readerStore,
            new CatalogStoreStub([firstFeed, secondFeed, timeoutFeed, invalidXmlFeed]),
            downloader);

        var firstRefresh = await service.RefreshProfileAsync("profile-1");
        var secondRefresh = await service.RefreshProfileAsync("profile-1");

        Assert.AreEqual(4, firstRefresh.FeedsChecked);
        Assert.AreEqual(1, firstRefresh.ArticlesFetched);
        Assert.AreEqual(1, firstRefresh.ArticlesAdded);
        Assert.AreEqual(3, firstRefresh.Failures.Count);
        StringAssert.Contains(string.Join(" ", firstRefresh.Failures), "Broken feed");
        StringAssert.Contains(string.Join(" ", firstRefresh.Failures), "Slow feed: Feed timed out");
        StringAssert.Contains(string.Join(" ", firstRefresh.Failures), "Invalid XML feed: Invalid feed XML");
        CollectionAssert.AreEquivalent(
            new[] { secondFeed.Id, timeoutFeed.Id, invalidXmlFeed.Id },
            firstRefresh.FailedFeedIds.ToArray());
        Assert.AreEqual(2, readerStore.SavedArticles.Count);
        Assert.AreEqual(0, secondRefresh.ArticlesAdded);
        Assert.AreEqual(1, readerStore.SavedArticles.Select(item => item.Article.Id).Distinct().Count());
        Assert.IsTrue(readerStore.SavedArticles.All(item => item.FeedId == "feed-1" && item.Article.FeedId == "feed-1"));
        Assert.AreEqual("Press Releases", readerStore.SavedArticles.First().Article.Categories.Single().Term);
        Assert.AreEqual("Example Author", readerStore.SavedArticles.First().Article.Author);
        var refreshStates = readerStore.RefreshStates.ToDictionary(state => state.FeedId, StringComparer.Ordinal);
        Assert.IsNotNull(refreshStates[firstFeed.Id].LastSuccessfulAt);
        Assert.IsNull(refreshStates[firstFeed.Id].LastFailure);
        Assert.IsNull(refreshStates[secondFeed.Id].LastSuccessfulAt);
        StringAssert.Contains(refreshStates[secondFeed.Id].LastFailure!, "Feed unavailable");
        StringAssert.Contains(refreshStates[timeoutFeed.Id].LastFailure!, "Feed timed out");
        StringAssert.Contains(refreshStates[invalidXmlFeed.Id].LastFailure!, "Invalid feed XML");
    }

    [TestMethod]
    public async Task RefreshProfilePropagatesCancellation()
    {
        var feed = new CatalogFeed("feed-1", "Example", "https://example.com/feed.xml", null, null);
        var readerStore = new ReaderStoreStub(
        [new ProfileSubscription("profile-1", feed.Id, feed.Name, feed.FeedUrl, "Unfiled")]);
        var service = new FeedRefreshService(
            readerStore,
            new CatalogStoreStub([feed]),
            new FeedDownloaderStub(new Dictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>>
            {
                [feed.Id] = () => [new DownloadedFeedItem("item-1", "Headline", null, null, null, null)]
            }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => service.RefreshProfileAsync("profile-1", cancellation.Token));

        Assert.AreEqual(0, readerStore.SavedArticles.Count);
    }

    [TestMethod]
    public async Task RefreshFeedsOnlyRefreshesRequestedProfileSubscriptions()
    {
        var firstFeed = new CatalogFeed("feed-1", "First feed", "https://example.com/first.xml", null, null);
        var secondFeed = new CatalogFeed("feed-2", "Second feed", "https://example.com/second.xml", null, null);
        var readerStore = new ReaderStoreStub(
        [
            new ProfileSubscription("profile-1", firstFeed.Id, firstFeed.Name, firstFeed.FeedUrl, "News"),
            new ProfileSubscription("profile-1", secondFeed.Id, secondFeed.Name, secondFeed.FeedUrl, "Tech")
        ]);
        var downloader = new FeedDownloaderStub(new Dictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>>
        {
            [firstFeed.Id] = () => [new DownloadedFeedItem("first-item", "First", null, null, null, null)],
            [secondFeed.Id] = () => [new DownloadedFeedItem("second-item", "Second", null, null, null, null)]
        });
        var service = new FeedRefreshService(
            readerStore,
            new CatalogStoreStub([firstFeed, secondFeed]),
            downloader);

        var result = await service.RefreshFeedsAsync("profile-1", [secondFeed.Id]);

        Assert.AreEqual(1, result.FeedsChecked);
        Assert.AreEqual(1, result.ArticlesFetched);
        Assert.AreEqual(secondFeed.Id, readerStore.SavedArticles.Single().FeedId);
    }

    [TestMethod]
    public async Task GetRawArticleContent_RequiresSubscriptionAndPassesArticleIdentity()
    {
        var feed = new CatalogFeed("feed-1", "Example", "https://example.com/feed.xml", null, null);
        var readerStore = new ReaderStoreStub(
        [new ProfileSubscription("profile-1", feed.Id, feed.Name, feed.FeedUrl, "News")]);
        var downloader = new RawFeedDownloaderStub("<item><guid>story-1</guid></item>");
        var service = new FeedRefreshService(readerStore, new CatalogStoreStub([feed]), downloader);

        var rawContent = await service.GetRawArticleContentAsync(
            "profile-1",
            feed.Id,
            "story-1",
            "https://example.com/story-1",
            "Example story");

        Assert.AreEqual("<item><guid>story-1</guid></item>", rawContent);
        Assert.AreEqual("story-1", downloader.ExternalId);
        Assert.AreEqual("https://example.com/story-1", downloader.Link);
        Assert.AreEqual("Example story", downloader.Title);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.GetRawArticleContentAsync(
                "other-profile",
                feed.Id,
                "story-1",
                "https://example.com/story-1",
                "Example story"));
    }

    private sealed class FeedDownloaderStub(
        IReadOnlyDictionary<string, Func<IReadOnlyList<DownloadedFeedItem>>> responses) : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) => Task.FromResult(responses[feed.Id]());
    }

    private sealed class RawFeedDownloaderStub(string rawContent) : IFeedDownloader, IRawFeedContentDownloader
    {
        public string? ExternalId { get; private set; }
        public string? Link { get; private set; }
        public string? Title { get; private set; }

        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadedFeedItem>>([]);

        public Task<string> DownloadRawArticleContentAsync(
            CatalogFeed feed,
            string? externalId,
            string? link,
            string title,
            CancellationToken cancellationToken = default)
        {
            ExternalId = externalId;
            Link = link;
            Title = title;
            return Task.FromResult(rawContent);
        }
    }

    private sealed class CatalogStoreStub(IReadOnlyList<CatalogFeed> feeds) : ICatalogStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CatalogFeed>> GetFeedsAsync(CancellationToken cancellationToken = default) => Task.FromResult(feeds);
        public Task<IReadOnlyList<CatalogCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>([]);
        public Task<IReadOnlyList<CatalogCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CatalogCollection>>([]);
        public Task<IReadOnlyList<string>> GetCollectionFeedIdsAsync(string collectionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetCollectionFeedIdsByCollectionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
        public Task AddFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateFeedAsync(CatalogFeed feed, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteFeedAsync(string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateCategoryAsync(CatalogCategory category, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCategoryAsync(string categoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MergeCategoriesAsync(string sourceCategoryId, string targetCategoryId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateCollectionAsync(CatalogCollection collection, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteCollectionAsync(string collectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AddFeedToCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFeedFromCollectionAsync(string collectionId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ReaderStoreStub(IReadOnlyList<ProfileSubscription> subscriptions) : IReaderStore
    {
        private readonly ConcurrentDictionary<string, byte> _savedIds = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<(string ProfileId, string FeedId), ProfileFeedRefreshState> _refreshStates = new();

        public ConcurrentBag<(string FeedId, FeedArticle Article)> SavedArticles { get; } = [];
        public ProfileFeedRefreshState[] RefreshStates => _refreshStates.Values.ToArray();

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
        public Task<int> SaveArticlesAsync(string feedId, IReadOnlyList<FeedArticle> articles, CancellationToken cancellationToken = default)
        {
            var addedCount = 0;
            foreach (var article in articles)
            {
                SavedArticles.Add((feedId, article));
                if (_savedIds.TryAdd(article.Id, 0))
                {
                    addedCount++;
                }
            }

            return Task.FromResult(addedCount);
        }
        public Task SetArticleReadAsync(string profileId, string articleId, bool isRead, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetArticleSavedAsync(string profileId, string articleId, bool isSaved, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfileFeedRefreshState>>(_refreshStates.Values.Where(state => state.ProfileId == profileId).ToArray());
        public Task ClearFeedRefreshFailureAsync(string profileId, string feedId, CancellationToken cancellationToken = default)
        {
            if (_refreshStates.TryGetValue((profileId, feedId), out var state))
            {
                _refreshStates[(profileId, feedId)] = state with { LastFailure = null };
            }

            return Task.CompletedTask;
        }
        public Task ClearFeedRefreshFailuresAsync(string profileId, CancellationToken cancellationToken = default)
        {
            foreach (var key in _refreshStates.Keys.Where(key => key.ProfileId == profileId))
            {
                if (_refreshStates.TryGetValue(key, out var state))
                {
                    _refreshStates[key] = state with { LastFailure = null };
                }
            }

            return Task.CompletedTask;
        }
        public Task RecordFeedRefreshAttemptAsync(string profileId, string feedId, DateTimeOffset attemptedAt, CancellationToken cancellationToken = default)
        {
            _refreshStates.AddOrUpdate(
                (profileId, feedId),
                _ => new ProfileFeedRefreshState(profileId, feedId, attemptedAt, null, null),
                (_, state) => state with { LastAttemptAt = attemptedAt, LastFailure = null });
            return Task.CompletedTask;
        }
        public Task RecordFeedRefreshResultAsync(string profileId, string feedId, DateTimeOffset? successfulAt, string? failure, CancellationToken cancellationToken = default)
        {
            _refreshStates.AddOrUpdate(
                (profileId, feedId),
                _ => new ProfileFeedRefreshState(profileId, feedId, DateTimeOffset.MinValue, successfulAt, failure),
                (_, state) => state with
                {
                    LastSuccessfulAt = successfulAt ?? state.LastSuccessfulAt,
                    LastFailure = failure
                });
            return Task.CompletedTask;
        }
    }
}