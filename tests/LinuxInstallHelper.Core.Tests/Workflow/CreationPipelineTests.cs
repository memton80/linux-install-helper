using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Download;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Tests.Disks;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Tests.Verification;
using LinuxInstallHelper.Core.Verification;
using LinuxInstallHelper.Core.Workflow;
using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Workflow;

public sealed class CreationPipelineTests : IDisposable
{
    private static readonly Uri Primary = new("https://primary.example/distro.iso");
    private static readonly Uri Mirror = new("https://mirror.example/distro.iso");
    private readonly TempFolder _temp = new();
    private readonly List<string> _calls = [];
    private readonly List<CreationLogEntry> _logs = [];
    private readonly List<CreationProgress> _progress = [];

    public void Dispose() => _temp.Dispose();

    private Distro Distro => TestDistros.Get("debian-netinst");

    private DiskInfo Target => DiskFilterTests.UsbKey();

    private ResolvedImage Resolved(long? size = 1000) => new()
    {
        Distro = Distro,
        FileName = "distro.iso",
        Urls = [Primary, Mirror],
        Hash = "abc",
        Size = size,
        ChecksumSignature = SignatureStatus.Verified,
        ChecksumSigner = "DF9B9C49EAA9298432589D76DA87E80D6294BE9B",
    };

    private sealed class Fakes
    {
        public required FakeResolver Resolver { get; init; }

        public required FakeProbe Probe { get; init; }

        public required FakeDownloader Downloader { get; init; }

        public required FakeVerifier Verifier { get; init; }

        public required FakeWriter Writer { get; init; }

        public required FakeEjector Ejector { get; init; }

        public CreationPipeline Pipeline() => new(Resolver, Probe, Downloader, Verifier, Writer, Ejector);
    }

    private Fakes Create(ResolvedImage? resolved = null) => new()
    {
        Resolver = new FakeResolver(resolved ?? Resolved(), _calls),
        Probe = new FakeProbe(_calls),
        Downloader = new FakeDownloader(_calls),
        Verifier = new FakeVerifier(_calls),
        Writer = new FakeWriter(_calls),
        Ejector = new FakeEjector(_calls),
    };

    private Task<CreationResult> Run(Fakes fakes, CreationSource? source = null, UserSettings? settings = null) =>
        fakes.Pipeline().RunAsync(
            new CreationJob(source ?? new DistroImageSource(Distro), Target, settings ?? new UserSettings(), _temp.Path),
            new SynchronousProgress<CreationProgress>(_progress.Add),
            _logs.Add);

    [Fact]
    public async Task Runs_every_step_in_order()
    {
        var fakes = Create();

        var result = await Run(fakes);

        Assert.Equal(["resolve", "probe", "probe", "download", "verify", "write", "eject"], _calls);
        Assert.True(result.Downloaded);
        Assert.True(result.Ejected);
        Assert.True(result.WriteVerified);
        Assert.Equal(SignatureStatus.Verified, result.ChecksumSignature);
        Assert.Contains(_logs, l => l.Key == "Log_ChecksumSignatureVerified");
        Assert.Equal(
            Enum.GetValues<CreationStage>(),
            _progress.Where(p => p.State == StageState.Done).Select(p => p.Stage).Distinct());
    }

    [Fact]
    public async Task Reachable_mirrors_are_tried_first()
    {
        var fakes = Create();
        fakes.Probe.Unreachable.Add(Primary);

        await Run(fakes);

        Assert.Equal([Mirror, Primary], fakes.Downloader.LastRequest!.Urls);
        Assert.Contains(_logs, l => l.Key == "Log_LinkFailed");
    }

    [Fact]
    public async Task Stops_before_downloading_when_no_mirror_answers()
    {
        var fakes = Create();
        fakes.Probe.Unreachable.Add(Primary);
        fakes.Probe.Unreachable.Add(Mirror);

        var ex = await Assert.ThrowsAsync<DownloadException>(() => Run(fakes));

        Assert.Equal(DownloadFailure.AllMirrorsFailed, ex.Failure);
        Assert.DoesNotContain("download", _calls);
        Assert.DoesNotContain("write", _calls);
    }

