namespace RssReader.Domain;

public sealed record CatalogFeed(
    string Id,
    string Name,
    string FeedUrl,
    string? Description,
    string? CategoryId,
    string? WebsiteUrl = null,
    DateTimeOffset? LastHealthCheckedAt = null,
    bool? LastHealthCheckSucceeded = null);