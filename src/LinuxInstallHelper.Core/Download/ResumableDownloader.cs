using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Download;

/// <param name="Urls">Download URLs, best first (mirrors are used when the previous one fails).</param>
/// <param name="DestinationPath">Final path of the file.</param>
/// <param name="ExpectedSize">Size announced by the catalog, if known.</param>
public sealed record DownloadRequest(IReadOnlyList<Uri> Urls, string DestinationPath, long? ExpectedSize = null);

public sealed record DownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? Remaining,
    Uri Url,
    int MirrorIndex,
    int Attempt)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

public sealed record DownloadResult(string Path, long Size, Uri Url, bool Resumed);

public enum DownloadFailure
{
    AllMirrorsFailed,
    InsufficientSpace,
    SizeMismatch,
}

public sealed class DownloadException : Exception
{
    public DownloadException(DownloadFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public DownloadFailure Failure { get; }
}

public sealed class DownloadOptions
{
    public int MaxAttemptsPerUrl { get; init; } = 3;

    /// <summary>Delay before the first retry, doubled at each attempt.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The connection is considered dead when no byte arrives for this long.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int BufferSize { get; init; } = 1024 * 1024;

    /// <summary>Extra free space required on top of the remaining bytes.</summary>
    public long FreeSpaceMargin { get; init; } = 256L * 1024 * 1024;
}

public interface IDownloader
{
    Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Removes the partial file and its metadata.</summary>
    void DeletePartial(string destinationPath);
}

/// <summary>
/// HTTPS downloader that resumes interrupted downloads (Range / If-Range), retries with backoff,
/// falls back to mirrors, detects stalled connections and reports speed and remaining time.
/// The partial file (<c>.part</c>) is kept on cancellation so the next attempt can resume.
/// </summary>
public sealed class ResumableDownloader : IDownloader
{
    public const string PartialExtension = ".part";
    public const string StateExtension = ".part.json";

    private readonly HttpClient _http;
    private readonly DownloadOptions _options;
    private readonly ILogger _logger;

    public ResumableDownloader(HttpClient http, DownloadOptions? options = null, ILogger<ResumableDownloader>? logger = null)
    {
        _http = http;
        _options = options ?? new DownloadOptions();
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public static string PartialPath(string destination) => destination + PartialExtension;

    public static string StatePath(string destination) => destination + StateExtension;

    public void DeletePartial(string destinationPath)
    {
        TryDelete(PartialPath(destinationPath));
        TryDelete(StatePath(destinationPath));
    }

    public async Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Urls.Count == 0)
        {
            throw new ArgumentException("At least one URL is required.", nameof(request));
        }