    [Fact]
    public async Task Never_writes_an_image_that_fails_verification()
    {
        var fakes = Create();
        fakes.Verifier.Failures.Enqueue(VerificationFailure.ChecksumMismatch);

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Run(fakes));

        Assert.Equal(VerificationFailure.ChecksumMismatch, ex.Failure);
        Assert.DoesNotContain("write", _calls);
        Assert.False(File.Exists(Path.Combine(_temp.Path, "distro.iso")));
    }

    [Fact]
    public async Task Downloads_again_once_when_a_resumed_download_is_corrupted()
    {
        var fakes = Create();
        fakes.Downloader.Resumed = true;
        fakes.Verifier.Failures.Enqueue(VerificationFailure.ChecksumMismatch);

        await Run(fakes);

        Assert.Equal(["resolve", "probe", "probe", "download", "verify", "download", "verify", "write", "eject"], _calls);
        Assert.Contains(_logs, l => l.Key == "Log_RetryAfterMismatch");
    }

    [Fact]
    public async Task Bad_signature_stops_everything()
    {
        var fakes = Create();
        fakes.Verifier.Failures.Enqueue(VerificationFailure.BadSignature);

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Run(fakes));

        Assert.Equal(VerificationFailure.BadSignature, ex.Failure);
        Assert.DoesNotContain("write", _calls);
    }

    [Fact]
    public async Task Reuses_a_valid_cached_image()
    {
        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "distro.iso"), new byte[1000]);
        var fakes = Create();

        var result = await Run(fakes);

        Assert.Equal(["resolve", "verify", "write", "eject"], _calls);
        Assert.False(result.Downloaded);
        Assert.Contains(_progress, p => p.Stage == CreationStage.Download && p.State == StageState.Skipped);
    }

    [Fact]
    public async Task Downloads_again_when_the_cached_image_is_corrupted()
    {
        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "distro.iso"), new byte[1000]);
        var fakes = Create();
        fakes.Verifier.Failures.Enqueue(VerificationFailure.ChecksumMismatch);

        await Run(fakes);

        Assert.Equal(["resolve", "verify", "probe", "probe", "download", "verify", "write", "eject"], _calls);
        Assert.Contains(_logs, l => l.Key == "Log_CachedImageInvalid");
    }

    [Fact]
    public async Task Image_larger_than_the_drive_is_refused_before_downloading()
    {
        var fakes = Create(Resolved(size: Target.Size + 1));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => Run(fakes));

        Assert.Equal(UsbWriteFailure.ImageTooLarge, ex.Failure);
        Assert.Equal(["resolve"], _calls);
    }

    [Fact]
    public async Task Settings_control_ejection_and_iso_cleanup()
    {
        var fakes = Create();

        var result = await Run(fakes, settings: new UserSettings { EjectWhenDone = false, KeepIsoAfterWrite = false, VerifyAfterWrite = false });

        Assert.DoesNotContain("eject", _calls);
        Assert.False(result.Ejected);
        Assert.False(fakes.Writer.LastRequest!.VerifyAfterWrite);
        Assert.False(File.Exists(result.ImagePath));
    }

    [Fact]
    public async Task Eject_refusal_is_reported_but_not_fatal()
    {
        var fakes = Create();
        fakes.Ejector.Error = "In use";

        var result = await Run(fakes);

        Assert.False(result.Ejected);
        Assert.Equal("In use", result.EjectError);
        Assert.Contains(_logs, l => l.Key == "Log_EjectFailed");
    }

    [Fact]
    public async Task Local_image_is_hashed_and_checked_against_the_expected_value()
    {
        var iso = CreateIsoFile();
        var fakes = Create();
        var expected = await FileHasher.ComputeAsync(iso);

        var result = await Run(fakes, new LocalImageSource(iso, expected.ToUpperInvariant()));

        Assert.Equal(["write", "eject"], _calls);
        Assert.Equal(expected, result.ImageHash);
        Assert.Equal(iso, fakes.Writer.LastRequest!.ImagePath);
        Assert.True(File.Exists(iso));
    }

    [Fact]
    public async Task Local_image_with_a_wrong_hash_is_refused()
    {
        var fakes = Create();

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Run(fakes, new LocalImageSource(CreateIsoFile(), new string('0', 64))));

        Assert.Equal(VerificationFailure.ChecksumMismatch, ex.Failure);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task A_file_that_is_not_an_iso_is_refused()
    {
        var fakes = Create();

        await Assert.ThrowsAsync<VerificationException>(() => Run(fakes, new LocalImageSource(Fixtures.PathOf("payload.bin"), null)));

        Assert.Empty(_calls);
    }

    private string CreateIsoFile()
    {
        var data = new byte[40 * 2048];
        data[510] = 0x55;
        data[511] = 0xAA;
        data[16 * 2048] = 1;
        "CD001"u8.CopyTo(data.AsSpan((16 * 2048) + 1));
        var path = _temp.File("local.iso");
        File.WriteAllBytes(path, data);
        return path;
    }

    private sealed class FakeResolver(ResolvedImage image, List<string> calls) : IImageResolver
    {
        public Task<ResolvedImage> ResolveAsync(Distro distro, CancellationToken cancellationToken = default)
        {
            calls.Add("resolve");
            return Task.FromResult(image);
        }
    }

    private sealed class FakeProbe(List<string> calls) : IUrlProbe
    {
        public HashSet<Uri> Unreachable { get; } = [];

        public Task<UrlProbeResult> ProbeAsync(Uri url, CancellationToken cancellationToken = default)
        {
            lock (calls)
            {
                calls.Add("probe");
            }

            var ok = !Unreachable.Contains(url);
            return Task.FromResult(new UrlProbeResult(url, ok, ok ? 200 : 404, ok ? 1000 : null, ok, url, ok ? null : "HTTP 404", TimeSpan.Zero));
        }
    }

    private sealed class FakeDownloader(List<string> calls) : IDownloader
    {
        public bool Resumed { get; set; }

        public DownloadRequest? LastRequest { get; private set; }

        public async Task<DownloadResult> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("download");
            LastRequest = request;
            await File.WriteAllBytesAsync(request.DestinationPath, new byte[1000], cancellationToken);
            progress?.Report(new DownloadProgress(1000, 1000, 1, TimeSpan.Zero, request.Urls[0], 0, 1));
            return new DownloadResult(request.DestinationPath, 1000, request.Urls[0], Resumed);
        }

        public void DeletePartial(string destinationPath)
        {
        }
    }

    private sealed class FakeVerifier(List<string> calls) : IImageVerifier
    {
        public Queue<VerificationFailure> Failures { get; } = new();

        public Task<ImageVerificationResult> VerifyAsync(string isoPath, ResolvedImage image, IProgress<VerificationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("verify");
            if (Failures.TryDequeue(out var failure))
            {
                throw new VerificationException(failure, failure.ToString());
            }

            return Task.FromResult(new ImageVerificationResult(HashAlgorithmKind.Sha256, image.Hash, SignatureStatus.NotProvided, null, null));
        }
    }

    private sealed class FakeWriter(List<string> calls) : IUsbWriter
    {
        public string Name => "fake";

        public UsbWriteRequest? LastRequest { get; private set; }

        public Task<UsbWriteResult> WriteAsync(UsbWriteRequest request, IProgress<UsbWriteProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            calls.Add("write");
            LastRequest = request;
            progress?.Report(new UsbWriteProgress(UsbWriteStage.Writing, 1000, 1000));
            return Task.FromResult(new UsbWriteResult(1000, "abc", request.VerifyAfterWrite));
        }
    }

    private sealed class FakeEjector(List<string> calls) : IDiskEjector
    {
        public string? Error { get; set; }

        public Task<string?> EjectAsync(DiskInfo disk, CancellationToken cancellationToken = default)
        {
            calls.Add("eject");
            return Task.FromResult(Error);
        }
    }
}
