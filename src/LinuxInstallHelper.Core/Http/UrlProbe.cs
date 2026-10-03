using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace LinuxInstallHelper.Core.Http;

/// <summary>Result of a lightweight check of a download URL.</summary>
public sealed record UrlProbeResult(
    Uri Url,
    bool IsReachable,
    int? StatusCode,
    long? ContentLength,
    bool AcceptsRanges,
    Uri? FinalUrl,
    string? Error,
    TimeSpan Elapsed);

public interface IUrlProbe
{
    Task<UrlProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken = default);
}

/// <summary>
/// Checks that a URL answers without downloading it: <c>HEAD</c> first, then a one byte ranged
/// <c>GET</c> for servers that do not support <c>HEAD</c> properly.
/// </summary>
public sealed class UrlProbe : IUrlProbe
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public UrlProbe(HttpClient http, TimeSpan? timeout = null)
    {
        _http = http;
        _timeout = timeout ?? HttpTimeouts.Probe;
    }

    public async Task<UrlProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var watch = Stopwatch.StartNew();

        if (url.Scheme != Uri.UriSchemeHttps)
        {
            return new UrlProbeResult(url, false, null, null, false, null, "Only HTTPS URLs are allowed.", watch.Elapsed);
        }

        var head = await SendAsync(url, HttpMethod.Head, ranged: false, cancellationToken).ConfigureAwait(false);
        if (head.IsReachable && head.ContentLength is > 0)
        {
            return head with { Elapsed = watch.Elapsed };
        }

        var get = await SendAsync(url, HttpMethod.Get, ranged: true, cancellationToken).ConfigureAwait(false);
        return (get.IsReachable || !head.IsReachable ? get : head) with { Elapsed = watch.Elapsed };
    }

    private async Task<UrlProbeResult> SendAsync(Uri url, HttpMethod method, bool ranged, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        try
        {
            using var request = new HttpRequestMessage(method, url);
            if (ranged)
            {
                request.Headers.Range = new RangeHeaderValue(0, 0);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var final = response.RequestMessage?.RequestUri;
            if (final is not null && final.Scheme != Uri.UriSchemeHttps)
            {
                return new UrlProbeResult(url, false, (int)response.StatusCode, null, false, final, "Redirected to a non-HTTPS URL.", TimeSpan.Zero);
            }

            long? length = response.StatusCode == HttpStatusCode.PartialContent
                ? response.Content.Headers.ContentRange?.Length
                : response.Content.Headers.ContentLength;
            var acceptsRanges = response.StatusCode == HttpStatusCode.PartialContent
                || response.Headers.AcceptRanges.Contains("bytes", StringComparer.OrdinalIgnoreCase);

            return new UrlProbeResult(
                url,
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                length,
                acceptsRanges,
                final,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                TimeSpan.Zero);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UrlProbeResult(url, false, null, null, false, null, $"No answer within {_timeout.TotalSeconds:0} s.", TimeSpan.Zero);
        }
        catch (HttpRequestException ex)
        {
            return new UrlProbeResult(url, false, null, null, false, null, ex.InnerException?.Message ?? ex.Message, TimeSpan.Zero);
        }
    }
}
