using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.Core.Tests.Helpers;

public static class TestDistros
{
    public static DistroCatalog Embedded() => CatalogSerializer.Deserialize(EmbeddedResources.ReadCatalogJson());

    public static Distro Get(string id) => Embedded().Find(id) ?? throw new InvalidOperationException(id);

    public static Distro WithImage(Distro source, DistroImage image) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Edition = source.Edition,
        Version = source.Version,
        Family = source.Family,
        Categories = source.Categories,
        Desktop = source.Desktop,
        Description = source.Description,
        Homepage = source.Homepage,
        Color = source.Color,
        Architecture = source.Architecture,
        SecureBoot = source.SecureBoot,
        Requirements = source.Requirements,
        Image = image,
    };

    public static DistroImage Copy(
        DistroImage source,
        string? fileName = null,
        IReadOnlyList<string>? urls = null,
        string? sha256 = null,
        ChecksumSource? checksum = null,
        SignatureSource? signature = null,
        ImageResolveRule? resolve = null,
        bool clearChecksum = false,
        bool clearSignature = false,
        bool clearResolve = false) => new()
    {
        FileName = fileName ?? source.FileName,
        Size = source.Size,
        Urls = urls ?? source.Urls,
        Sha256 = sha256 ?? source.Sha256,
        Checksum = clearChecksum ? null : checksum ?? source.Checksum,
        Signature = clearSignature ? null : signature ?? source.Signature,
        Resolve = clearResolve ? null : resolve ?? source.Resolve,
        Hybrid = source.Hybrid,
    };
}
