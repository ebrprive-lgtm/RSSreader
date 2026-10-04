using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
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
        return await RefreshSubscriptionsAsync(subscriptions, cancellationToken);
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
            subscriptions.Where(subscription => requestedFeedIds.Contains(subscription.FeedId)).ToArray(),
            cancellationToken);
    }

    public async Task<string> GetRawArticleContentAsync(
        string profileId,
        string feedId,
        string? externalId,
        string? link,
        string title,
        CancellationToken cancellationToken = default)
    {
        if (feedDownloader is not IRawFeedContentDownloader rawFeedContentDownloader)
        {
            throw new InvalidOperationException("Raw feed content is not supported by the configured downloader.");
        }

        var subscriptions = await readerStore.GetSubscriptionsAsync(profileId, cancellationToken);
        if (!subscriptions.Any(subscription => subscription.FeedId == feedId))
        {
            throw new InvalidOperationException("The feed is not followed by this profile.");
        }

        var feed = (await catalogStore.GetFeedsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id == feedId)
            ?? throw new InvalidOperationException("The feed is no longer in the catalog.");
        return await rawFeedContentDownloader.DownloadRawArticleContentAsync(
            feed,
            externalId,
            link,
            title,
            cancellationToken);
    }

    private async Task<FeedRefreshSummary> RefreshSubscriptionsAsync(
        IReadOnlyList<ProfileSubscription> subscriptions,
        CancellationToken cancellationToken)
    {
        var feeds = await catalogStore.GetFeedsAsync(cancellationToken);
        var feedLookup = feeds.ToDictionary(feed => feed.Id, StringComparer.Ordinal);
        var failures = new ConcurrentBag<string>();
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
                    var items = await feedDownloader.DownloadAsync(feed, cancellationToken);
                    var articles = items.Select(item => ToArticle(feed, item)).ToArray();
                    var added = await readerStore.SaveArticlesAsync(feed.Id, articles, cancellationToken);
                    Interlocked.Add(ref downloadedCount, articles.Length);
                    Interlocked.Add(ref addedCount, added);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failures.Add($"{subscription.FeedName}: {exception.Message}");
                }
                finally
                {
                    gate.Release();
                }
            });

        await Task.WhenAll(refreshTasks);
        return new FeedRefreshSummary(subscriptions.Count, downloadedCount, failures.ToArray())
        {
            ArticlesAdded = addedCount
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
            Author = item.Author
        };
    }
}

public sealed record FeedRefreshSummary(int FeedsChecked, int ArticlesFetched, IReadOnlyList<string> Failures)
{
    public int ArticlesAdded { get; init; }
}