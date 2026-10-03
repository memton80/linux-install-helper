using LinuxInstallHelper.Core.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace LinuxInstallHelper.Core.Verification;

public interface IPublicKeyStore
{
    /// <summary>
    /// Returns the key rings for the requested fingerprints. Keys that cannot be found are skipped:
    /// the caller decides what to do when none is available.
    /// </summary>
    Task<IReadOnlyList<PgpPublicKeyRing>> GetKeysAsync(IEnumerable<string> fingerprints, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pinned OpenPGP keys: embedded in the application first, then the local cache, then the
/// <c>catalog/keys</c> folder of the GitHub repository. A key is only accepted when its primary
/// fingerprint is exactly the requested one.
/// </summary>
public sealed class PublicKeyStore : IPublicKeyStore
{
    public const string DefaultRemoteBaseUrl = "https://raw.githubusercontent.com/memton80/linux-install-helper/main/catalog/keys/";

    private readonly HttpClient? _http;
    private readonly string? _cacheFolder;
    private readonly Uri _remoteBase;
    private readonly ILogger _logger;
    private readonly Dictionary<string, PgpPublicKeyRing> _memory = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PublicKeyStore(HttpClient? http = null, AppPaths? paths = null, Uri? remoteBase = null, ILogger<PublicKeyStore>? logger = null)
    {
        _http = http;
        _cacheFolder = paths is null ? null : Path.Combine(paths.CatalogCache, "keys");
        _remoteBase = remoteBase ?? new Uri(DefaultRemoteBaseUrl);
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<IReadOnlyList<PgpPublicKeyRing>> GetKeysAsync(IEnumerable<string> fingerprints, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fingerprints);
        var result = new List<PgpPublicKeyRing>();

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var fingerprint in fingerprints.Select(f => f.ToUpperInvariant()).Distinct())
            {
                var ring = await FindAsync(fingerprint, cancellationToken).ConfigureAwait(false);
                if (ring is not null)
                {
                    result.Add(ring);
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        return result;
    }

    private async Task<PgpPublicKeyRing?> FindAsync(string fingerprint, CancellationToken cancellationToken)
    {
        if (_memory.TryGetValue(fingerprint, out var known))
        {
            return known;
        }

        var ring = TryParse(EmbeddedResources.TryReadKey(fingerprint), fingerprint, "embedded")
            ?? TryParse(ReadCache(fingerprint), fingerprint, "cache")
            ?? await DownloadAsync(fingerprint, cancellationToken).ConfigureAwait(false);

        if (ring is not null)
        {
            _memory[fingerprint] = ring;
        }
        else
        {
            _logger.LogWarning("OpenPGP key {Fingerprint} is not available", fingerprint);
        }

        return ring;
    }

    private string? ReadCache(string fingerprint)
    {
        if (_cacheFolder is null)
        {
            return null;
        }

        var path = Path.Combine(_cacheFolder, fingerprint + ".asc");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<PgpPublicKeyRing?> DownloadAsync(string fingerprint, CancellationToken cancellationToken)
    {
        if (_http is null)
        {
            return null;
        }

        try
        {
            var url = new Uri(_remoteBase, fingerprint + ".asc");
            var text = await _http.GetSmallTextAsync(url, cancellationToken, maxBytes: 512 * 1024).ConfigureAwait(false);
            var ring = TryParse(text, fingerprint, "remote");
            if (ring is not null && _cacheFolder is not null)
            {
                Directory.CreateDirectory(_cacheFolder);
                await File.WriteAllTextAsync(Path.Combine(_cacheFolder, fingerprint + ".asc"), text, cancellationToken).ConfigureAwait(false);
            }

            return ring;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not download OpenPGP key {Fingerprint}", fingerprint);
            return null;
        }
    }

    private PgpPublicKeyRing? TryParse(string? armored, string fingerprint, string origin)
    {
        if (string.IsNullOrWhiteSpace(armored))
        {
            return null;
        }

        try
        {
            var ring = OpenPgpVerifier.LoadKeyRings(armored)
                .FirstOrDefault(r => string.Equals(OpenPgpVerifier.Fingerprint(r), fingerprint, StringComparison.Ordinal));
            if (ring is null)
            {
                _logger.LogWarning("The {Origin} key file for {Fingerprint} contains another key: ignored", origin, fingerprint);
            }

            return ring;
        }
        catch (Exception ex) when (ex is IOException or PgpException)
        {
            _logger.LogWarning(ex, "The {Origin} key file for {Fingerprint} is not valid", origin, fingerprint);
            return null;
        }
    }
}
