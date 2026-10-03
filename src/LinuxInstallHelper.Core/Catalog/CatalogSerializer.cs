using System.Text.Json;

namespace LinuxInstallHelper.Core.Catalog;

public static class CatalogSerializer
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        WriteIndented = true,
    };

    /// <summary>Deserializes a catalog without semantic validation (see <see cref="CatalogValidator"/>).</summary>
    public static DistroCatalog Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DistroCatalog>(json, Options)
                ?? throw new CatalogException("The catalog is empty.");
        }
        catch (JsonException ex)
        {
            throw new CatalogException($"The catalog is not valid JSON: {ex.Message}", [ex.Message], ex);
        }
    }
}
