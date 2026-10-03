using System.Net;
using System.Net.Http.Headers;

namespace LinuxInstallHelper.Core.Http;

/// <summary>Creates the HTTP clients used by the application (HTTPS only, sane timeouts, identifiable user agent).</summary>
public static class HttpClientFactory
{
    public const string ProductName = "LinuxInstallHelper";

    /// <summary>
    /// Handler shared by every client. Redirects are followed (mirrors rely on them) but .NET never follows a
    /// redirect from HTTPS to HTTP. Responses are not decompressed so that byte ranges stay exact.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ResponseDrainTimeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Client without a global timeout: long downloads manage their own timeouts (stall detection),
    /// short requests use <see cref="HttpTimeouts"/> through cancellation tokens.
    /// </summary>
    public static HttpClient Create(string version, HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
        };
        ConfigureClient(client, version);
        return client;
    }

    public static void ConfigureClient(HttpClient client, string version)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductName, version));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/memton80/linux-install-helper)"));
    }
}

/// <summary>Timeouts for the short requests (catalog, checksum files, link probes).</summary>
public static class HttpTimeouts
{
    public static TimeSpan Catalog { get; } = TimeSpan.FromSeconds(10);

    public static TimeSpan SmallFile { get; } = TimeSpan.FromSeconds(30);

    public static TimeSpan Probe { get; } = TimeSpan.FromSeconds(20);
}
