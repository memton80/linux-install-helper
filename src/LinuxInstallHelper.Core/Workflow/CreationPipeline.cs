using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Download;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Verification;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Workflow;

public enum CreationStage
{
    Resolve,
    CheckLinks,
    Download,
    Verify,
    Write,
    Eject,
}

public enum StageState
{
    Pending,
    Running,
    Done,
    Skipped,
    Failed,
}

public sealed record CreationProgress(
    CreationStage Stage,
    StageState State,
    double? Fraction = null,
    long Bytes = 0,
    long Total = 0,
    double BytesPerSecond = 0,
    TimeSpan? Remaining = null,
    UsbWriteStage? WriteStage = null);

public enum LogKind
{
    Info,
    Success,
    Warning,
}

/// <summary>A step of the process, as a localizable message: <see cref="Key"/> is a resource name, <see cref="Args"/> its arguments.</summary>
public sealed record CreationLogEntry(DateTimeOffset Time, LogKind Kind, string Key, IReadOnlyList<object?> Args);

public abstract record CreationSource;

/// <summary>Download a distribution from the catalog.</summary>
public sealed record DistroImageSource(Distro Distro) : CreationSource;

/// <summary>Use an ISO file already on the computer, optionally with the SHA-256 the user expects.</summary>
public sealed record LocalImageSource(string Path, string? ExpectedSha256) : CreationSource;

public sealed record CreationJob(CreationSource Source, DiskInfo Target, UserSettings Settings, string DownloadFolder);

public sealed record CreationResult(
    string ImagePath,
    string ImageHash,
    HashAlgorithmKind HashAlgorithm,
    bool Downloaded,
    SignatureStatus ChecksumSignature,
    SignatureStatus ImageSignature,
    string? Signer,
    bool WriteVerified,
    bool Ejected,
    string? EjectError,
    ResolvedImage? Resolved);

