using System.Net;
using LinuxInstallHelper.Core.Download;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Tests.Verification;

namespace LinuxInstallHelper.Core.Tests.Download;

public sealed class ResumableDownloaderTests : IDisposable
{
    private const string Url = "https://mirror.example/distro.iso";
    private const string Mirror = "https://backup.example/distro.iso";
    private readonly TempFolder _temp = new();
    private readonly byte[] _content = CreateContent(300_000);

    public void Dispose() => _temp.Dispose();

    private string Destination => _temp.File("distro.iso");

    private static byte[] CreateContent(int length)
    {
        var bytes = new byte[length];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static ResumableDownloader Downloader(StubHttpHandler handler) => new(
        handler.CreateClient(),
        new DownloadOptions { RetryDelay = TimeSpan.Zero, BufferSize = 16 * 1024, ProgressInterval = TimeSpan.Zero, FreeSpaceMargin = 0 });

    private void WritePartial(int length, string? etag = "\"v1\"", string url = Url)
    {
        File.WriteAllBytes(ResumableDownloader.PartialPath(Destination), _content[..length]);
        File.WriteAllText(
            ResumableDownloader.StatePath(Destination),
            System.Text.Json.JsonSerializer.Serialize(new PartialDownloadState { Url = url, ETag = etag, TotalSize = _content.Length }));
    }

    [Fact]
    public async Task Downloads_a_file_and_reports_progress()
    {
        var handler = new StubHttpHandler().Add(Url, _content, etag: "\"v1\"");
        var reports = new List<DownloadProgress>();

        var result = await Downloader(handler).DownloadAsync(
            new DownloadRequest([new Uri(Url)], Destination, _content.Length),
            new SynchronousProgress<DownloadProgress>(reports.Add));

        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
        Assert.False(result.Resumed);
        Assert.False(File.Exists(ResumableDownloader.PartialPath(Destination)));
        Assert.False(File.Exists(ResumableDownloader.StatePath(Destination)));
        Assert.NotEmpty(reports);
        Assert.Equal(_content.Length, reports[^1].BytesReceived);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task Resumes_a_partial_download_with_range_and_if_range()
    {
        WritePartial(100_000);
        var handler = new StubHttpHandler().Add(Url, _content, etag: "\"v1\"");

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length));

        Assert.True(result.Resumed);
        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(100_000, request.Headers.Range!.Ranges.Single().From);
        Assert.Equal("\"v1\"", request.Headers.IfRange!.EntityTag!.Tag);
    }

    [Fact]
    public async Task Restarts_when_the_file_changed_on_the_server()
    {
        WritePartial(100_000, etag: "\"old\"");
        var handler = new StubHttpHandler().Add(Url, _content, etag: "\"new\"");

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length));

        Assert.False(result.Resumed);
        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Restarts_when_the_server_ignores_ranges()
    {
        WritePartial(100_000);
        var handler = new StubHttpHandler().Add(Url, _content, etag: "\"v1\"", supportRanges: false);

        await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length));

        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Resumes_after_the_connection_drops()
    {
        var calls = 0;
        var handler = new StubHttpHandler().AddHandler(Url, request =>
        {
            var response = StubHttpHandler.Serve(request, _content, "\"v1\"", supportRanges: true);
            if (Interlocked.Increment(ref calls) == 1)
            {
                // First answer: the stream breaks after 120 kB.
                response.Content = new StreamContent(new BreakingStream(_content, 120_000));
                response.Content.Headers.ContentLength = _content.Length;
            }

            return response;
        });

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length));

        Assert.True(result.Resumed);
        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
        Assert.Equal(2, calls);
        Assert.Equal(120_000, handler.Requests.Last().Headers.Range!.Ranges.Single().From);
    }

    [Fact]
    public async Task Falls_back_to_the_next_mirror()
    {
        var handler = new StubHttpHandler()
            .AddStatus(Url, HttpStatusCode.NotFound)
            .Add(Mirror, _content);

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url), new Uri(Mirror)], Destination, _content.Length));

        Assert.Equal(new Uri(Mirror), result.Url);
        Assert.Equal(1, handler.CountRequests(Url));
        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Retries_a_temporary_error_before_switching_mirror()
    {
        var calls = 0;
        var handler = new StubHttpHandler().AddHandler(Url, request => Interlocked.Increment(ref calls) == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new ByteArrayContent([]) }
            : StubHttpHandler.Serve(request, _content, null, true));

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url), new Uri(Mirror)], Destination, _content.Length));

        Assert.Equal(new Uri(Url), result.Url);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Skips_a_mirror_serving_a_different_size()
    {
        var handler = new StubHttpHandler()
            .Add(Url, _content[..1000])
            .Add(Mirror, _content);

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url), new Uri(Mirror)], Destination, _content.Length));

        Assert.Equal(new Uri(Mirror), result.Url);
        Assert.Equal(1, handler.CountRequests(Url));
    }

    [Fact]
    public async Task Fails_when_every_mirror_fails()
    {
        var handler = new StubHttpHandler()
            .AddStatus(Url, HttpStatusCode.InternalServerError)
            .AddStatus(Mirror, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<DownloadException>(() =>
            Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url), new Uri(Mirror)], Destination, _content.Length)));

        Assert.Equal(DownloadFailure.AllMirrorsFailed, ex.Failure);
        Assert.Equal(3, handler.CountRequests(Url));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Cancellation_keeps_the_partial_file_for_later()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHttpHandler().AddHandler(Url, request =>
        {
            var response = StubHttpHandler.Serve(request, _content, "\"v1\"", true);
            response.Content = new StreamContent(new BreakingStream(_content, 50_000, cts));
            response.Content.Headers.ContentLength = _content.Length;
            return response;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length), null, cts.Token));

        Assert.True(File.Exists(ResumableDownloader.PartialPath(Destination)));
        Assert.True(File.Exists(ResumableDownloader.StatePath(Destination)));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Complete_partial_file_is_finished_without_downloading_again()
    {
        WritePartial(_content.Length);
        var handler = new StubHttpHandler().Add(Url, _content, etag: "\"v1\"");

        var result = await Downloader(handler).DownloadAsync(new DownloadRequest([new Uri(Url)], Destination, _content.Length));

        Assert.True(result.Resumed);
        Assert.Empty(handler.Requests);
        Assert.Equal(_content, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Refuses_plain_http()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Downloader(new StubHttpHandler()).DownloadAsync(new DownloadRequest([new Uri("http://mirror.example/distro.iso")], Destination)));
    }

    [Fact]
    public void DeletePartial_removes_partial_data()
    {
        WritePartial(10);

        Downloader(new StubHttpHandler()).DeletePartial(Destination);

        Assert.False(File.Exists(ResumableDownloader.PartialPath(Destination)));
        Assert.False(File.Exists(ResumableDownloader.StatePath(Destination)));
    }

    /// <summary>Serves the beginning of the content, then fails like a dropped connection (or cancels).</summary>
    private sealed class BreakingStream(byte[] content, int breakAfter, CancellationTokenSource? cancelInstead = null) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= breakAfter)
            {
                if (cancelInstead is not null)
                {
                    cancelInstead.Cancel();
                    throw new OperationCanceledException(cancelInstead.Token);
                }

                throw new IOException("Connection reset by peer.");
            }

            var n = Math.Min(count, breakAfter - _position);
            Array.Copy(content, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
