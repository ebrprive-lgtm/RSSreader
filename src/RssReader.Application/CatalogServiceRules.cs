using RssReader.Domain;

namespace RssReader.Application;

internal static class CatalogServiceRules
{
    public static void EnsureCatalogMaster(Profile actor)
    {
        if (!actor.IsCatalogMaster || actor.Id != Profile.CreateCatalogMaster().Id)
        {
            throw new UnauthorizedAccessException("Only Catalog Master can modify the shared feed catalog.");
        }
    }

    public static string RequireName(string name, string entryType)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A {entryType} name is required.", nameof(name));
        }

        return name.Trim();
    }

    public static bool TryNormalizeFeedUrl(string? feedUrl, out string normalizedUrl) =>
        FeedUrlNormalizer.TryNormalize(feedUrl, out normalizedUrl);
}
