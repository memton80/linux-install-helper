using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.Core.Tests.Catalog;

public class DistroFilterTests
{
    private static readonly IReadOnlyList<Distro> Distros = EmbeddedCatalog();

    private static IReadOnlyList<Distro> EmbeddedCatalog() =>
        CatalogSerializer.Deserialize(EmbeddedResources.ReadCatalogJson()).Distros;

    [Fact]
    public void Empty_query_returns_everything_in_catalog_order()
    {
        var result = DistroFilter.Apply(Distros, DistroQuery.All);

        Assert.Equal(Distros.Select(d => d.Id), result.Select(d => d.Id));
    }

    [Fact]
    public void Text_search_is_case_and_accent_insensitive()
    {
        Assert.Contains(DistroFilter.Apply(Distros, new DistroQuery("UBUNTU")), d => d.Id == "ubuntu-desktop");
        Assert.Contains(DistroFilter.Apply(Distros, new DistroQuery("leger"), "fr"), d => d.Id == "lubuntu");
    }

    [Fact]
    public void All_terms_must_match()
    {
        var result = DistroFilter.Apply(Distros, new DistroQuery("mint xfce"));

        var single = Assert.Single(result);
        Assert.Equal("linuxmint-xfce", single.Id);
    }

    [Fact]
    public void Family_filter_keeps_only_that_family()
    {
        var result = DistroFilter.Apply(Distros, new DistroQuery(Family: DistroFamilies.Arch));

        Assert.NotEmpty(result);
        Assert.All(result, d => Assert.Equal(DistroFamilies.Arch, d.Family));
    }

    [Theory]
    [InlineData(DistroCategories.Server)]
    [InlineData(DistroCategories.Lightweight)]
    [InlineData(DistroCategories.Desktop)]
    [InlineData(DistroCategories.Security)]
    public void Category_filter_keeps_only_that_category(string category)
    {
        var result = DistroFilter.Apply(Distros, new DistroQuery(Category: category));

        Assert.NotEmpty(result);
        Assert.All(result, d => Assert.Contains(category, d.Categories));
    }

    [Fact]
    public void Filters_combine()
    {
        var intel = DistroFilter.Apply(Distros, new DistroQuery("debian", DistroFamilies.Debian, DistroCategories.Server, DistroArchitectures.X64));
        var arm = DistroFilter.Apply(Distros, new DistroQuery("debian", DistroFamilies.Debian, DistroCategories.Server, DistroArchitectures.Arm64));

        Assert.Equal(["debian-netinst"], intel.Select(d => d.Id));
        Assert.Equal(["debian-netinst-arm64"], arm.Select(d => d.Id));
    }

    [Theory]
    [InlineData("ARM")]
    [InlineData("aarch64")]
    public void Searching_arm_finds_the_arm_images_only(string text)
    {
        var result = DistroFilter.Apply(Distros, new DistroQuery(text), "fr");

        Assert.NotEmpty(result);
        Assert.Equal(Distros.Where(d => d.Architecture == DistroArchitectures.Arm64).Select(d => d.Id), result.Select(d => d.Id));
    }

    [Fact]
    public void Unknown_text_returns_nothing()
    {
        Assert.Empty(DistroFilter.Apply(Distros, new DistroQuery("freebsd")));
    }
}
