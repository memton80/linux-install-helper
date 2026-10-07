using System.Net.Http.Headers;
using System.Text.Json;
using LinuxInstallHelper.Core.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Updates;

/// <summary>A published version of the application.</summary>
/// <param name="Version">Its version number.</param>
/// <param name="Tag">Its Git tag (<c>v1.0.4</c>).</param>
/// <param name="Page">Its release page, where the .exe files are.</param>
public sealed record AppRelease(Version Version, string Tag, Uri Page);

public interface IAppUpdateChecker
{
    /// <summary>The latest release when it is newer than <paramref name="currentVersion"/>, otherwise null (also when offline).</summary>
    Task<AppRelease?> FindNewerAsync(string currentVersion, CancellationToken cancellationToken = default);
}

/// <summary>Asks GitHub for the latest release of the application (pre-releases and drafts are never offered).</summary>
public sealed class AppUpdateChecker : IAppUpdateChecker
{
    public const string LatestReleaseUrl = "https://api.github.com/repos/memton80/linux-install-helper/releases/latest";

    // Release pages must stay on the repository: the link is opened in the browser.
    private const string ReleasesPrefix = "https://github.com/memton80/linux-install-helper/releases/";

    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public AppUpdateChecker(HttpClient http, ILogger<AppUpdateChecker>? logger = null)
    {
        _http = http;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<AppRelease?> FindNewerAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        if (ParseVersion(currentVersion) is not { } current)
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HttpTimeouts.SmallFile);
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("No release information ({Status})", (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var latest = ParseRelease(json);
            if (latest is not null && latest.Version > current)
            {
                _logger.LogInformation("Version {Latest} is available (running {Current})", latest.Version, current);
                return latest;
            }

            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Could not check for a new version: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>Reads the answer of the GitHub "latest release" API, or null when it is not a usable release.</summary>
    public static AppRelease? ParseRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Bool(root, "draft") || Bool(root, "prerelease")
                || !root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag
                || ParseVersion(tag) is not { } version)
            {
                return null;
            }

            var page = root.TryGetProperty("html_url", out var url) && url.GetString() is { } link
                && link.StartsWith(ReleasesPrefix, StringComparison.Ordinal) && Uri.TryCreate(link, UriKind.Absolute, out var uri)
                    ? uri
                    : new Uri(ReleasesPrefix + "latest");
            return new AppRelease(version, tag, page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v1.0.4", "V1.0.4" or "1.0.4" as a version with at least three parts.</summary>
    public static Version? ParseVersion(string? text)
    {
        var value = text?.Trim().TrimStart('v', 'V');
        if (string.IsNullOrEmpty(value) || !Version.TryParse(value.Split('-', '+')[0], out var version))
        {
            return null;
        }

        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
