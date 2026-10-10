using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using RssReader.Domain;

namespace RssReader.Application;

public sealed class FeedRefreshService(
    IReaderStore readerStore,
    ICatalogStore catalogStore,
    IFeedDownloader feedDownloader)
{
    public async Task<FeedRefreshSummary> RefreshProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await readerStore.GetSubscriptionsAsync(profileId, cancellationToken);
        return await RefreshSubscriptionsAsync(profileId, subscriptions, cancellationToken);
    }

    public async Task<FeedRefreshSummary> RefreshFeedsAsync(
        string profileId,
        IReadOnlyCollection<string> feedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedIds);
        if (feedIds.Count == 0)
        {
            return new FeedRefreshSummary(0, 0, []);
        }

        var requestedFeedIds = feedIds.ToHashSet(StringComparer.Ordinal);
        var subscriptions = await readerStore.GetSubscriptionsAsync(profileId, cancellationToken);
        return await RefreshSubscriptionsAsync(
            profileId,
            subscriptions.Where(subscription => requestedFeedIds.Contains(subscription.FeedId)).ToArray(),
            cancellationToken);
    }

    public async Task<RawArticleContent> GetRawArticleContentAsync(
        string profileId,
        string feedId,
        string? externalId,
        string? link,
        string title,
        string? cachedSourceXml = null,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await readerStore.GetSubscriptionsAsync(profileId, cancellationToken);
        if (!subscriptions.Any(subscription => subscription.FeedId == feedId))
        {
            throw new InvalidOperationException("The feed is not followed by this profile.");
        }

        var feed = (await catalogStore.GetFeedsForProfileAsync(profileId, cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedId)
            ?? throw new InvalidOperationException("The feed is no longer in the catalog.");

        if (feedDownloader is not IRawFeedContentDownloader rawFeedContentDownloader)
        {
            return GetCachedArticleContent(cachedSourceXml)
                ?? throw new InvalidOperationException("Raw feed content is not supported by the configured downloader.");
        }

        try
        {
            var rawContent = await rawFeedContentDownloader.DownloadRawArticleContentAsync(
                feed,
                externalId,
                link,
                title,
                cancellationToken);
            return new RawArticleContent(rawContent, IsCached: false);
        }
        catch (HttpRequestException exception) when (HasCachedSource(cachedSourceXml))
        {
            return LogCachedFallback(cachedSourceXml!, exception);
        }
        catch (InvalidDataException exception) when (HasCachedSource(cachedSourceXml))
        {
            return LogCachedFallback(cachedSourceXml!, exception);
        }
        catch (XmlException exception) when (HasCachedSource(cachedSourceXml))
        {
            return LogCachedFallback(cachedSourceXml!, exception);
        }
        catch (IOException exception) when (HasCachedSource(cachedSourceXml))
        {
            return LogCachedFallback(cachedSourceXml!, exception);
        }
        catch (TaskCanceledException exception) when (
            !cancellationToken.IsCancellationRequested &&
            HasCachedSource(cachedSourceXml))
        {
            return LogCachedFallback(cachedSourceXml!, exception);
        }
    }

    private async Task<FeedRefreshSummary> RefreshSubscriptionsAsync(
        string profileId,
        IReadOnlyList<ProfileSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        var feeds = await catalogStore.GetFeedsForProfileAsync(profileId, cancellationToken);
        var feedLookup = feeds.ToDictionary(feed => feed.Id, StringComparer.Ordinal);
        var failures = new ConcurrentBag<string>();
        var failedFeedIds = new ConcurrentBag<string>();
        var downloadedCount = 0;
        var addedCount = 0;
        var gate = new SemaphoreSlim(4);

        var refreshTasks = subscriptions
            .Where(subscription => feedLookup.ContainsKey(subscription.FeedId))
            .Select(async subscription =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var feed = feedLookup[subscription.FeedId];
                    await readerStore.RecordFeedRefreshAttemptAsync(
                        profileId,
                        feed.Id,
                        DateTimeOffset.UtcNow,
                        cancellationToken);
                    var items = await feedDownloader.DownloadAsync(feed, cancellationToken);
                    var articles = items.Select(item => ToArticle(feed, item)).ToArray();
                    var added = await readerStore.SaveArticlesAsync(feed.Id, articles, cancellationToken);
                    await readerStore.RecordFeedRefreshResultAsync(
                        profileId,
                        feed.Id,
                        DateTimeOffset.UtcNow,
                        null,
                        CancellationToken.None);
                    Interlocked.Add(ref downloadedCount, articles.Length);
                    Interlocked.Add(ref addedCount, added);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await readerStore.RecordFeedRefreshResultAsync(
                        profileId,
                        subscription.FeedId,
                        null,
                        null,
                        CancellationToken.None);
                    throw;
                }
                catch (Exception exception)
                {
                    var failure = $"{subscription.FeedName}: {exception.Message}";
                    failures.Add(failure);
                    failedFeedIds.Add(subscription.FeedId);
                    await readerStore.RecordFeedRefreshResultAsync(
                        profileId,
                        subscription.FeedId,
                        null,
                        failure,
                        CancellationToken.None);
                }
                finally
                {
                    gate.Release();
                }
            });

        await Task.WhenAll(refreshTasks);
        return new FeedRefreshSummary(subscriptions.Count, downloadedCount, failures.ToArray())
        {
            ArticlesAdded = addedCount,
            FailedFeedIds = failedFeedIds.ToArray()
        };
    }

    private static FeedArticle ToArticle(CatalogFeed feed, DownloadedFeedItem item)
    {
        var externalId = string.IsNullOrWhiteSpace(item.ExternalId)
            ? item.Link ?? $"{item.Title}|{item.PublishedAt:O}"
            : item.ExternalId.Trim();
        var stableId = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{feed.Id}\n{externalId}")));

        return new FeedArticle(
            stableId,
            feed.Id,
            externalId,
            string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title.Trim(),
            item.Link,
            item.PublishedAt,
            item.Summary,
            item.Content,
            item.ImageUrl)
        {
            Categories = item.Categories ?? [],
            Author = item.Author,
            SourceXml = item.SourceXml
        };
    }

    private static bool HasCachedSource(string? cachedSourceXml) =>
        !string.IsNullOrWhiteSpace(cachedSourceXml);

    private static RawArticleContent? GetCachedArticleContent(string? cachedSourceXml) =>
        HasCachedSource(cachedSourceXml)
            ? new RawArticleContent(cachedSourceXml!, IsCached: true)
            : null;

    private static RawArticleContent LogCachedFallback(string cachedSourceXml, Exception exception)
    {
        Trace.TraceWarning($"The live article XML could not be loaded; using its cached copy. {exception.Message}");
        return new RawArticleContent(cachedSourceXml, IsCached: true);
    }
}

public sealed record FeedRefreshSummary(int FeedsChecked, int ArticlesFetched, IReadOnlyList<string> Failures)
{
    public int ArticlesAdded { get; init; }
    public IReadOnlyList<string> FailedFeedIds { get; init; } = [];
}