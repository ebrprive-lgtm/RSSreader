using System.Xml.Linq;
using RssReader.Domain;

namespace RssReader.Application;

public sealed record ProfileFeedImportSummary(int AddedCount, int DuplicateCount, int SkippedCount);

public sealed class ProfileFeedService(ICatalogStore catalogStore, IReaderStore readerStore)
{
    private const string DefaultFolderName = "Unfiled";

    public async Task<CatalogFeed> AddFeedAsync(
        Profile profile,
        string name,
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A feed name is required.", nameof(name));
        }

        if (!FeedUrlNormalizer.TryNormalize(feedUrl, out var normalizedUrl))
        {
            throw new ArgumentException("Feed URLs must use HTTP or HTTPS.", nameof(feedUrl));
        }

        await EnsureFolderAsync(profile.Id, DefaultFolderName, cancellationToken);
        var existing = await catalogStore.GetFeedsForProfileAsync(profile.Id, cancellationToken);
        var feed = existing.FirstOrDefault(candidate =>
            string.Equals(candidate.FeedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase));
        if (feed is null)
        {
            feed = await catalogStore.AddProfileFeedAsync(
                profile.Id,
                new CatalogFeed(
                    Guid.NewGuid().ToString("N"),
                    name.Trim(),
                    normalizedUrl,
                    null,
                    null),
                cancellationToken);
        }

        var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id, cancellationToken);
        if (subscriptions.All(subscription => subscription.FeedId != feed.Id))
        {
            await readerStore.SubscribeAsync(profile.Id, feed.Id, DefaultFolderName, cancellationToken);
        }

        return feed;
    }

    public async Task<ProfileFeedImportSummary> ImportOpmlAsync(
        Profile profile,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(stream);
        var parsed = OpmlFeedParser.Parse(stream);
        var accessibleFeeds = await catalogStore.GetFeedsForProfileAsync(profile.Id, cancellationToken);
        var feedsByUrl = accessibleFeeds.ToDictionary(feed => feed.FeedUrl, StringComparer.OrdinalIgnoreCase);
        var subscriptions = await readerStore.GetSubscriptionsAsync(profile.Id, cancellationToken);
        var subscribedFeedIds = subscriptions.Select(subscription => subscription.FeedId).ToHashSet(StringComparer.Ordinal);
        var folders = (await readerStore.GetFoldersAsync(profile.Id, cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var duplicateCount = 0;
        var skippedCount = parsed.SkippedCount;

        foreach (var importedFeed in parsed.Feeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FeedUrlNormalizer.TryNormalize(importedFeed.FeedUrl, out var normalizedUrl))
            {
                skippedCount++;
                continue;
            }

            var folderName = NormalizeFolderName(importedFeed.CategoryName);
            if (folders.Add(folderName))
            {
                await readerStore.AddFolderAsync(profile.Id, folderName, cancellationToken);
            }

            if (!feedsByUrl.TryGetValue(normalizedUrl, out var feed))
            {
                feed = await catalogStore.AddProfileFeedAsync(
                    profile.Id,
                    new CatalogFeed(
                        Guid.NewGuid().ToString("N"),
                        RequireFeedName(importedFeed.Name),
                        normalizedUrl,
                        NormalizeOptional(importedFeed.Description),
                        null,
                        NormalizeWebsiteUrl(importedFeed.WebsiteUrl)),
                    cancellationToken);
                feedsByUrl[feed.FeedUrl] = feed;
            }

            if (!subscribedFeedIds.Add(feed.Id))
            {
                duplicateCount++;
                continue;
            }

            await readerStore.SubscribeAsync(profile.Id, feed.Id, folderName, cancellationToken);
            addedCount++;
        }

        return new ProfileFeedImportSummary(addedCount, duplicateCount, skippedCount);
    }

    public async Task<string> ExportOpmlAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await readerStore.GetSubscriptionsAsync(profileId, cancellationToken);
        var root = new XElement("opml",
            new XAttribute("version", "2.0"),
            new XElement("head", new XElement("title", "RSS Reader subscriptions")),
            new XElement("body"));
        var body = root.Element("body")!;
        var folderNodes = new Dictionary<string, XElement>(StringComparer.Ordinal);

        foreach (var subscription in subscriptions.OrderBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.FeedName, StringComparer.OrdinalIgnoreCase))
        {
            var parent = body;
            var path = string.Empty;
            foreach (var segment in subscription.FolderName.Split(
                         " / ",
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                path = path.Length == 0 ? segment : $"{path} / {segment}";
                if (!folderNodes.TryGetValue(path, out var folderNode))
                {
                    folderNode = new XElement("outline",
                        new XAttribute("text", segment),
                        new XAttribute("title", segment));
                    parent.Add(folderNode);
                    folderNodes.Add(path, folderNode);
                }

                parent = folderNode;
            }

            parent.Add(new XElement("outline",
                new XAttribute("type", "rss"),
                new XAttribute("text", subscription.FeedName),
                new XAttribute("title", subscription.FeedName),
                new XAttribute("xmlUrl", subscription.FeedUrl),
                new XAttribute("htmlUrl", subscription.WebsiteUrl ?? string.Empty)));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root)
            .ToString(SaveOptions.DisableFormatting);
    }

    private async Task EnsureFolderAsync(
        string profileId,
        string folderName,
        CancellationToken cancellationToken)
    {
        var folders = await readerStore.GetFoldersAsync(profileId, cancellationToken);
        if (!folders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            await readerStore.AddFolderAsync(profileId, folderName, cancellationToken);
        }
    }

    private static string RequireFeedName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Imported feed" : name.Trim();

    private static string NormalizeFolderName(string? folderName) =>
        string.IsNullOrWhiteSpace(folderName) ||
        string.Equals(folderName.Trim(), "All", StringComparison.OrdinalIgnoreCase)
            ? DefaultFolderName
            : folderName.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeWebsiteUrl(string? value) =>
        FeedUrlNormalizer.TryNormalize(value, out var normalized) ? normalized : null;
}

public static class FeedUrlNormalizer
{
    public static bool TryNormalize(string? feedUrl, out string normalizedUrl)
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
