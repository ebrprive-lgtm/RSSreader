namespace RssReader.App.ViewModels;

public sealed record FeedRefreshWarning(string? FeedId, string FeedName, string Message);
