using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LinuxInstallHelper.Core.Catalog;

/// <summary>Root of <c>catalog/distros.json</c>. See <c>catalog/README.md</c> for the field reference.</summary>
public sealed class DistroCatalog
{
    /// <summary>Schema version understood by this build of the application.</summary>
    public const int SupportedSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    /// <summary>Date of the last change, <c>yyyy-MM-dd</c>.</summary>
    public required string Updated { get; init; }

    public required IReadOnlyList<Distro> Distros { get; init; }

    [JsonIgnore]
    public DateOnly UpdatedDate =>
        DateOnly.TryParseExact(Updated, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateOnly.MinValue;

    public Distro? Find(string id) => Distros.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));
}

public sealed class Distro
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Edition { get; init; }

    public required string Version { get; init; }

    /// <summary>One of <see cref="DistroFamilies"/>.</summary>
    public required string Family { get; init; }

    /// <summary>Values from <see cref="DistroCategories"/>.</summary>
    public required IReadOnlyList<string> Categories { get; init; }

    public string? Desktop { get; init; }

    public required LocalizedText Description { get; init; }

    public required string Homepage { get; init; }

    /// <summary>Brand color (<c>#RRGGBB</c>) used for the badge.</summary>
    public required string Color { get; init; }

    /// <summary><c>x86_64</c> or <c>aarch64</c>.</summary>
    public required string Architecture { get; init; }

    /// <summary>False when the image does not boot with Secure Boot enabled.</summary>
    public bool SecureBoot { get; init; } = true;

    public DistroRequirements? Requirements { get; init; }

    public required DistroImage Image { get; init; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Edition) ? Name : $"{Name} {Edition}";

    [JsonIgnore]
    public string Monogram => Name.Length == 0 ? "?" : char.ToUpperInvariant(Name.First(char.IsLetterOrDigit)).ToString();

    public bool HasCategory(string category) => Categories.Contains(category, StringComparer.Ordinal);
}

public sealed class LocalizedText
{
    public required string En { get; init; }

    public required string Fr { get; init; }

    /// <summary>Returns the text for a two-letter language code, English otherwise.</summary>
    public string Get(string? twoLetterLanguage) =>
        string.Equals(twoLetterLanguage, "fr", StringComparison.OrdinalIgnoreCase) ? Fr : En;
}

public sealed class DistroRequirements
{
    public int? RamMb { get; init; }

    public int? DiskGb { get; init; }
}

public sealed class DistroImage
{
    /// <summary>Placeholder replaced by the resolved ISO file name in checksum and signature URLs.</summary>
    public const string FileNamePlaceholder = "{fileName}";

    public required string FileName { get; init; }

    /// <summary>Size in bytes when the catalog was last updated.</summary>
    public required long Size { get; init; }

    /// <summary>
    /// True when <see cref="FileName"/> always points to the latest build (openSUSE Tumbleweed "Current"):
    /// its size and hash change with every build, so <see cref="Size"/> is only indicative.
    /// </summary>
    public bool LatestAlias { get; init; }

    public required IReadOnlyList<string> Urls { get; init; }

    public string? Sha256 { get; init; }

    public ChecksumSource? Checksum { get; init; }

    public SignatureSource? Signature { get; init; }

    public ImageResolveRule? Resolve { get; init; }

    /// <summary>True for isohybrid images that can be written as-is to a USB drive.</summary>
    public bool Hybrid { get; init; } = true;
}

public sealed class ChecksumSource
{
    public required string Url { get; init; }
}

public sealed class SignatureSource
{
    /// <summary><see cref="SignatureKinds.Detached"/> or <see cref="SignatureKinds.ClearSigned"/>.</summary>
    public required string Kind { get; init; }

    /// <summary><see cref="SignatureTargets.Checksum"/> or <see cref="SignatureTargets.Image"/>.</summary>
    public required string Target { get; init; }

    public string? Url { get; init; }

    /// <summary>Pinned OpenPGP fingerprints allowed to sign.</summary>
    public required IReadOnlyList<string> Fingerprints { get; init; }
}

public sealed class ImageResolveRule
{
    /// <summary><see cref="ResolveTypes.ChecksumPattern"/> or <see cref="ResolveTypes.Json"/>.</summary>
    public required string Type { get; init; }

    public string? Pattern { get; init; }

    public string? Url { get; init; }

    public string? UrlField { get; init; }

    public string? Sha256Field { get; init; }

    public string? SizeField { get; init; }

    public string? VersionField { get; init; }
}

public static class SignatureKinds
{
    public const string Detached = "detached";
    public const string ClearSigned = "clearsigned";
}

public static class SignatureTargets
{
    public const string Checksum = "checksum";
    public const string Image = "image";
}

public static class ResolveTypes
{
    public const string ChecksumPattern = "checksum-pattern";
    public const string Json = "json";
}

public static class DistroFamilies
{
    public const string Debian = "debian";
    public const string Ubuntu = "ubuntu";
    public const string Fedora = "fedora";
    public const string Arch = "arch";
    public const string OpenSuse = "opensuse";
    public const string Other = "other";

    public static IReadOnlyList<string> All { get; } = [Ubuntu, Debian, Fedora, Arch, OpenSuse, Other];
}

public static class DistroCategories
{
    public const string Beginner = "beginner";
    public const string Desktop = "desktop";
    public const string Lightweight = "lightweight";
    public const string Server = "server";
    public const string Security = "security";
    public const string Rolling = "rolling";
    public const string Developer = "developer";
    public const string Gaming = "gaming";

    public static IReadOnlyList<string> All { get; } = [Beginner, Desktop, Lightweight, Server, Security, Rolling, Developer, Gaming];
}

public static partial class DistroArchitectures
{
    public const string X64 = "x86_64";
    public const string Arm64 = "aarch64";

    public static IReadOnlyList<string> All { get; } = [X64, Arm64];

    /// <summary>
    /// The architecture that an ISO file name announces (<c>debian-13.7.0-arm64-netinst.iso</c>,
    /// <c>Fedora-Workstation-Live-44-1.7.x86_64.iso</c>), or null when it does not say.
    /// </summary>
    public static string? FromFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var arm = ArmPattern().IsMatch(name);
        var x64 = X64Pattern().IsMatch(name);
        return arm == x64 ? null : arm ? Arm64 : X64;
    }

    [GeneratedRegex("(?<![a-z0-9])(aarch64|arm64)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArmPattern();

    [GeneratedRegex("(?<![a-z0-9])(x86_64|x86-64|amd64|x64)(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex X64Pattern();
}
