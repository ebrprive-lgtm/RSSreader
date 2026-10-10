using System.Net;

namespace RssReader.Application;

public sealed record ExtractedArticleContent(
    string Html,
    string? Title,
    string? Author,
    DateTimeOffset RetrievedAt);

public interface IArticleContentService
{
    Task<ExtractedArticleContent> ExtractAsync(
        Uri articleUri,
        CancellationToken cancellationToken = default);
}

public sealed class ArticleContentExtractionException(
    string message,
    Exception? innerException = null,
    HttpStatusCode? statusCode = null,
    bool isCloudflareResponse = false)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public bool IsCloudflareResponse { get; } = isCloudflareResponse;
}

public sealed class ArticleContentPersistenceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
