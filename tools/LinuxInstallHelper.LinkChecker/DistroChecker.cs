using System.Globalization;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Verification;

namespace LinuxInstallHelper.LinkChecker;

internal enum CheckStatus
{
    Ok,
    Warning,
    Error,
}

internal sealed record UrlCheck(Uri Url, UrlProbeResult Probe, string? Problem);

internal sealed class DistroCheckResult
{
    public required Distro Distro { get; init; }

    public CheckStatus Status { get; private set; } = CheckStatus.Ok;

    public List<string> Notes { get; } = [];

    public List<UrlCheck> Urls { get; } = [];

    public ResolvedImage? Resolved { get; set; }

    public long? ServerSize { get; set; }

    public string Icon => Status switch
    {
        CheckStatus.Ok => "✅",
        CheckStatus.Warning => "⚠️",
        _ => "❌",
    };

    public string Summary => Resolved is null
        ? "image could not be resolved"
        : $"{Resolved.FileName} ({FormatSize(ServerSize)}), {Resolved.HashAlgorithm} {SignatureText}";

    public string SignatureText => Resolved?.ChecksumSignature switch
    {
        SignatureStatus.Verified => "signed ✔",
        SignatureStatus.Unavailable => "signature unavailable",
        SignatureStatus.NotProvided => "not signed",
        _ => "-",
    };

    public void Warn(string note)
    {
        Notes.Add("⚠️ " + note);
        if (Status == CheckStatus.Ok)
        {
            Status = CheckStatus.Warning;
        }
    }

    public void Fail(string note)
    {
        Notes.Add("❌ " + note);
        Status = CheckStatus.Error;
    }

    public static string FormatSize(long? bytes) => bytes is null
        ? "size unknown"
        : string.Create(CultureInfo.InvariantCulture, $"{bytes.Value / 1024d / 1024d / 1024d:0.00} GiB, {bytes.Value} bytes");
}

internal sealed class DistroChecker(IImageResolver resolver, IUrlProbe probe)
{
    public async Task<DistroCheckResult> CheckAsync(Distro distro, CancellationToken cancellationToken)
    {
        var result = new DistroCheckResult { Distro = distro };
        var image = distro.Image;

        try
        {
            result.Resolved = await resolver.ResolveAsync(distro, cancellationToken);
            if (result.Resolved.Warning is not null)
            {
                result.Warn(result.Resolved.Warning);
            }

            if (image.Signature?.Target == SignatureTargets.Checksum && result.Resolved.ChecksumSignature != SignatureStatus.Verified)
            {
                result.Warn("The checksum signature could not be verified.");
            }

            if (result.Resolved.IsNewerThanCatalog && !image.LatestAlias)
            {
                result.Warn($"Newer image available: {result.Resolved.FileName} (catalog: {image.FileName}). Please update the catalog.");
            }
        }
        catch (VerificationException ex)
        {
            result.Fail($"{ex.Failure}: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            result.Fail(ex.Message);
        }

        var urls = result.Resolved?.Urls ?? image.Urls.Select(u => new Uri(u)).ToList();
        long? expected = result.Resolved is not null
            ? result.Resolved.Size
            : image.LatestAlias ? null : image.Size;

        foreach (var url in urls)
        {
            var probed = await probe.ProbeAsync(url, cancellationToken);
            string? problem = null;
            if (!probed.IsReachable)
            {
                problem = probed.Error ?? "unreachable";
            }
            else if (probed.ContentLength is null)
            {
                problem = "the server does not report the file size";
            }
            else if (expected is not null && probed.ContentLength != expected)
            {
                problem = string.Create(CultureInfo.InvariantCulture, $"size mismatch: server {probed.ContentLength}, catalog {expected}");
            }
            else if (expected is null && Math.Abs(probed.ContentLength.Value - image.Size) > image.Size / 4)
            {
                result.Warn(string.Create(CultureInfo.InvariantCulture, $"{url} is {probed.ContentLength} bytes, far from the catalog size {image.Size}."));
            }

            result.ServerSize ??= probed.IsReachable ? probed.ContentLength : null;
            result.Urls.Add(new UrlCheck(url, probed, problem));
        }

        var working = result.Urls.Where(u => u.Problem is null).ToList();
        if (working.Count == 0)
        {
            result.Fail("No download URL works: " + string.Join("; ", result.Urls.Select(u => $"{u.Url} → {u.Problem}")));
        }
        else
        {
            foreach (var broken in result.Urls.Where(u => u.Problem is not null))
            {
                result.Warn($"Mirror {broken.Url} → {broken.Problem}");
            }

            if (working.All(u => !u.Probe.AcceptsRanges))
            {
                result.Warn("No mirror announces byte ranges: interrupted downloads cannot be resumed.");
            }
        }

        if (result.Resolved?.ImageSignatureUrl is { } signatureUrl)
        {
            var signature = await probe.ProbeAsync(signatureUrl, cancellationToken);
            if (!signature.IsReachable)
            {
                result.Fail($"Image signature {signatureUrl} → {signature.Error}");
            }
        }

        return result;
    }
}
