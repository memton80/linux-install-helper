using System.Net;
using System.Text.Json.Nodes;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Catalog;

public sealed class CatalogServiceTests : IDisposable
{
    private const string RemoteUrl = CatalogServiceOptions.DefaultRemoteUrl;
    private readonly TempFolder _temp = new();
    private readonly AppPaths _paths;

    public CatalogServiceTests()
    {
        _paths = new AppPaths(_temp.Path);
        _paths.EnsureCreated();
    }

    public void Dispose() => _temp.Dispose();

    private static string CatalogWithDate(string date)
    {
        var json = JsonNode.Parse(EmbeddedResources.ReadCatalogJson())!.AsObject();
        json["updated"] = date;
        return json.ToJsonString();
    }

    private string CacheFile => Path.Combine(_paths.CatalogCache, CatalogService.CacheFileName);

    [Fact]
    public async Task Uses_the_remote_catalog_and_caches_it()
    {
        var handler = new StubHttpHandler().Add(RemoteUrl, CatalogWithDate("2099-01-01"));
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Remote, result.Source);
        Assert.Equal("2099-01-01", result.Catalog.Updated);
        Assert.Null(result.RemoteError);
        Assert.True(File.Exists(CacheFile));
        Assert.Same(result, service.Current);
    }

    [Fact]
    public async Task Falls_back_to_the_embedded_catalog_when_offline()
    {
        var handler = new StubHttpHandler().AddStatus(RemoteUrl, HttpStatusCode.ServiceUnavailable);
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Embedded, result.Source);
        Assert.NotNull(result.RemoteError);
    }

    [Fact]
    public async Task Prefers_a_newer_cached_catalog_when_offline()
    {
        await File.WriteAllTextAsync(CacheFile, CatalogWithDate("2099-06-01"));
        var handler = new StubHttpHandler().AddStatus(RemoteUrl, HttpStatusCode.NotFound);
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.Equal("2099-06-01", result.Catalog.Updated);
    }

    [Fact]
    public async Task Ignores_a_cached_catalog_older_than_the_embedded_one()
    {
        await File.WriteAllTextAsync(CacheFile, CatalogWithDate("2000-01-01"));
        var handler = new StubHttpHandler().AddStatus(RemoteUrl, HttpStatusCode.NotFound);
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Embedded, result.Source);
    }

    [Fact]
    public async Task Rejects_an_invalid_remote_catalog_and_keeps_the_cache_untouched()
    {
        var handler = new StubHttpHandler().Add(RemoteUrl, "{\"schemaVersion\": 1, \"updated\": \"2099-01-01\", \"distros\": []}");
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Embedded, result.Source);
        Assert.NotNull(result.RemoteError);
        Assert.False(File.Exists(CacheFile));
    }

    [Fact]
    public async Task Ignores_a_corrupted_cache()
    {
        await File.WriteAllTextAsync(CacheFile, "not json");
        var handler = new StubHttpHandler().AddStatus(RemoteUrl, HttpStatusCode.NotFound);
        var service = new CatalogService(handler.CreateClient(), _paths);

        var result = await service.LoadAsync();

        Assert.Equal(CatalogSource.Embedded, result.Source);
    }
}