public interface ICreationPipeline
{
    Task<CreationResult> RunAsync(CreationJob job, IProgress<CreationProgress>? progress = null, Action<CreationLogEntry>? log = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The whole process: find the image, check the links, download (or reuse a verified copy), verify,
/// write, eject. Each step must succeed before the next one starts; nothing is written to the drive
/// until the image has been proven authentic.
/// </summary>
public sealed class CreationPipeline : ICreationPipeline
{
    private readonly IImageResolver _resolver;
    private readonly IUrlProbe _probe;
    private readonly IDownloader _downloader;
    private readonly IImageVerifier _verifier;
    private readonly IUsbWriter _writer;
    private readonly IDiskEjector _ejector;
    private readonly ILogger _logger;

    public CreationPipeline(
        IImageResolver resolver,
        IUrlProbe probe,
        IDownloader downloader,
        IImageVerifier verifier,
        IUsbWriter writer,
        IDiskEjector ejector,
        ILogger<CreationPipeline>? logger = null)
    {
        _resolver = resolver;
        _probe = probe;
        _downloader = downloader;
        _verifier = verifier;
        _writer = writer;
        _ejector = ejector;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<CreationResult> RunAsync(
        CreationJob job,
        IProgress<CreationProgress>? progress = null,
        Action<CreationLogEntry>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        var run = new Run(progress, log, _logger);

        var image = job.Source switch
        {
            DistroImageSource distro => await PrepareDistroImageAsync(job, distro.Distro, run, cancellationToken).ConfigureAwait(false),
            LocalImageSource local => await PrepareLocalImageAsync(local, run, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentException("Unknown image source.", nameof(job)),
        };

        // Write
        run.Stage(CreationStage.Write, StageState.Running);
        run.Log(LogKind.Info, "Log_Writing", job.Target.FriendlyName, job.Target.Number);
        var writeProgress = new InlineProgress<UsbWriteProgress>(p =>
        {
            if (p.Stage == UsbWriteStage.Verifying && p.BytesDone == 0)
            {
                run.Log(LogKind.Info, "Log_WriteVerifying");
            }

            run.Report(new CreationProgress(CreationStage.Write, StageState.Running, p.Fraction, p.BytesDone, p.TotalBytes, p.BytesPerSecond, p.Remaining, p.Stage));
        });
        var written = await _writer.WriteAsync(new UsbWriteRequest(image.Path, job.Target, job.Settings.VerifyAfterWrite), writeProgress, cancellationToken)
            .ConfigureAwait(false);
        run.Log(LogKind.Success, "Log_WriteDone", written.BytesWritten);
        if (written.Verified)
        {
            run.Log(LogKind.Success, "Log_WriteVerified");
        }

        run.Stage(CreationStage.Write, StageState.Done);

        // Eject
        var ejected = false;
        string? ejectError = null;
        if (job.Settings.EjectWhenDone)
        {
            run.Stage(CreationStage.Eject, StageState.Running);
            run.Log(LogKind.Info, "Log_Ejecting");
            ejectError = await _ejector.EjectAsync(job.Target, cancellationToken).ConfigureAwait(false);
            ejected = ejectError is null;
            if (ejected)
            {
                run.Log(LogKind.Success, "Log_Ejected");
            }
            else
            {
                run.Log(LogKind.Warning, "Log_EjectFailed", ejectError);
            }

            run.Stage(CreationStage.Eject, StageState.Done);
        }
        else
        {
            run.Stage(CreationStage.Eject, StageState.Skipped);
        }

        if (!job.Settings.KeepIsoAfterWrite && job.Source is DistroImageSource)
        {
            TryDelete(image.Path);
            run.Log(LogKind.Info, "Log_IsoDeleted");
        }

        return new CreationResult(
            image.Path,
            image.Hash,
            image.Algorithm,
            image.Downloaded,
            image.Resolved?.ChecksumSignature ?? SignatureStatus.NotProvided,
            image.ImageSignature,
            image.Resolved?.ChecksumSigner ?? image.ImageSigner,
            written.Verified,
            ejected,
            ejectError,
            image.Resolved);
    }

    private sealed record PreparedImage(string Path, string Hash, HashAlgorithmKind Algorithm, bool Downloaded, SignatureStatus ImageSignature, ResolvedImage? Resolved, string? ImageSigner = null);

    private async Task<PreparedImage> PrepareDistroImageAsync(CreationJob job, Distro distro, Run run, CancellationToken cancellationToken)
    {
        // Resolve the exact file and its official checksum (signature checked here).
        run.Stage(CreationStage.Resolve, StageState.Running);
        run.Log(LogKind.Info, "Log_Resolving", distro.DisplayName);
        var resolved = await _resolver.ResolveAsync(distro, cancellationToken).ConfigureAwait(false);
        run.Log(LogKind.Info, "Log_Resolved", resolved.FileName, resolved.DisplayVersion);
        switch (resolved.ChecksumSignature)
        {
            case SignatureStatus.Verified:
                run.Log(LogKind.Success, "Log_ChecksumSignatureVerified", FormatFingerprint(resolved.ChecksumSigner));
                break;
            case SignatureStatus.Unavailable:
                run.Log(LogKind.Warning, "Log_ChecksumSignatureUnavailable");
                break;
            default:
                run.Log(LogKind.Info, "Log_ChecksumNotSigned");
                break;
        }

        if (resolved.Warning is not null)
        {
            _logger.LogWarning("{Distro}: {Warning}", distro.Id, resolved.Warning);
        }

        if (resolved.Size is long announced && announced > job.Target.Size)
        {
            throw new UsbWriteException(UsbWriteFailure.ImageTooLarge, "The image does not fit on this USB drive.");
        }

        run.Stage(CreationStage.Resolve, StageState.Done);

        Directory.CreateDirectory(job.DownloadFolder);
        var path = Path.Combine(job.DownloadFolder, resolved.FileName);

        // Reuse a previous download when it is still valid.
        if (File.Exists(path) && (resolved.Size is null || new FileInfo(path).Length == resolved.Size))
        {
            run.Log(LogKind.Info, "Log_CachedImageFound", path);
            run.Stage(CreationStage.CheckLinks, StageState.Skipped);
            run.Stage(CreationStage.Download, StageState.Skipped);
            run.Stage(CreationStage.Verify, StageState.Running);
            try
            {
                var cached = await VerifyAsync(path, resolved, run, cancellationToken).ConfigureAwait(false);
                return new PreparedImage(path, cached.Hash, cached.Algorithm, false, cached.ImageSignature, resolved, cached.Signer);
            }
            catch (VerificationException ex) when (ex.Failure == VerificationFailure.ChecksumMismatch)
            {
                run.Log(LogKind.Warning, "Log_CachedImageInvalid");
                TryDelete(path);
            }
        }

        // Check the links before downloading: reachable mirrors first.
        run.Stage(CreationStage.CheckLinks, StageState.Running);
        var urls = await CheckLinksAsync(resolved, run, cancellationToken).ConfigureAwait(false);
        run.Stage(CreationStage.CheckLinks, StageState.Done);

        var download = await DownloadAsync(urls, path, resolved, run, cancellationToken).ConfigureAwait(false);

        run.Stage(CreationStage.Verify, StageState.Running);
        ImageVerificationResult verified;
        try
        {
            verified = await VerifyAsync(path, resolved, run, cancellationToken).ConfigureAwait(false);
        }
        catch (VerificationException ex) when (ex.Failure == VerificationFailure.ChecksumMismatch && download.Resumed)
        {
            // A resumed download may mix two versions of the file: download it again from scratch once.
            run.Log(LogKind.Warning, "Log_RetryAfterMismatch");
            TryDelete(path);
            _downloader.DeletePartial(path);
            await DownloadAsync(urls, path, resolved, run, cancellationToken).ConfigureAwait(false);
            run.Stage(CreationStage.Verify, StageState.Running);
            verified = await VerifyOrDeleteAsync(path, resolved, run, cancellationToken).ConfigureAwait(false);
        }
        catch (VerificationException)
        {
            // Never keep a file that failed verification.
            TryDelete(path);
            throw;
        }

        return new PreparedImage(path, verified.Hash, verified.Algorithm, true, verified.ImageSignature, resolved, verified.Signer);
    }

    private async Task<ImageVerificationResult> VerifyOrDeleteAsync(string path, ResolvedImage resolved, Run run, CancellationToken cancellationToken)
    {
        try
        {
            return await VerifyAsync(path, resolved, run, cancellationToken).ConfigureAwait(false);
        }
        catch (VerificationException)
        {
            TryDelete(path);
            throw;
        }
    }

    private async Task<ImageVerificationResult> VerifyAsync(string path, ResolvedImage resolved, Run run, CancellationToken cancellationToken)
    {
        run.Log(LogKind.Info, "Log_Verifying", resolved.HashAlgorithm.ToString().ToUpperInvariant().Replace("SHA", "SHA-", StringComparison.Ordinal));
        var verifyProgress = new InlineProgress<VerificationProgress>(p =>
            run.Report(new CreationProgress(CreationStage.Verify, StageState.Running, p.Fraction, p.BytesProcessed, p.TotalBytes)));
        var result = await _verifier.VerifyAsync(path, resolved, verifyProgress, cancellationToken).ConfigureAwait(false);

        run.Log(LogKind.Success, "Log_ImageVerified", result.Hash);
        if (result.ImageSignature == SignatureStatus.Verified)
        {
            run.Log(LogKind.Success, "Log_ImageSignatureVerified", FormatFingerprint(result.Signer));
        }
        else if (result.ImageSignature == SignatureStatus.Unavailable)
        {
            run.Log(LogKind.Warning, "Log_ImageSignatureUnavailable");
        }

        run.Stage(CreationStage.Verify, StageState.Done);
        return result;
    }

    private async Task<IReadOnlyList<Uri>> CheckLinksAsync(ResolvedImage resolved, Run run, CancellationToken cancellationToken)
    {
        var probes = await Task.WhenAll(resolved.Urls.Select(url => _probe.ProbeAsync(url, cancellationToken))).ConfigureAwait(false);
        var reachable = new List<Uri>();
        var others = new List<Uri>();

        foreach (var probe in probes)
        {
            var sizeMismatch = resolved.Size is long expected && probe.ContentLength is long length && length != expected;
            if (probe.IsReachable && !sizeMismatch)
            {
                reachable.Add(probe.Url);
                run.Log(LogKind.Info, "Log_LinkOk", probe.Url.Host, probe.ContentLength ?? 0);
            }
            else
            {
                others.Add(probe.Url);
                run.Log(LogKind.Warning, "Log_LinkFailed", probe.Url.Host, sizeMismatch ? "size mismatch" : probe.Error);
            }
        }

        if (reachable.Count == 0)
        {
            throw new DownloadException(DownloadFailure.AllMirrorsFailed, "No download server answered: " + string.Join("; ", probes.Select(p => $"{p.Url.Host}: {p.Error}")));
        }

        return [.. reachable, .. others];
    }

    private async Task<DownloadResult> DownloadAsync(IReadOnlyList<Uri> urls, string path, ResolvedImage resolved, Run run, CancellationToken cancellationToken)
    {
        run.Stage(CreationStage.Download, StageState.Running);
        run.Log(LogKind.Info, "Log_Downloading", urls[0].Host);
        var lastMirror = 0;
        var downloadProgress = new InlineProgress<DownloadProgress>(p =>
        {
            if (p.MirrorIndex != lastMirror)
            {
                lastMirror = p.MirrorIndex;
                run.Log(LogKind.Warning, "Log_MirrorFallback", p.Url.Host);
            }

            run.Report(new CreationProgress(CreationStage.Download, StageState.Running, p.Fraction, p.BytesReceived, p.TotalBytes ?? 0, p.BytesPerSecond, p.Remaining));
        });

        var result = await _downloader.DownloadAsync(new DownloadRequest(urls, path, resolved.Size), downloadProgress, cancellationToken).ConfigureAwait(false);
        if (result.Resumed)
        {
            run.Log(LogKind.Info, "Log_DownloadResumed");
        }

        run.Log(LogKind.Success, "Log_Downloaded", result.Size);
        run.Stage(CreationStage.Download, StageState.Done);
        return result;
    }

    private async Task<PreparedImage> PrepareLocalImageAsync(LocalImageSource local, Run run, CancellationToken cancellationToken)
    {
        run.Stage(CreationStage.Resolve, StageState.Skipped);
        run.Stage(CreationStage.CheckLinks, StageState.Skipped);
        run.Stage(CreationStage.Download, StageState.Skipped);
        run.Stage(CreationStage.Verify, StageState.Running);
        run.Log(LogKind.Info, "Log_LocalImage", local.Path);

        var info = IsoInspector.Inspect(local.Path);
        if (!info.IsIso9660)
        {
            throw new VerificationException(VerificationFailure.NotListed, "The selected file is not an ISO image.");
        }

        if (!info.IsHybrid)
        {
            run.Log(LogKind.Warning, "Log_NotHybrid");
        }

        var length = info.Size;
        var hashProgress = new InlineProgress<long>(read => run.Report(new CreationProgress(CreationStage.Verify, StageState.Running, length > 0 ? (double)read / length : null, read, length)));
        var hash = await FileHasher.ComputeAsync(local.Path, HashAlgorithmKind.Sha256, hashProgress, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(local.ExpectedSha256))
        {
            if (!FileHasher.HashEquals(local.ExpectedSha256, hash))
            {
                throw new VerificationException(
                    VerificationFailure.ChecksumMismatch,
                    $"The SHA-256 of the file does not match the expected value.\nExpected: {local.ExpectedSha256.Trim().ToLowerInvariant()}\nActual:   {hash}");
            }

            run.Log(LogKind.Success, "Log_ImageVerified", hash);
        }
        else
        {
            run.Log(LogKind.Info, "Log_LocalImageHash", hash);
        }

        run.Stage(CreationStage.Verify, StageState.Done);
        return new PreparedImage(local.Path, hash, HashAlgorithmKind.Sha256, false, SignatureStatus.NotProvided, null);
    }

    private static string FormatFingerprint(string? fingerprint) =>
        fingerprint is { Length: 40 } f ? string.Join(' ', Enumerable.Range(0, 10).Select(i => f.Substring(i * 4, 4))) : fingerprint ?? string.Empty;

    private void TryDelete(string path)
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
            _logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }

    /// <summary>Progress and log plumbing of one run.</summary>
    private sealed class Run(IProgress<CreationProgress>? progress, Action<CreationLogEntry>? log, ILogger logger)
    {
        public void Stage(CreationStage stage, StageState state) => progress?.Report(new CreationProgress(stage, state, state == StageState.Done ? 1 : null));

        public void Report(CreationProgress value) => progress?.Report(value);

        public void Log(LogKind kind, string key, params object?[] args)
        {
            logger.Log(kind == LogKind.Warning ? LogLevel.Warning : LogLevel.Information, "{Key} {Args}", key, string.Join(" | ", args));
            log?.Invoke(new CreationLogEntry(DateTimeOffset.Now, kind, key, args));
        }
    }
}
