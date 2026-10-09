using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace RssReader.Infrastructure;

internal sealed class FeedRequestClient(HttpClient httpClient)
{
    private const int MaximumErrorBodyBytes = 4_096;

    public async Task<HttpResponseMessage> SendAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("RssReader/1.0");
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            throw new HttpRequestException(
                FormatTransportFailure(uri, request, stopwatch.Elapsed, exception),
                exception,
                exception.StatusCode);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw new TaskCanceledException(
                FormatTransportFailure(uri, request, stopwatch.Elapsed, exception),
                exception);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var bodyExcerpt = await ReadErrorBodyExcerptAsync(response.Content, cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            throw new HttpRequestException(
                FormatHttpFailure(uri, request, response, stopwatch.Elapsed, bodyExcerpt),
                inner: null,
                response.StatusCode);
        }
    }

    private static string FormatTransportFailure(
        Uri uri,
        HttpRequestMessage request,
        TimeSpan elapsed,
        Exception exception)
    {
        var details = new StringBuilder()
            .AppendLine("The feed request did not receive an HTTP response.")
            .AppendLine($"Original URI: {RedactUri(uri)}")
            .AppendLine($"Elapsed: {elapsed.TotalMilliseconds:F0} ms")
            .AppendLine($"Request method: {request.Method}")
            .AppendLine("Request headers:")
            .AppendLine(FormatHeaders(request.Headers))
            .AppendLine("Exception details:");

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            details.Append(current.GetType().Name).Append(": ").AppendLine(current.Message);
        }

        return details.ToString().TrimEnd();
    }

    private static string FormatHttpFailure(
        Uri originalUri,
        HttpRequestMessage request,
        HttpResponseMessage response,
        TimeSpan elapsed,
        ErrorBodyExcerpt bodyExcerpt)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        var isBrowserVerificationChallenge = IsBrowserVerificationChallenge(bodyExcerpt.Text);
        var details = new StringBuilder()
            .AppendLine($"Feed request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).")
            .AppendLine($"Reason phrase: {response.ReasonPhrase ?? "(none)"}")
            .AppendLine($"Original URI: {RedactUri(originalUri)}")
            .AppendLine($"Final URI: {(finalUri is null ? "(unavailable)" : RedactUri(finalUri))}")
            .AppendLine($"Elapsed: {elapsed.TotalMilliseconds:F0} ms")
            .AppendLine($"HTTP version: {response.Version}")
            .AppendLine($"Request method: {request.Method}")
            .AppendLine("Request headers:")
            .AppendLine(FormatHeaders(request.Headers))
            .AppendLine("Final request headers:")
            .AppendLine(response.RequestMessage is null
                ? "  (unavailable)"
                : FormatHeaders(response.RequestMessage.Headers))
            .AppendLine("Response headers:")
            .AppendLine(FormatHeaders(response.Headers))
            .AppendLine("Response content headers:")
            .AppendLine(response.Content is null ? "(none)" : FormatHeaders(response.Content.Headers));

        if (isBrowserVerificationChallenge)
        {
            details.AppendLine(
                "Likely cause: The server returned a browser-verification/anti-bot page instead of RSS or Atom data. " +
                "This downloader does not execute JavaScript or complete browser challenges; use a publisher-provided " +
                "feed endpoint that permits feed-reader requests.");
        }

        details.AppendLine("Response body excerpt:");

        if (bodyExcerpt.Error is not null)
        {
            details.AppendLine(bodyExcerpt.Error);
        }
        else if (string.IsNullOrEmpty(bodyExcerpt.Text))
        {
            details.AppendLine("(empty)");
        }
        else if (isBrowserVerificationChallenge)
        {
            details.AppendLine(
                "Browser-verification challenge markup and script omitted. Detected indicators include " +
                "'Checking your browser', a JavaScript-required notice, and a challenge endpoint.");
        }
        else
        {
            details.Append(bodyExcerpt.Text);
            if (bodyExcerpt.IsTruncated)
            {
                details.AppendLine().Append($"[truncated after {MaximumErrorBodyBytes} bytes]");
            }
        }

        if (bodyExcerpt.DecodingNote is not null)
        {
            details.AppendLine().Append(bodyExcerpt.DecodingNote);
        }

        return details.ToString().TrimEnd();
    }

    private static bool IsBrowserVerificationChallenge(string body) =>
        body.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase) &&
        (body.Contains("Javascript required", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("__challenge", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase) ||
         body.Contains("X-Hashcash-Solution", StringComparison.OrdinalIgnoreCase));

    private static string FormatHeaders(HttpHeaders headers)
    {
        var formattedHeaders = headers.Select(header =>
        {
            var value = IsSensitiveHeader(header.Key)
                ? "[redacted]"
                : (string.Equals(header.Key, "Location", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(header.Key, "Referer", StringComparison.OrdinalIgnoreCase)) &&
                  header.Value.FirstOrDefault() is { } uriValue
                    ? RedactUriValue(uriValue)
                    : string.Join(", ", header.Value);
            return $"  {header.Key}: {value}";
        });
        return string.Join(Environment.NewLine, formattedHeaders.DefaultIfEmpty("  (none)"));
    }

    private static bool IsSensitiveHeader(string name) =>
        name.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static string RedactUri(Uri uri)
    {
        var builder = new UriBuilder(uri);
        if (!string.IsNullOrEmpty(builder.UserName))
        {
            builder.UserName = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Password))
        {
            builder.Password = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Fragment))
        {
            builder.Fragment = "[redacted]";
        }

        if (!string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = RedactQueryValues(builder.Query);
        }

        return builder.Uri.ToString();
    }

    private static string RedactUriValue(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? RedactUri(uri)
            : RedactQueryValuesInText(value);

    private static string RedactQueryValuesInText(string value)
    {
        var queryStart = value.IndexOf('?');
        if (queryStart < 0)
        {
            return value;
        }

        var fragmentStart = value.IndexOf('#', queryStart);
        var queryEnd = fragmentStart < 0 ? value.Length : fragmentStart;
        var redactedQuery = RedactQueryValues(value[queryStart..queryEnd]);
        return value[..queryStart] + redactedQuery + value[queryEnd..];
    }

    private static string RedactQueryValues(string query)
    {
        var hasQuestionMark = query.StartsWith("?", StringComparison.Ordinal);
        var redactedQuery = string.Join(
            "&",
            query.TrimStart('?').Split('&').Select(part =>
            {
                var separator = part.IndexOf('=');
                return separator < 0 ? part : $"{part[..(separator + 1)]}[redacted]";
            }));
        return hasQuestionMark ? $"?{redactedQuery}" : redactedQuery;
    }

    private static async Task<ErrorBodyExcerpt> ReadErrorBodyExcerptAsync(
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return new ErrorBodyExcerpt(string.Empty, false, null, null);
        }

        try
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[MaximumErrorBodyBytes + 1];
            var bytesRead = 0;
            while (bytesRead < buffer.Length)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(bytesRead, buffer.Length - bytesRead),
                    cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                bytesRead += count;
            }

            var isTruncated = bytesRead > MaximumErrorBodyBytes;
            var excerptLength = Math.Min(bytesRead, MaximumErrorBodyBytes);
            var decodingNote = default(string);
            Encoding encoding;
            var charset = content.Headers.ContentType?.CharSet?.Trim().Trim('"');
            try
            {
                encoding = string.IsNullOrWhiteSpace(charset)
                    ? Encoding.UTF8
                    : Encoding.GetEncoding(charset, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
                decodingNote = $"Could not decode the body using declared charset '{charset}'; UTF-8 was used.";
            }
            catch (NotSupportedException)
            {
                encoding = Encoding.UTF8;
                decodingNote = $"Could not decode the body using declared charset '{charset}'; UTF-8 was used.";
            }

            return new ErrorBodyExcerpt(
                encoding.GetString(buffer, 0, excerptLength),
                isTruncated,
                decodingNote,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            return new ErrorBodyExcerpt(
                string.Empty,
                false,
                null,
                $"Could not read the response body excerpt: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed record ErrorBodyExcerpt(string Text, bool IsTruncated, string? DecodingNote, string? Error);
}
