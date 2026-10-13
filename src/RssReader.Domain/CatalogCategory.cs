namespace RssReader.Domain;

public sealed record CatalogCategory(string Id, string Name, bool IsEnabled = true);