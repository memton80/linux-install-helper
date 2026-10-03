using System.Text.Json.Nodes;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Catalog;

public class CatalogValidationTests
{
    private static async Task<CatalogValidator> Validator() => await CatalogValidator.GetDefaultAsync();

    private static JsonObject RepositoryCatalog() =>
        JsonNode.Parse(Fixtures.Text("catalog/distros.json"))!.AsObject();

    private static JsonObject FirstDistro(JsonObject catalog) => catalog["distros"]![0]!.AsObject();

    [Fact]
    public async Task Repository_catalog_is_valid()
    {
        var validator = await Validator();

        var catalog = validator.ParseAndValidate(Fixtures.Text("catalog/distros.json"));

        Assert.Equal(DistroCatalog.SupportedSchemaVersion, catalog.SchemaVersion);
        Assert.True(catalog.Distros.Count >= 10, "The catalog should start with about ten distributions.");
    }

    [Fact]
    public async Task Embedded_catalog_is_the_repository_catalog()
    {
        var validator = await Validator();

        var embedded = validator.ParseAndValidate(EmbeddedResources.ReadCatalogJson());
        var repository = validator.ParseAndValidate(Fixtures.Text("catalog/distros.json"));

        Assert.Equal(repository.Updated, embedded.Updated);
        Assert.Equal(repository.Distros.Select(d => d.Id), embedded.Distros.Select(d => d.Id));
    }

    [Fact]
    public async Task Every_pinned_fingerprint_has_an_embedded_key()
    {
        var catalog = (await Validator()).ParseAndValidate(EmbeddedResources.ReadCatalogJson());
        var embedded = EmbeddedResources.EmbeddedKeyFingerprints();

        var missing = catalog.Distros
            .Where(d => d.Image.Signature is not null)
            .SelectMany(d => d.Image.Signature!.Fingerprints.Select(f => $"{d.Id}: {f}"))
            .Where(entry => !embedded.Contains(entry.Split(": ")[1]))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Every_distribution_can_be_verified_and_uses_official_https_urls()
    {
        var catalog = (await Validator()).ParseAndValidate(EmbeddedResources.ReadCatalogJson());

        foreach (var distro in catalog.Distros)
        {
            var image = distro.Image;
            Assert.True(image.Sha256 is not null || image.Checksum is not null || image.Resolve?.Type == ResolveTypes.Json, distro.Id);
            Assert.All(image.Urls, url => Assert.StartsWith("https://", url));
            Assert.True(image.Hybrid, $"{distro.Id} must be an isohybrid image to be written as-is.");
        }
    }

    [Fact]
    public async Task Missing_required_field_is_rejected_by_the_schema()
    {
        var catalog = RepositoryCatalog();
        FirstDistro(catalog).Remove("version");

        var ex = await Assert.ThrowsAsync<CatalogException>(async () => (await Validator()).ParseAndValidate(catalog.ToJsonString()));

        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task Http_url_is_rejected()
    {
        var catalog = RepositoryCatalog();
        FirstDistro(catalog)["image"]!["urls"]![0] = "http://example.org/ubuntu.iso";

        await Assert.ThrowsAsync<CatalogException>(async () => (await Validator()).ParseAndValidate(catalog.ToJsonString()));
    }

    [Fact]
    public async Task Unknown_schema_version_is_rejected()
    {
        var catalog = RepositoryCatalog();
        catalog["schemaVersion"] = 2;

        await Assert.ThrowsAsync<CatalogException>(async () => (await Validator()).ParseAndValidate(catalog.ToJsonString()));
    }

    [Fact]
    public async Task Invalid_json_is_rejected()
    {
        await Assert.ThrowsAsync<CatalogException>(async () => (await Validator()).ParseAndValidate("{ \"schemaVersion\": 1, "));
    }

    [Fact]
    public async Task Duplicate_ids_are_rejected()
    {
        var catalog = RepositoryCatalog();
        var distros = catalog["distros"]!.AsArray();
        distros.Add(distros[0]!.DeepClone());

        var ex = await Assert.ThrowsAsync<CatalogException>(async () => (await Validator()).ParseAndValidate(catalog.ToJsonString()));

        Assert.Contains(ex.Errors, e => e.Contains("Duplicate id", StringComparison.Ordinal));
    }

    [Fact]
    public void Semantic_rules_catch_what_the_schema_cannot()
    {
        var catalog = CatalogSerializer.Deserialize(Fixtures.Text("catalog/distros.json"));
        var ubuntu = catalog.Distros[0];
        var broken = new DistroCatalog
        {
            SchemaVersion = 1,
            Updated = catalog.Updated,
            Distros =
            [
                new Distro
                {
                    Id = ubuntu.Id,
                    Name = ubuntu.Name,
                    Version = ubuntu.Version,
                    Family = ubuntu.Family,
                    Categories = ubuntu.Categories,
                    Description = ubuntu.Description,
                    Homepage = ubuntu.Homepage,
                    Color = ubuntu.Color,
                    Architecture = ubuntu.Architecture,
                    Image = new DistroImage
                    {
                        FileName = "other-name.iso",
                        Size = ubuntu.Image.Size,
                        Urls = ubuntu.Image.Urls,
                        Checksum = ubuntu.Image.Checksum,
                        Signature = new SignatureSource { Kind = "detached", Target = "checksum", Url = "http://insecure/SHA256SUMS.gpg", Fingerprints = ["abc"] },
                        Resolve = new ImageResolveRule { Type = ResolveTypes.ChecksumPattern, Pattern = "^ubuntu-(?<version>[0-9.]+)-desktop-amd64\\.iso$" },
                    },
                },
            ],
        };

        var errors = CatalogValidator.ValidateSemantics(broken);

        Assert.Contains(errors, e => e.Contains("does not match the resolve pattern", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("detached signature needs an absolute https://", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("invalid fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_regular_expression_is_reported()
    {
        var catalog = CatalogSerializer.Deserialize(Fixtures.Text("catalog/distros.json"));
        var json = Fixtures.Text("catalog/distros.json").Replace("^ubuntu-(?<version>26\\\\.04(\\\\.[0-9]+)*)-desktop-amd64\\\\.iso$", "^ubuntu-([0-9", StringComparison.Ordinal);
        Assert.NotEqual(Fixtures.Text("catalog/distros.json"), json);

        var errors = CatalogValidator.ValidateSemantics(CatalogSerializer.Deserialize(json));

        Assert.Contains(errors, e => e.Contains("invalid resolve pattern", StringComparison.Ordinal));
        Assert.Empty(CatalogValidator.ValidateSemantics(catalog));
    }

    [Fact]
    public void Localized_text_falls_back_to_english()
    {
        var text = new LocalizedText { En = "Hello", Fr = "Bonjour" };

        Assert.Equal("Bonjour", text.Get("fr"));
        Assert.Equal("Hello", text.Get("de"));
        Assert.Equal("Hello", text.Get(null));
    }
}
