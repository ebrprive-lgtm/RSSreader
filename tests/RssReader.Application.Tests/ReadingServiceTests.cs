using System.Diagnostics;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Application.Tests;

[TestClass]
public sealed class ReadingServiceTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task GetArticlesCombinesProfileFolderTagReadLaterAndSearchFilters()
    {
        var profile = Profile.CreateRegular("Reader");
        var feed = new CatalogFeed("feed-1", "Gaming News", "https://example.com/feed.xml", null, null);
        var readerStore = new MemoryReaderStore();
        readerStore.Subscriptions.Add(new ProfileSubscription(profile.Id, feed.Id, feed.Name, feed.FeedUrl, "Gaming"));
        readerStore.Tags.Add(new ProfileFeedTag(profile.Id, feed.Id, "Reviews"));
        readerStore.Articles.Add(new ArticleForProfile(
            new FeedArticle("article-1", feed.Id, "item-1", "Handheld review", null, DateTimeOffset.UtcNow, "Portable gaming hardware", null),
            feed.Name,
            "Gaming",
            false,
            true));
        readerStore.Articles.Add(new ArticleForProfile(
            new FeedArticle("article-2", feed.Id, "item-2", "Console update", null, DateTimeOffset.UtcNow, "System changes", null),
            feed.Name,
            "Gaming",
            true,
            false));
        var service = new ReadingService(readerStore, new MemoryCatalogStore([feed]));

        var result = await service.GetArticlesAsync(profile.Id, new ArticleFilter(
            FolderName: "Gaming",
            TagName: "Reviews",
            IsRead: false,
            IsSaved: true,
            SearchText: "portable"));

        Assert.AreEqual("article-1", result.Single().Article.Id);
    }

    [TestMethod]
    public async Task GetArticlesFiltersPublisherTopicsSeparatelyFromProfileFeedTags()
    {
        var profile = Profile.CreateRegular("Reader");
        var feed = new CatalogFeed("feed-1", "Press Desk", "https://example.com/feed.xml", null, null);
        var readerStore = new MemoryReaderStore();
        readerStore.Subscriptions.Add(new ProfileSubscription(profile.Id, feed.Id, feed.Name, feed.FeedUrl, "News"));
        readerStore.Tags.Add(new ProfileFeedTag(profile.Id, feed.Id, "Reviews"));
        readerStore.Tags.Add(new ProfileFeedTag(profile.Id, feed.Id, "Press Releases"));
        readerStore.Articles.Add(new ArticleForProfile(
            new FeedArticle("article-1", feed.Id, "item-1", "Official update", null, DateTimeOffset.UtcNow, null, null)
            {
                Categories = [new ArticleCategory("Press Releases", "https://example.com/topics")]
            },
            feed.Name,
            "News",
            false,
            false));
        readerStore.Articles.Add(new ArticleForProfile(
            new FeedArticle("article-2", feed.Id, "item-2", "Review", null, DateTimeOffset.UtcNow, null, null),
            feed.Name,
            "News",
            false,
            false));
        var service = new ReadingService(readerStore, new MemoryCatalogStore([feed]));

        var result = await service.GetArticlesAsync(profile.Id, new ArticleFilter(
            TagName: "Reviews",
            Topic: new ArticleCategory("press releases", "https://example.com/topics")));

        Assert.AreEqual("article-1", result.Single().Article.Id);
    }

    [DataTestMethod]
    [DataRow(575)]
    [DataRow(5000)]
    [DataRow(25000)]
    public async Task GetArticlesSearchesGeneratedLargeCollections(int articleCount)
    {
        var profile = Profile.CreateRegular("Large Library Reader");
        var feed = new CatalogFeed("feed-1", "Generated News", "https://example.com/feed.xml", null, null);
        var readerStore = new MemoryReaderStore();
        readerStore.Subscriptions.Add(new ProfileSubscription(profile.Id, feed.Id, feed.Name, feed.FeedUrl, "News"));
        for (var articleIndex = 0; articleIndex < articleCount; articleIndex++)
        {
            var isMatch = articleIndex == articleCount - 1;
            var articleId = $"article-{articleIndex:D5}";
            readerStore.Articles.Add(new ArticleForProfile(
                new FeedArticle(
                    articleId,
                    feed.Id,
                    $"item-{articleIndex:D5}",
                    isMatch ? $"Target article {articleIndex:D5}" : $"Generated article {articleIndex:D5}",
                    null,
                    DateTimeOffset.UnixEpoch.AddMinutes(articleIndex),
                    $"Summary {articleIndex:D5}",
                    null),
                feed.Name,
                "News",
                false,
                false));
        }

        var service = new ReadingService(readerStore, new MemoryCatalogStore([feed]));
        var timer = Stopwatch.StartNew();
        var results = await service.GetArticlesAsync(
            profile.Id,
            new ArticleFilter(FolderName: "News", SearchText: "target article"));
        timer.Stop();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual($"article-{articleCount - 1:D5}", results[0].Article.Id);
        TestContext.WriteLine(
            $"Article count {articleCount:N0}: query and filter {timer.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [TestMethod]
    public async Task SubscribeRequiresExistingCatalogFeedAndSupportsCatalogMaster()
    {
        var catalogFeed = new CatalogFeed("feed-1", "Example", "https://example.com/feed.xml", null, null);
        var readerStore = new MemoryReaderStore();
        var service = new ReadingService(readerStore, new MemoryCatalogStore([catalogFeed]));
        var profile = Profile.CreateRegular("Reader");

        await service.SubscribeAsync(profile, catalogFeed.Id, "Gaming");

        Assert.AreEqual(profile.Id, readerStore.Subscriptions.Single().ProfileId);
        Assert.AreEqual("Gaming", readerStore.Subscriptions.Single().FolderName);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.SubscribeAsync(profile, "unknown-feed", "Gaming"));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.SubscribeAsync(profile, catalogFeed.Id, "Missing"));

        var catalogMaster = Profile.CreateCatalogMaster();
        await service.AddFolderAsync(catalogMaster, "Robotics");
        await service.SubscribeAsync(catalogMaster, catalogFeed.Id, "Robotics");

        var masterSubscription = readerStore.Subscriptions.Single(subscription => subscription.ProfileId == catalogMaster.Id);
        Assert.AreEqual("Robotics", masterSubscription.FolderName);
    }

    private sealed class MemoryCatalogStore(IReadOnlyList<CatalogFeed> feeds) : ICatalogStore
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

    private sealed class MemoryReaderStore : IReaderStore
    {
        public List<string> Folders { get; } = ["Gaming"];
        public List<ProfileSubscription> Subscriptions { get; } = [];
        public List<ProfileFeedTag> Tags { get; } = [];
        public List<ArticleForProfile> Articles { get; } = [];

        public Task<IReadOnlyList<ProfileSubscription>> GetSubscriptionsAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfileSubscription>>(Subscriptions.Where(item => item.ProfileId == profileId).ToArray());
        public Task SubscribeAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default) =>
            Task.Run(() => Subscriptions.Add(new ProfileSubscription(profileId, feedId, "Gaming News", "https://example.com/feed.xml", folderName)), cancellationToken);
        public Task UnsubscribeAsync(string profileId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> GetFoldersAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Folders);
        public Task AddFolderAsync(string profileId, string name, CancellationToken cancellationToken = default)
        {
            Folders.Add(name);
            return Task.CompletedTask;
        }
        public Task DeleteFolderAsync(string profileId, string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetFeedFolderAsync(string profileId, string feedId, string folderName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProfileFeedTag>> GetFeedTagsAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfileFeedTag>>(Tags.Where(item => item.ProfileId == profileId).ToArray());
        public Task AddFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFeedTagAsync(string profileId, string feedId, string tagName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ArticleForProfile>> GetArticlesAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ArticleForProfile>>(Articles.Where(item => Subscriptions.Any(subscription =>
                subscription.ProfileId == profileId && subscription.FeedId == item.Article.FeedId)).ToArray());
        public Task<int> SaveArticlesAsync(string feedId, IReadOnlyList<FeedArticle> articles, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task SetArticleReadAsync(string profileId, string articleId, bool isRead, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetArticleSavedAsync(string profileId, string articleId, bool isSaved, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ProfileFeedRefreshState>> GetFeedRefreshStatesAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfileFeedRefreshState>>([]);
        public Task ClearFeedRefreshFailureAsync(string profileId, string feedId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearFeedRefreshFailuresAsync(string profileId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordFeedRefreshAttemptAsync(string profileId, string feedId, DateTimeOffset attemptedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordFeedRefreshResultAsync(string profileId, string feedId, DateTimeOffset? successfulAt, string? failure, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}