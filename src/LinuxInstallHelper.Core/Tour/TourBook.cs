using System.Text.Json;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.Core.Tour;

/// <summary>
/// The lessons shown while a drive is created (<c>tour/tours.json</c>): one tour per distribution of the catalog,
/// plus a generic tour for local images and distributions added to the catalog after this build.
/// See <c>tour/README.md</c>.
/// </summary>
public sealed class TourBook
{
    /// <summary>Tour used when the distribution has none of its own.</summary>
    public const string GenericTour = "generic";

    /// <summary>Every lesson, by key.</summary>
    public required IReadOnlyDictionary<string, TourLesson> Lessons { get; init; }

    /// <summary>The keys of the lessons of each tour, by distribution id (plus <see cref="GenericTour"/>).</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Tours { get; init; }

    public static TourBook LoadEmbedded() => Parse(EmbeddedResources.ReadTourJson());

    /// <exception cref="InvalidDataException">The file is not valid JSON or a tour names an unknown lesson.</exception>
    public static TourBook Parse(string json)
    {
        TourBook? book;
        try
        {
            book = JsonSerializer.Deserialize<TourBook>(json, CatalogSerializer.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The tour file is not valid: {ex.Message}", ex);
        }

        if (book is null || !book.Tours.ContainsKey(GenericTour))
        {
            throw new InvalidDataException($"The tour file has no '{GenericTour}' tour.");
        }

        var unknown = book.Tours
            .SelectMany(tour => tour.Value.Where(key => !book.Lessons.ContainsKey(key)).Select(key => $"{tour.Key}: {key}"))
            .ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidDataException($"Unknown lessons in the tour file: {string.Join(", ", unknown)}");
        }

        return book;
    }

    /// <summary>True when the distribution has a tour of its own.</summary>
    public bool Has(string? distroId) => distroId is not null && Tours.ContainsKey(distroId);

    /// <summary>The lessons for a distribution, or the generic ones when it has no tour (or for a local image).</summary>
    public IReadOnlyList<TourLesson> For(string? distroId) =>
        (Has(distroId) ? Tours[distroId!] : Tours[GenericTour]).Select(key => Lessons[key]).ToList();
}

/// <summary>One lesson. Its numbered <see cref="Steps"/> match the numbered marks drawn on its image.</summary>
public sealed class TourLesson
{
    /// <summary>Segoe Fluent Icons glyph, shown when the lesson has no image.</summary>
    public required string Glyph { get; init; }

    /// <summary>Name of the picture in <c>tour/images</c>, without extension.</summary>
    public string? Image { get; init; }

    /// <summary>The picture contains text: it exists as <c>name.en.png</c> and <c>name.fr.png</c>.</summary>
    public bool LocalizedImage { get; init; }

    public required LocalizedText Title { get; init; }

    public required LocalizedText Body { get; init; }

    public IReadOnlyList<LocalizedText> Steps { get; init; } = [];

    /// <summary>How it is done on Windows, compared with <see cref="Linux"/>.</summary>
    public LocalizedText? Windows { get; init; }

    public LocalizedText? Linux { get; init; }

    /// <summary>Commands to type, with their comments.</summary>
    public LocalizedText? Command { get; init; }

    /// <summary>File name of the picture for a two-letter language code, null when the lesson has none.</summary>
    public string? ImageFile(string? twoLetterLanguage)
    {
        if (Image is null)
        {
            return null;
        }

        if (!LocalizedImage)
        {
            return Image + ".png";
        }

        var language = string.Equals(twoLetterLanguage, "fr", StringComparison.OrdinalIgnoreCase) ? "fr" : "en";
        return $"{Image}.{language}.png";
    }
}
