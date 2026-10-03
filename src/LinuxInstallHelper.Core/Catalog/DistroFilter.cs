using System.Globalization;

namespace LinuxInstallHelper.Core.Catalog;

/// <summary>Search and filters of the distributions screen.</summary>
/// <param name="Text">Free text, matched against name, edition, version, desktop and description.</param>
/// <param name="Family">One of <see cref="DistroFamilies"/>, or null for all.</param>
/// <param name="Category">One of <see cref="DistroCategories"/>, or null for all.</param>
/// <param name="Architecture">One of <see cref="DistroArchitectures"/>, or null for all.</param>
public sealed record DistroQuery(string? Text = null, string? Family = null, string? Category = null, string? Architecture = null)
{
    public static DistroQuery All { get; } = new();
}

public static class DistroFilter
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;

    /// <summary>Returns the distributions matching <paramref name="query"/>, in catalog order.</summary>
    public static IReadOnlyList<Distro> Apply(IEnumerable<Distro> distros, DistroQuery query, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(distros);
        ArgumentNullException.ThrowIfNull(query);

        var terms = (query.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return distros
            .Where(d => query.Family is null || string.Equals(d.Family, query.Family, StringComparison.Ordinal))
            .Where(d => query.Category is null || d.HasCategory(query.Category))
            .Where(d => query.Architecture is null || string.Equals(d.Architecture, query.Architecture, StringComparison.Ordinal))
            .Where(d => terms.All(term => Matches(d, term, language)))
            .ToList();
    }

    private static bool Matches(Distro distro, string term, string? language)
    {
        return Contains(distro.Name, term)
            || Contains(distro.Edition, term)
            || Contains(distro.Version, term)
            || Contains(distro.Desktop, term)
            || Contains(distro.Family, term)
            || Contains(distro.Description.Get(language), term)
            || Contains(distro.Description.En, term);
    }

    private static bool Contains(string? source, string term) =>
        !string.IsNullOrEmpty(source) && Compare.IndexOf(source, term, SearchOptions) >= 0;
}
