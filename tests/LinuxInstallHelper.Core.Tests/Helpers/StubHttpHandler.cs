using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace LinuxInstallHelper.Core.Tests.Helpers;

/// <summary>In-memory HTTP server for tests: maps URLs to responses, supports HEAD and byte ranges.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public ConcurrentQueue<HttpRequestMessage> Requests { get; } = new();

    public StubHttpHandler Add(string url, byte[] content, string? etag = null, bool supportRanges = true)
    {
        _routes[url] = request => Serve(request, content, etag, supportRanges);
        return this;
    }

    public StubHttpHandler Add(string url, string content) => Add(url, System.Text.Encoding.UTF8.GetBytes(content));

    public StubHttpHandler AddStatus(string url, HttpStatusCode status)
    {
        _routes[url] = _ => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
        return this;
    }

    public StubHttpHandler AddHandler(string url, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _routes[url] = handler;
        return this;
    }

    public int CountRequests(string url) => Requests.Count(r => r.RequestUri?.ToString() == url);

    public HttpClient CreateClient() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request);
        var url = request.RequestUri!.ToString();
        var response = _routes.TryGetValue(url, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
        response.RequestMessage ??= request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Serve(HttpRequestMessage request, byte[] content, string? etag, bool supportRanges)
    {
        var isHead = request.Method == HttpMethod.Head;
        var range = request.Headers.Range?.Ranges.FirstOrDefault();
        var ifRange = request.Headers.IfRange?.EntityTag?.Tag;
        var honorRange = supportRanges && range is not null && (ifRange is null || ifRange == etag);

        HttpResponseMessage response;
        if (honorRange)
        {
            var from = range!.From ?? 0;
            if (from >= content.Length)
            {
                response = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) { Content = new ByteArrayContent([]) };
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(content.Length);
                return response;
            }

            var to = Math.Min(range.To ?? content.Length - 1, content.Length - 1);
            var slice = content[(int)from..((int)to + 1)];
            response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(isHead ? [] : slice) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, content.Length);
            response.Content.Headers.ContentLength = slice.Length;
        }
        else
        {
            response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(isHead ? [] : content) };
            response.Content.Headers.ContentLength = content.Length;
        }

        if (supportRanges)
        {
            response.Headers.AcceptRanges.Add("bytes");
        }

        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }
}
