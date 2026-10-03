using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Verification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Images;

public interface IImageResolver
{
    /// <summary>
    /// Finds the exact ISO to download and the SHA-256 it must have. The checksum file signature is
    /// verified here; a bad signature throws <see cref="VerificationException"/>.
    /// </summary>
    Task<ResolvedImage> ResolveAsync(Distro distro, CancellationToken cancellationToken = default);
}

public sealed partial class ImageResolver : IImageResolver
{
    private const int MaxJsonBytes = 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly HttpClient _http;
    private readonly IPublicKeyStore _keys;
    private readonly ILogger _logger;

    public ImageResolver(HttpClient http, IPublicKeyStore keys, ILogger<ImageResolver>? logger = null)
    {
        _http = http;
        _keys = keys;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<ResolvedImage> ResolveAsync(Distro distro, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(distro);
        var image = distro.Image;

        if (image.Resolve?.Type == ResolveTypes.Json)
        {
            return await ResolveFromJsonAsync(distro, image.Resolve, cancellationToken).ConfigureAwait(false);
        }

        var fileName = image.FileName;
        string? version = null;
        var sha256 = image.Sha256?.ToLowerInvariant();
        var signatureStatus = SignatureStatus.NotProvided;
        string? signer = null;
        string? warning = null;

        if (image.Checksum is not null)
        {
            var checksumUrl = HttpExtensions.RequireHttps(ReplaceFileName(image.Checksum.Url, image.FileName));
            byte[] raw;
            try
            {
                raw = await _http.GetSmallFileAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                throw new VerificationException(
                    VerificationFailure.ChecksumUnavailable,
                    $"The checksum file {checksumUrl} cannot be downloaded: {ex.Message}",
                    ex);
            }

            var checkedFile = await ReadChecksumFileAsync(image, raw, cancellationToken).ConfigureAwait(false);
            signatureStatus = checkedFile.Status;
            signer = checkedFile.Signer;
            warning = checkedFile.Warning;

            var checksums = ChecksumFile.Parse(checkedFile.Content);
            (fileName, version) = SelectFile(image, checksums);
            var listed = checksums.Find(fileName)
                ?? throw new VerificationException(VerificationFailure.NotListed, $"{fileName} is not listed in {checksumUrl}.");

            if (sha256 is not null && fileName == image.FileName && !string.Equals(sha256, listed, StringComparison.Ordinal))
            {
                throw new VerificationException(
                    VerificationFailure.ChecksumMismatch,
                    $"The SHA-256 in the catalog does not match the official checksum file for {fileName}.");
            }

            sha256 = listed;
        }
        else if (image.Resolve?.Type == ResolveTypes.ChecksumPattern && image.Resolve.Pattern is not null)
        {
            version = CaptureVersion(image.Resolve.Pattern, fileName);
        }

        if (sha256 is null)
        {
            throw new VerificationException(VerificationFailure.ChecksumUnavailable, $"No SHA-256 is known for {fileName}.");
        }

        if (fileName != image.FileName)
        {
            _logger.LogInformation("{Distro}: newer image {FileName} found (catalog has {CatalogFile})", distro.Id, fileName, image.FileName);
        }

        return new ResolvedImage
        {
            Distro = distro,
            FileName = fileName,
            Version = version,
            Size = fileName == image.FileName ? image.Size : null,
            Urls = BuildUrls(image, fileName),
            Sha256 = sha256,
            ChecksumSignature = signatureStatus,
            ChecksumSigner = signer,
            Warning = warning,
            ImageSignatureUrl = ImageSignatureUrl(image, fileName),
            ImageSignatureFingerprints = image.Signature?.Target == SignatureTargets.Image ? image.Signature.Fingerprints : [],
        };
    }

    /// <summary>Picks the file to download: the catalog's one when still listed, otherwise the newest pattern match.</summary>
    internal static (string FileName, string? Version) SelectFile(DistroImage image, ChecksumFile checksums)
    {
        var pattern = image.Resolve?.Type == ResolveTypes.ChecksumPattern ? image.Resolve.Pattern : null;

        if (checksums.Find(image.FileName) is not null)
        {
            return (image.FileName, pattern is null ? null : CaptureVersion(pattern, image.FileName));
        }

        if (pattern is not null)
        {
            var regex = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            var best = checksums.FileNames
                .Select(name => (Name: name, Match: regex.Match(name)))
                .Where(x => x.Match.Success)
                .OrderByDescending(x => x.Match.Groups["version"].Success ? x.Match.Groups["version"].Value : string.Empty, VersionComparer.Instance)
                .ThenByDescending(x => x.Name, VersionComparer.Instance)
                .FirstOrDefault();

            if (best.Name is not null)
            {
                return (best.Name, best.Match.Groups["version"].Success ? best.Match.Groups["version"].Value : null);
            }
        }

        throw new VerificationException(VerificationFailure.NotListed, $"{image.FileName} is not listed in the official checksum file.");
    }

    /// <summary>Replaces the file name at the end of each catalog URL when a newer image was found.</summary>
    internal static IReadOnlyList<Uri> BuildUrls(DistroImage image, string fileName)
    {
        var urls = new List<Uri>();
        foreach (var url in image.Urls)
        {
            var uri = HttpExtensions.RequireHttps(url);
            if (fileName != image.FileName)
            {
                var path = uri.AbsolutePath;
                var slash = path.LastIndexOf('/');
                var last = Uri.UnescapeDataString(path[(slash + 1)..]);
                if (string.Equals(last, image.FileName, StringComparison.Ordinal))
                {
                    var builder = new UriBuilder(uri) { Path = path[..(slash + 1)] + Uri.EscapeDataString(fileName) };
                    uri = builder.Uri;
                }
            }

            if (!urls.Contains(uri))
            {
                urls.Add(uri);
            }
        }

        return urls;
    }

    private static Uri? ImageSignatureUrl(DistroImage image, string fileName) =>
        image.Signature is { Target: SignatureTargets.Image, Url: not null } signature
            ? HttpExtensions.RequireHttps(ReplaceFileName(signature.Url, fileName))
            : null;

    private sealed record ChecksumContent(string Content, SignatureStatus Status, string? Signer, string? Warning);

    private async Task<ChecksumContent> ReadChecksumFileAsync(DistroImage image, byte[] raw, CancellationToken cancellationToken)
    {
        var text = HttpExtensions.DecodeText(raw);
        var signature = image.Signature;

        if (signature is null || signature.Target != SignatureTargets.Checksum)
        {
            var content = OpenPgpVerifier.IsClearSigned(text) ? OpenPgpVerifier.VerifyClearSigned(text, []).Content : text;
            return new ChecksumContent(content, SignatureStatus.NotProvided, null, null);
        }

        var keys = await _keys.GetKeysAsync(signature.Fingerprints, cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
        {
            var content = OpenPgpVerifier.IsClearSigned(text) ? OpenPgpVerifier.VerifyClearSigned(text, []).Content : text;
            return new ChecksumContent(content, SignatureStatus.Unavailable, null, "The OpenPGP key of the distribution is not available: only the SHA-256 is checked.");
        }

        if (signature.Kind == SignatureKinds.ClearSigned)
        {
            var result = OpenPgpVerifier.VerifyClearSigned(text, keys);
            if (!result.Signature.IsValid)
            {
                throw new VerificationException(VerificationFailure.BadSignature, $"The checksum file signature is not valid: {result.Signature.Error}");
            }

            return new ChecksumContent(result.Content, SignatureStatus.Verified, result.Signature.SignerFingerprint, null);
        }

        var signatureUrl = HttpExtensions.RequireHttps(ReplaceFileName(signature.Url!, image.FileName));
        byte[] signatureBytes;
        try
        {
            signatureBytes = await _http.GetSmallFileAsync(signatureUrl, cancellationToken, maxBytes: 256 * 1024).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            _logger.LogWarning(ex, "Signature {Url} is not available", signatureUrl);
            return new ChecksumContent(text, SignatureStatus.Unavailable, null, $"The OpenPGP signature could not be downloaded ({ex.Message}): only the SHA-256 is checked.");
        }

        var check = OpenPgpVerifier.VerifyDetached(raw, signatureBytes, keys);
        if (!check.IsValid)
        {
            throw new VerificationException(VerificationFailure.BadSignature, $"The checksum file signature is not valid: {check.Error}");
        }

        return new ChecksumContent(text, SignatureStatus.Verified, check.SignerFingerprint, null);
    }

    private async Task<ResolvedImage> ResolveFromJsonAsync(Distro distro, ImageResolveRule rule, CancellationToken cancellationToken)
    {
        var apiUrl = HttpExtensions.RequireHttps(rule.Url!);
        string json;
        try
        {
            json = await _http.GetSmallTextAsync(apiUrl, cancellationToken, maxBytes: MaxJsonBytes).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsNetworkError(ex))
        {
            throw new VerificationException(VerificationFailure.ChecksumUnavailable, $"{apiUrl} cannot be reached: {ex.Message}", ex);
        }

        string url, sha256;
        string? build;
        long? size;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            url = ReadField(root, rule.UrlField!) ?? throw new InvalidDataException($"'{rule.UrlField}' is missing.");
            sha256 = ReadField(root, rule.Sha256Field!) ?? throw new InvalidDataException($"'{rule.Sha256Field}' is missing.");
            build = rule.VersionField is null ? null : ReadField(root, rule.VersionField);
            size = rule.SizeField is not null && long.TryParse(ReadField(root, rule.SizeField), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new VerificationException(VerificationFailure.ChecksumUnavailable, $"{apiUrl} returned unexpected data: {ex.Message}", ex);
        }

        if (!Sha256Regex().IsMatch(sha256))
        {
            throw new VerificationException(VerificationFailure.ChecksumUnavailable, $"{apiUrl} returned an invalid SHA-256.");
        }

        var imageUri = HttpExtensions.RequireHttps(url);
        var fileName = Path.GetFileName(Uri.UnescapeDataString(imageUri.AbsolutePath));
        var urls = new List<Uri> { imageUri };
        urls.AddRange(BuildUrls(distro.Image, distro.Image.FileName).Where(u => u != imageUri));

        return new ResolvedImage
        {
            Distro = distro,
            FileName = fileName,
            Build = build,
            Size = size,
            Urls = urls,
            Sha256 = sha256.ToLowerInvariant(),
            ChecksumSignature = SignatureStatus.NotProvided,
        };
    }

    /// <summary>Reads a string or number at a dotted path ("a.b.c").</summary>
    internal static string? ReadField(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null,
        };
    }

    private static string? CaptureVersion(string pattern, string fileName)
    {
        var match = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout).Match(fileName);
        return match.Success && match.Groups["version"].Success ? match.Groups["version"].Value : null;
    }

    private static string ReplaceFileName(string url, string fileName) =>
        url.Replace(DistroImage.FileNamePlaceholder, Uri.EscapeDataString(fileName), StringComparison.Ordinal);

    internal static bool IsNetworkError(Exception ex) =>
        ex is HttpRequestException or TimeoutException or IOException or InvalidDataException;

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
