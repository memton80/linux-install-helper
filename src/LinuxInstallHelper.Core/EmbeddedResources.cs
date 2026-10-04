using System.Reflection;

namespace LinuxInstallHelper.Core;

/// <summary>Files from the <c>catalog</c> folder (offline fallback) and the tour embedded in this assembly.</summary>
public static class EmbeddedResources
{
    public const string CatalogName = "catalog/distros.json";
    public const string SchemaName = "catalog/distros.schema.json";
    public const string TourName = "tour/tours.json";
    private const string KeyPrefix = "catalog/keys/";

    private static readonly Assembly Assembly = typeof(EmbeddedResources).Assembly;

    public static string ReadCatalogJson() => ReadText(CatalogName);

    public static string ReadSchemaJson() => ReadText(SchemaName);

    public static string ReadTourJson() => ReadText(TourName);

    /// <summary>Returns the armored public key for a fingerprint, or null when it is not embedded.</summary>
    public static string? TryReadKey(string fingerprint)
    {
        var name = KeyPrefix + fingerprint.ToUpperInvariant() + ".asc";
        return Assembly.GetManifestResourceNames().Contains(name, StringComparer.Ordinal) ? ReadText(name) : null;
    }

    public static IReadOnlyList<string> EmbeddedKeyFingerprints() =>
        Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(KeyPrefix, StringComparison.Ordinal) && n.EndsWith(".asc", StringComparison.Ordinal))
            .Select(n => n[KeyPrefix.Length..^4])
            .ToList();

    private static string ReadText(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource '{name}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
