using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.Core.Images;

public enum SignatureStatus
{
    /// <summary>The distribution does not publish a signature for its checksums.</summary>
    NotProvided,

    /// <summary>The checksum file signature was verified with a pinned key.</summary>
    Verified,

    /// <summary>A signature is published but could not be checked (download failed or key missing).</summary>
    Unavailable,
}

/// <summary>The exact image to download for a distribution, with the hash it must have.</summary>
public sealed record ResolvedImage
{
    public required Distro Distro { get; init; }

    /// <summary>File name of the ISO (may be newer than the catalog's one after a point release).</summary>
    public required string FileName { get; init; }

    /// <summary>Version extracted from the file name, when the resolve pattern captures one.</summary>
    public string? Version { get; init; }

    /// <summary>Build number reported by a JSON resolver.</summary>
    public string? Build { get; init; }

    /// <summary>Expected size in bytes when known.</summary>
    public long? Size { get; init; }

    /// <summary>Download URLs, best first.</summary>
    public required IReadOnlyList<Uri> Urls { get; init; }

    /// <summary>Expected SHA-256, lowercase hexadecimal.</summary>
    public required string Sha256 { get; init; }

    public SignatureStatus ChecksumSignature { get; init; } = SignatureStatus.NotProvided;

    /// <summary>Fingerprint of the key that signed the checksums, when verified.</summary>
    public string? ChecksumSigner { get; init; }

    /// <summary>Non blocking problem worth showing to the user.</summary>
    public string? Warning { get; init; }

    /// <summary>Detached signature of the image itself (checked after the download).</summary>
    public Uri? ImageSignatureUrl { get; init; }

    public IReadOnlyList<string> ImageSignatureFingerprints { get; init; } = [];

    public bool IsNewerThanCatalog => !string.Equals(FileName, Distro.Image.FileName, StringComparison.Ordinal);

    /// <summary>Version to display: the resolved one when known, the catalog one otherwise.</summary>
    public string DisplayVersion => Version ?? Distro.Version;
}