        foreach (var url in request.Urls.Where(u => u.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Only HTTPS downloads are allowed: {url}", nameof(request));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath))!);
        Exception? lastError = null;

        for (var mirror = 0; mirror < request.Urls.Count; mirror++)
        {
            var url = request.Urls[mirror];
            for (var attempt = 1; attempt <= _options.MaxAttemptsPerUrl; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await DownloadOnceAsync(request, url, mirror, attempt, progress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (DownloadException ex) when (ex.Failure == DownloadFailure.InsufficientSpace)
                {
                    throw;
                }
                catch (Exception ex) when (IsRetryable(ex))
                {
                    lastError = ex;
                    var status = (ex as HttpRequestException)?.StatusCode;
                    _logger.LogWarning(ex, "Download attempt {Attempt} from {Url} failed", attempt, url);

                    // A missing file or a different file on this mirror will not fix itself: try the next one.
                    var wrongFile = ex is DownloadException { Failure: DownloadFailure.SizeMismatch };
                    if (wrongFile || ResumePolicy.IsPermanentFailure(status) || attempt == _options.MaxAttemptsPerUrl)
                    {
                        break;
                    }

                    var delay = TimeSpan.FromTicks(_options.RetryDelay.Ticks * (1L << (attempt - 1)));
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        throw new DownloadException(
            DownloadFailure.AllMirrorsFailed,
            $"The download failed on every mirror. Last error: {lastError?.Message}",
            lastError);
    }

    private async Task<DownloadResult> DownloadOnceAsync(
        DownloadRequest request,
        Uri url,
        int mirror,
        int attempt,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = PartialPath(request.DestinationPath);
        var statePath = StatePath(request.DestinationPath);
        var existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        var state = existing > 0 ? ReadState(statePath) : null;

        var decision = ResumePolicy.Decide(existing, state, request.ExpectedSize, url);
        _logger.LogInformation("Downloading {Url} (mirror {Mirror}, attempt {Attempt}): {Action} at {Offset}", url, mirror + 1, attempt, decision.Action, decision.Offset);

        if (decision.Action == ResumeAction.Complete)
        {
            return Finish(request, url, decision.Offset, resumed: true);
        }

        if (decision.Action == ResumeAction.Restart)
        {
            DeletePartial(request.DestinationPath);
        }

        var offset = decision.Action == ResumeAction.Resume ? decision.Offset : 0;
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0)
        {
            httpRequest.Headers.Range = new RangeHeaderValue(offset, null);
            if (decision.Validator is { } validator)
            {
                httpRequest.Headers.IfRange = validator.StartsWith('"')
                    ? new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(validator))
                    : DateTimeOffset.TryParse(validator, out var date) ? new RangeConditionHeaderValue(date) : null;
            }
        }

        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseCts.CancelAfter(_options.ResponseTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, responseCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{url} did not answer within {_options.ResponseTimeout.TotalSeconds:0} s.");
        }

        using (response)
        {
            var final = response.RequestMessage?.RequestUri;
            if (final is not null && final.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException($"{url} redirected to a non-HTTPS URL.", null, HttpStatusCode.Forbidden);
            }

            var contentRange = response.Content.Headers.ContentRange;
            var outcome = offset > 0
                ? ResumePolicy.Interpret(response.StatusCode, offset, contentRange?.From, contentRange?.Length)
                : response.StatusCode == HttpStatusCode.OK ? RangeOutcome.Restart : RangeOutcome.Error;

            switch (outcome)
            {
                case RangeOutcome.Complete:
                    return Finish(request, url, offset, resumed: true);
                case RangeOutcome.Error:
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {url}", null, response.StatusCode);
                case RangeOutcome.Restart:
                    offset = 0;
                    break;
            }

            var total = outcome == RangeOutcome.Append
                ? contentRange?.Length
                : response.Content.Headers.ContentLength;

            if (request.ExpectedSize is not null && total is not null && total != request.ExpectedSize)
            {
                throw new DownloadException(
                    DownloadFailure.SizeMismatch,
                    $"{url} serves {total} bytes but {request.ExpectedSize} were expected.");
            }

            EnsureFreeSpace(request.DestinationPath, (total ?? request.ExpectedSize ?? 0) - offset);

            WriteState(statePath, new PartialDownloadState
            {
                Url = url.ToString(),
                ETag = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified?.ToString("R"),
                TotalSize = total ?? request.ExpectedSize,
            });

            var received = await CopyAsync(response, partPath, offset, total, url, mirror, attempt, progress, cancellationToken).ConfigureAwait(false);
            if (total is not null && received != total)
            {
                throw new IOException($"The connection closed after {received} of {total} bytes.");
            }

            return Finish(request, url, received, resumed: offset > 0);
        }
    }

    private async Task<long> CopyAsync(
        HttpResponseMessage response,
        string partPath,
        long offset,
        long? total,
        Uri url,
        int mirror,
        int attempt,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var mode = offset > 0 ? FileMode.Append : FileMode.Create;
        await using var file = new FileStream(partPath, mode, FileAccess.Write, FileShare.Read, 1, FileOptions.Asynchronous);
        if (offset > 0 && file.Position != offset)
        {
            throw new IOException("The partial file changed while resuming.");
        }

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var buffer = new byte[_options.BufferSize];
        var received = offset;
        var meter = new SpeedMeter();
        var clock = Stopwatch.StartNew();
        TimeSpan? lastReport = null;
        meter.Add(TimeSpan.Zero, received);

        while (true)
        {
            int read;
            stallCts.CancelAfter(_options.StallTimeout);
            try
            {
                read = await body.ReadAsync(buffer, stallCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No data received from {url} for {_options.StallTimeout.TotalSeconds:0} s.");
            }

            if (read == 0)
            {
                break;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            var now = clock.Elapsed;
            if (lastReport is null || now - lastReport.Value >= _options.ProgressInterval)
            {
                lastReport = now;
                meter.Add(now, received);
                progress?.Report(new DownloadProgress(received, total, meter.BytesPerSecond, meter.Remaining(received, total), url, mirror, attempt));
            }
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        meter.Add(clock.Elapsed, received);
        progress?.Report(new DownloadProgress(received, total ?? received, meter.BytesPerSecond, TimeSpan.Zero, url, mirror, attempt));
        return received;
    }

    private DownloadResult Finish(DownloadRequest request, Uri url, long size, bool resumed)
    {
        var partPath = PartialPath(request.DestinationPath);
        if (request.ExpectedSize is not null && size != request.ExpectedSize)
        {
            DeletePartial(request.DestinationPath);
            throw new DownloadException(DownloadFailure.SizeMismatch, $"Downloaded {size} bytes but {request.ExpectedSize} were expected.");
        }

        File.Move(partPath, request.DestinationPath, overwrite: true);
        TryDelete(StatePath(request.DestinationPath));
        _logger.LogInformation("Downloaded {Path} ({Size} bytes) from {Url}", request.DestinationPath, size, url);
        return new DownloadResult(request.DestinationPath, size, url, resumed);
    }

    private void EnsureFreeSpace(string destination, long remainingBytes)
    {
        if (remainingBytes <= 0)
        {
            return;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < remainingBytes + _options.FreeSpaceMargin)
            {
                throw new DownloadException(
                    DownloadFailure.InsufficientSpace,
                    $"Not enough free space on {root}: {remainingBytes + _options.FreeSpaceMargin} bytes needed, {free} available.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not check the free space for {Path}", destination);
        }
    }

    private static PartialDownloadState? ReadState(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<PartialDownloadState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteState(string path, PartialDownloadState state) =>
        File.WriteAllText(path, JsonSerializer.Serialize(state));

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException or IOException or TimeoutException
            || (ex is DownloadException d && d.Failure == DownloadFailure.SizeMismatch);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
