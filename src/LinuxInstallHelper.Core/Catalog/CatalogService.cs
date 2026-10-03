using System.Text;
using LinuxInstallHelper.Core.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Catalog;

public enum CatalogSource
{
    /// <summary>Freshly downloaded from the GitHub repository.</summary>
    Remote,

    /// <summary>Last catalog downloaded successfully, stored in <c>%LOCALAPPDATA%</c>.</summary>
    Cache,

    /// <summary>Catalog embedded in the application (offline fallback).</summary>
    Embedded,
}

public sealed record CatalogLoadResult(DistroCatalog Catalog, CatalogSource Source, string? RemoteError);

public interface ICatalogService
{
    /// <summary>Last loaded catalog, null before the first <see cref="LoadAsync"/>.</summary>
    CatalogLoadResult? Current { get; }

    /// <summary>Downloads the latest catalog, falling back to the cached then embedded copy.</summary>
    Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class CatalogServiceOptions
{
    public const string DefaultRemoteUrl =
        "https://raw.githubusercontent.com/memton80/linux-install-helper/main/catalog/distros.json";

    public Uri RemoteUri { get; init; } = new(DefaultRemoteUrl);

    public TimeSpan RemoteTimeout { get; init; } = HttpTimeouts.Catalog;

    public bool EnableRemote { get; init; } = true;
}

public sealed class CatalogService : ICatalogService
{
    public const string CacheFileName = "distros.json";
    private const int MaxCatalogBytes = 2 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly CatalogServiceOptions _options;
    private readonly ILogger<CatalogService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CatalogService(HttpClient http, AppPaths paths, CatalogServiceOptions? options = null, ILogger<CatalogService>? logger = null)
    {
        _http = http;
        _paths = paths;
        _options = options ?? new CatalogServiceOptions();
        _logger = logger ?? NullLogger<CatalogService>.Instance;
    }

    public CatalogLoadResult? Current { get; private set; }

    private string CachePath => Path.Combine(_paths.CatalogCache, CacheFileName);

    public async Task<CatalogLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var validator = await CatalogValidator.GetDefaultAsync().ConfigureAwait(false);
            string? remoteError = null;

            if (_options.EnableRemote)
            {
                try
                {
                    var json = await _http.GetSmallTextAsync(_options.RemoteUri, cancellationToken, _options.RemoteTimeout, MaxCatalogBytes)
                        .ConfigureAwait(false);
                    var remote = validator.ParseAndValidate(json);
                    await SaveCacheAsync(json, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Catalog downloaded from {Url} ({Count} distributions, updated {Updated})", _options.RemoteUri, remote.Distros.Count, remote.Updated);
                    return Current = new CatalogLoadResult(remote, CatalogSource.Remote, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    remoteError = ex is CatalogException { Errors.Count: > 0 } ce
                        ? $"{ce.Message} {string.Join(" ", ce.Errors.Take(3))}"
                        : ex.Message;
                    _logger.LogWarning(ex, "Could not use the online catalog, falling back to a local copy");
                }
            }

            var embedded = validator.ParseAndValidate(EmbeddedResources.ReadCatalogJson());
            var cached = TryLoadCache(validator);

            var result = cached is not null && cached.UpdatedDate >= embedded.UpdatedDate
                ? new CatalogLoadResult(cached, CatalogSource.Cache, remoteError)
                : new CatalogLoadResult(embedded, CatalogSource.Embedded, remoteError);

            _logger.LogInformation("Using the {Source} catalog (updated {Updated})", result.Source, result.Catalog.Updated);
            return Current = result;
        }
        finally
        {
            _lock.Release();
        }
    }

    private DistroCatalog? TryLoadCache(CatalogValidator validator)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            return validator.ParseAndValidate(File.ReadAllText(CachePath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CatalogException)
        {
            _logger.LogWarning(ex, "Ignoring the cached catalog {Path}", CachePath);
            return null;
        }
    }

    private async Task SaveCacheAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_paths.CatalogCache);
            var temp = CachePath + ".tmp";
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temp, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not cache the catalog in {Path}", CachePath);
        }
    }
}
