using RssReader.Application;

namespace RssReader.App.ViewModels;

public sealed record CatalogFeedPreview(
	string Name,
	string? Category,
	string? Description,
	IReadOnlyList<DownloadedFeedItem> Items);