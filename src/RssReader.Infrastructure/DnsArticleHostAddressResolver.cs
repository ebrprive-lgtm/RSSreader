using System.Net;

namespace RssReader.Infrastructure;

public sealed class DnsArticleHostAddressResolver : IArticleHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return [address];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}
