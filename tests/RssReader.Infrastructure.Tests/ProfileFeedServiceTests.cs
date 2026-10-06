using System.Text;
using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class ProfileFeedServiceTests
{
    [TestMethod]
    public async Task OpmlImportIsProfileScopedPreservesFoldersAndExportsSubscriptions()
    {
        using var database = new TemporaryDatabase();
        var (profiles, catalog, reader) = await CreateStoresAsync(database.Path);
        var firstProfile = Profile.CreateRegular("First Reader");
        var secondProfile = Profile.CreateRegular("Second Reader");
        await profiles.AddAsync(firstProfile);
        await profiles.AddAsync(secondProfile);
        var service = new ProfileFeedService(catalog, reader);
        const string opml = """
            <opml version="2.0">
              <body>
                <outline text="Technology">
                  <outline text=".NET">
                    <outline type="rss" text="Engineering" title="Engineering"
                             xmlUrl="https://example.com/feed.xml"
                             htmlUrl="https://example.com" />
                    <outline type="rss" text="Engineering duplicate"
                             xmlUrl="https://example.com/feed.xml" />
                    <outline type="rss" text="Unsupported"
                             xmlUrl="file:///example.xml" />
                  </outline>
                </outline>
              </body>
            </opml>
            """;

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(opml));
        var imported = await service.ImportOpmlAsync(firstProfile, stream);

        Assert.AreEqual(1, imported.AddedCount);
        Assert.AreEqual(1, imported.DuplicateCount);
        Assert.AreEqual(1, imported.SkippedCount);
        Assert.AreEqual(0, (await catalog.GetFeedsAsync()).Count);
        Assert.AreEqual(1, (await catalog.GetFeedsForProfileAsync(firstProfile.Id)).Count);
        Assert.AreEqual(0, (await catalog.GetFeedsForProfileAsync(secondProfile.Id)).Count);
        var subscription = (await reader.GetSubscriptionsAsync(firstProfile.Id)).Single();
        Assert.AreEqual("Technology / .NET", subscription.FolderName);
        Assert.AreEqual("Engineering", subscription.FeedName);

        var exported = await service.ExportOpmlAsync(firstProfile.Id);
        await using var exportedStream = new MemoryStream(Encoding.UTF8.GetBytes(exported));
        var exportedFeed = OpmlFeedParser.Parse(exportedStream).Feeds.Single();
        Assert.AreEqual("Technology / .NET", exportedFeed.CategoryName);
        Assert.AreEqual("Engineering", exportedFeed.Name);
        Assert.AreEqual("https://example.com/feed.xml", exportedFeed.FeedUrl);
        Assert.AreEqual("https://example.com/", exportedFeed.WebsiteUrl);

        await using var secondImportStream = new MemoryStream(Encoding.UTF8.GetBytes(exported));
        var secondImport = await service.ImportOpmlAsync(secondProfile, secondImportStream);
        Assert.AreEqual(1, secondImport.AddedCount);
        Assert.AreEqual(1, (await catalog.GetFeedsForProfileAsync(secondProfile.Id)).Count);
        Assert.AreEqual(1, (await reader.GetSubscriptionsAsync(secondProfile.Id)).Count);

        await reader.UnsubscribeAsync(firstProfile.Id, subscription.FeedId);
        Assert.AreEqual(0, (await catalog.GetFeedsForProfileAsync(firstProfile.Id)).Count);
        Assert.AreEqual(1, (await catalog.GetFeedsForProfileAsync(secondProfile.Id)).Count);
    }

    [TestMethod]
    public async Task UnsubscribingLastOwnerDeletesPersonalFeedAndDependentArticleState()
    {
        using var database = new TemporaryDatabase();
        var (profiles, catalog, reader) = await CreateStoresAsync(database.Path);
        var profile = Profile.CreateRegular("Reader");
        await profiles.AddAsync(profile);
        var service = new ProfileFeedService(catalog, reader);
        var feed = await service.AddFeedAsync(profile, "Engineering", "https://example.com/feed.xml");
        var refresh = new FeedRefreshService(
            reader,
            catalog,
            new FixtureFeedDownloader());
        var refreshSummary = await refresh.RefreshProfileAsync(profile.Id);
        Assert.AreEqual(1, refreshSummary.ArticlesFetched);
        var article = (await reader.GetArticlesAsync(profile.Id)).Single().Article;
        await reader.SetArticleReadAsync(profile.Id, article.Id, true);
        await reader.SetArticleSavedAsync(profile.Id, article.Id, true);

        await reader.UnsubscribeAsync(profile.Id, feed.Id);

        Assert.AreEqual(0, (await catalog.GetFeedsForProfileAsync(profile.Id)).Count);
        Assert.AreEqual(0, (await catalog.GetFeedsAsync()).Count);
        Assert.AreEqual(0, (await reader.GetSubscriptionsAsync(profile.Id)).Count);
        Assert.AreEqual(0, (await reader.GetArticlesAsync(profile.Id)).Count);
    }

    [TestMethod]
    public async Task CatalogMasterCannotAddProfileOwnedFeeds()
    {
        using var database = new TemporaryDatabase();
        var (_, catalog, reader) = await CreateStoresAsync(database.Path);
        var master = Profile.CreateCatalogMaster();
        var service = new ProfileFeedService(catalog, reader);

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
            service.AddFeedAsync(master, "Private feed", "https://example.com/feed.xml"));

        Assert.AreEqual(0, (await catalog.GetFeedsAsync()).Count);
    }

    [TestMethod]
    public async Task PersonalFeedCanReuseSharedCatalogUrlWithoutMakingItPrivate()
    {
        using var database = new TemporaryDatabase();
        var (profiles, catalog, reader) = await CreateStoresAsync(database.Path);
        var profile = Profile.CreateRegular("Reader");
        await profiles.AddAsync(profile);
        var sharedFeed = new CatalogFeed(
            "shared-feed",
            "Shared engineering",
            "https://example.com/feed.xml",
            null,
            null);
        await catalog.AddFeedAsync(sharedFeed);

        var feed = await new ProfileFeedService(catalog, reader)
            .AddFeedAsync(profile, "Personal name", sharedFeed.FeedUrl);

        Assert.AreEqual(sharedFeed.Id, feed.Id);
        Assert.AreEqual(1, (await catalog.GetFeedsAsync()).Count);
        Assert.AreEqual(1, (await catalog.GetFeedsForProfileAsync(profile.Id)).Count);
        Assert.AreEqual(1, (await reader.GetSubscriptionsAsync(profile.Id)).Count);
    }

    private static async Task<(SqliteProfileStore Profiles, SqliteCatalogStore Catalog, SqliteReaderStore Reader)>
        CreateStoresAsync(string databasePath)
    {
        var profiles = new SqliteProfileStore(databasePath);
        var catalog = new SqliteCatalogStore(databasePath);
        var reader = new SqliteReaderStore(databasePath);
        await profiles.InitializeAsync();
        await catalog.InitializeAsync();
        await reader.InitializeAsync();
        return (profiles, catalog, reader);
    }

    private sealed class FixtureFeedDownloader : IFeedDownloader
    {
        public Task<IReadOnlyList<DownloadedFeedItem>> DownloadAsync(
            CatalogFeed feed,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DownloadedFeedItem>>(
            [
                new DownloadedFeedItem(
                    "external-1",
                    "Headline",
                    "https://example.com/article",
                    DateTimeOffset.UtcNow,
                    "Summary",
                    null)
            ]);
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rss-reader-profile-feeds-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { Path, $"{Path}-shm", $"{Path}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
