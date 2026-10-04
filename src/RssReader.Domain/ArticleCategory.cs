namespace RssReader.Domain;

public sealed record ArticleCategory(string Term, string? Scheme = null, string? Label = null);