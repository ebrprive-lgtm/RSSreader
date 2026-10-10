using System.Net;

namespace RssReader.Infrastructure;

public interface IArticleHostAddressResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
