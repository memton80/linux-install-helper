using LinuxInstallHelper.Core.Http;
using LinuxInstallHelper.Core.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Verification;

/// <param name="BytesProcessed">Bytes of the ISO read so far.</param>
/// <param name="TotalBytes">Size of the ISO.</param>
public sealed record VerificationProgress(long BytesProcessed, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesProcessed / TotalBytes, 0, 1) : 0;
}

public sealed record ImageVerificationResult(
    HashAlgorithmKind Algorithm,
    string Hash,
    SignatureStatus ImageSignature,
    string? Signer,
    string? Warning);

public interface IImageVerifier
{
    /// <summary>
    /// Checks the hash of the downloaded ISO and, when the distribution signs the image itself, its
    /// OpenPGP signature (both in a single read). Throws <see cref="VerificationException"/> on mismatch.
    /// </summary>
    Task<ImageVerificationResult> VerifyAsync(string isoPath, ResolvedImage image, IProgress<VerificationProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class ImageVerifier : IImageVerifier
{
    private readonly HttpClient _http;
    private readonly IPublicKeyStore _keys;
    private readonly ILogger _logger;

    public ImageVerifier(HttpClient http, IPublicKeyStore keys, ILogger<ImageVerifier>? logger = null)
    {
        _http = http;
        _keys = keys;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<ImageVerificationResult> VerifyAsync(
        string isoPath,
        ResolvedImage image,
        IProgress<VerificationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var total = new FileInfo(isoPath).Length;
        var byteProgress = progress is null ? null : new Progress<long>(read => progress.Report(new VerificationProgress(read, total)));

        var (signatureBytes, keys, warning) = await PrepareSignatureAsync(image, cancellationToken).ConfigureAwait(false);

        string hash;
        SignatureCheckResult? signature = null;
        await using (var file = FileHasher.OpenSequential(isoPath))
        using (var hashing = new HashingStream(file, image.HashAlgorithm))
        {
            if (signatureBytes is not null && keys.Count > 0)
            {
                signature = await OpenPgpVerifier.VerifyDetachedAsync(hashing, signatureBytes, keys, byteProgress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var buffer = new byte[FileHasher.BufferSize];
                long read = 0;
                int n;
                while ((n = await hashing.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    read += n;
                    progress?.Report(new VerificationProgress(read, total));
                }
            }

            hash = hashing.GetHashHex();
        }

        if (!FileHasher.HashEquals(image.Hash, hash))
        {
            _logger.LogError("Checksum mismatch for {File}: expected {Expected}, got {Actual}", isoPath, image.Hash, hash);
            throw new VerificationException(
                VerificationFailure.ChecksumMismatch,
                $"The {image.HashAlgorithm} of {Path.GetFileName(isoPath)} does not match the official value.\nExpected: {image.Hash}\nActual:   {hash}");
        }

        if (signature is { IsValid: false })
        {
            throw new VerificationException(VerificationFailure.BadSignature, $"The OpenPGP signature of the image is not valid: {signature.Error}");
        }

        var status = signature?.IsValid == true
            ? SignatureStatus.Verified
            : image.ImageSignatureUrl is null ? SignatureStatus.NotProvided : SignatureStatus.Unavailable;

        _logger.LogInformation("{File} verified ({Algorithm} {Hash}, image signature {Status})", isoPath, image.HashAlgorithm, hash, status);
        return new ImageVerificationResult(image.HashAlgorithm, hash, status, signature?.SignerFingerprint, warning);
    }

    private async Task<(byte[]? Signature, IReadOnlyList<Org.BouncyCastle.Bcpg.OpenPgp.PgpPublicKeyRing> Keys, string? Warning)> PrepareSignatureAsync(
        ResolvedImage image,
        CancellationToken cancellationToken)
    {
        if (image.ImageSignatureUrl is null)
        {
            return (null, [], null);
        }

        var keys = await _keys.GetKeysAsync(image.ImageSignatureFingerprints, cancellationToken).ConfigureAwait(false);
        if (keys.Count == 0)
        {
            return (null, keys, "The OpenPGP key of the distribution is not available: only the checksum is verified.");
        }

        try
        {
            var signature = await _http.GetSmallFileAsync(image.ImageSignatureUrl, cancellationToken, maxBytes: 256 * 1024).ConfigureAwait(false);
            return (signature, keys, null);
        }
        catch (Exception ex) when (ImageResolver.IsNetworkError(ex))
        {
            _logger.LogWarning(ex, "Image signature {Url} is not available", image.ImageSignatureUrl);
            return (null, keys, $"The OpenPGP signature of the image could not be downloaded ({ex.Message}): only the checksum is verified.");
        }
    }
}
