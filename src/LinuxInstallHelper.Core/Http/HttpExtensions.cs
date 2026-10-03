using System.Text;

namespace LinuxInstallHelper.Core.Http;

public static class HttpExtensions
{
    /// <summary>Default upper bound for small text files (catalog, checksums, signatures, keys).</summary>
    public const int DefaultMaxSmallFileBytes = 4 * 1024 * 1024;

    /// <summary>Throws when the URL is not an absolute HTTPS URL.</summary>
    public static Uri RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"Only HTTPS URLs are allowed: '{url}'.");
        }

        return uri;
    }

    /// <summary>Downloads a small file with a timeout and a size limit.</summary>
    public static async Task<byte[]> GetSmallFileAsync(
        this HttpClient client,
        Uri url,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int maxBytes = DefaultMaxSmallFileBytes)
    {
        ArgumentNullException.ThrowIfNull(client);
        RequireHttps(url.ToString());

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? HttpTimeouts.SmallFile);

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            RequireHttpsFinalUri(response, url);

            if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            {
                throw new InvalidDataException($"{url} is too large ({length} bytes, limit {maxBytes}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new InvalidDataException($"{url} is too large (limit {maxBytes} bytes).");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out while downloading {url}.");
        }
    }

    public static async Task<string> GetSmallTextAsync(
        this HttpClient client,
        Uri url,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int maxBytes = DefaultMaxSmallFileBytes)
    {
        var bytes = await client.GetSmallFileAsync(url, cancellationToken, timeout, maxBytes).ConfigureAwait(false);
        return DecodeText(bytes);
    }

    /// <summary>Decodes UTF-8 text, dropping a byte order mark if present.</summary>
    public static string DecodeText(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    /// <summary>Refuses a response that ended on a non-HTTPS URL (defense in depth).</summary>
    public static void RequireHttpsFinalUri(HttpResponseMessage response, Uri requested)
    {
        var final = response.RequestMessage?.RequestUri;
        if (final is not null && final.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"{requested} redirected to a non-HTTPS URL ({final}).");
        }
    }
}
