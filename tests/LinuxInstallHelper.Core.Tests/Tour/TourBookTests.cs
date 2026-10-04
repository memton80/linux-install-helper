using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Tour;

namespace LinuxInstallHelper.Core.Tests.Tour;

public class TourBookTests
{
    private const long MaxImageBytes = 250 * 1024;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string ImagesFolder => Fixtures.Repository("tour", "images");

    private static IEnumerable<(string Key, TourLesson Lesson)> Lessons(TourBook book) =>
        book.Lessons.Select(pair => (pair.Key, pair.Value));

    private static IEnumerable<string> ImageFiles(TourLesson lesson) =>
        lesson.LocalizedImage ? [lesson.ImageFile("en")!, lesson.ImageFile("fr")!] : [lesson.ImageFile(null)!];

    [Fact]
    public void Embedded_tour_is_the_repository_tour()
    {
        Assert.Equal(
            File.ReadAllText(Fixtures.Repository("tour", "tours.json")).ReplaceLineEndings(),
            EmbeddedResources.ReadTourJson().ReplaceLineEndings());
    }

    [Fact]
    public void Every_catalog_distribution_has_its_own_tour()
    {
        var book = TourBook.LoadEmbedded();

        var missing = TestDistros.Embedded().Distros.Where(d => !book.Has(d.Id)).Select(d => d.Id).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_tour_is_a_known_distribution_or_the_generic_one()
    {
        var ids = TestDistros.Embedded().Distros.Select(d => d.Id).Append(TourBook.GenericTour).ToHashSet();

        var unknown = TourBook.LoadEmbedded().Tours.Keys.Where(id => !ids.Contains(id)).ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Tours_have_a_few_lessons_without_repeats()
    {
        foreach (var (id, keys) in TourBook.LoadEmbedded().Tours)
        {
            Assert.InRange(keys.Count, 5, 12);
            Assert.True(keys.Count == keys.Distinct().Count(), $"{id} repeats a lesson.");
        }
    }

    [Fact]
    public void Every_lesson_is_used()
    {
        var book = TourBook.LoadEmbedded();
        var used = book.Tours.Values.SelectMany(keys => keys).ToHashSet();

        Assert.Empty(book.Lessons.Keys.Where(key => !used.Contains(key)));
    }

    [Fact]
    public void Every_text_is_written_in_english_and_french()
    {
        foreach (var (key, lesson) in Lessons(TourBook.LoadEmbedded()))
        {
            var texts = new List<LocalizedText?> { lesson.Title, lesson.Body, lesson.Windows, lesson.Linux, lesson.Command };
            texts.AddRange(lesson.Steps);
            foreach (var text in texts.OfType<LocalizedText>())
            {
                Assert.False(string.IsNullOrWhiteSpace(text.En), $"{key} has an empty English text.");
                Assert.False(string.IsNullOrWhiteSpace(text.Fr), $"{key} has an empty French text.");
            }

            Assert.True(lesson.Windows is null == lesson.Linux is null, $"{key} compares Windows and Linux on one side only.");
            Assert.True(lesson.Glyph.Length == 1 && lesson.Glyph[0] >= '' && lesson.Glyph[0] <= '', $"{key} has no icon glyph.");
        }
    }

    [Fact]
    public void Steps_are_short_lists_that_match_the_marks_of_the_image()
    {
        foreach (var (key, lesson) in Lessons(TourBook.LoadEmbedded()))
        {
            Assert.True(lesson.Steps.Count <= 5, $"{key} has more steps than an image can show marks.");
            Assert.True(lesson.Steps.Count == 0 || lesson.Image is not null, $"{key} has numbered steps but no image to show them.");
        }
    }

    [Fact]
    public void Every_image_exists_in_each_language()
    {
        var book = TourBook.LoadEmbedded();

        var missing = Lessons(book)
            .Where(entry => entry.Lesson.Image is not null)
            .SelectMany(entry => ImageFiles(entry.Lesson).Select(file => $"{entry.Key}: {file}"))
            .Where(entry => !File.Exists(Path.Combine(ImagesFolder, entry.Split(": ")[1])))
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Lessons_sharing_an_image_agree_on_its_languages()
    {
        var conflicts = TourBook.LoadEmbedded().Lessons.Values
            .Where(lesson => lesson.Image is not null)
            .GroupBy(lesson => lesson.Image)
            .Where(group => group.Select(lesson => lesson.LocalizedImage).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Every_image_file_is_used_and_small()
    {
        var used = TourBook.LoadEmbedded().Lessons.Values
            .Where(lesson => lesson.Image is not null)
            .SelectMany(ImageFiles)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var path in Directory.GetFiles(ImagesFolder))
        {
            var name = Path.GetFileName(path);
            Assert.True(used.Contains(name), $"{name} is not used by any lesson.");
            Assert.True(new FileInfo(path).Length <= MaxImageBytes, $"{name} is larger than {MaxImageBytes / 1024} KB.");
            Assert.Equal(PngSignature, File.ReadAllBytes(path)[..PngSignature.Length]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-distribution-added-later")]
    public void Distributions_without_a_tour_get_the_generic_one(string? distroId)
    {
        var book = TourBook.LoadEmbedded();

        Assert.False(book.Has(distroId));
        Assert.Equal(book.Tours[TourBook.GenericTour].Select(key => book.Lessons[key]), book.For(distroId));
    }

    [Fact]
    public void A_distribution_gets_its_own_lessons_in_order()
    {
        var book = TourBook.LoadEmbedded();

        var lessons = book.For("archlinux");

        Assert.Equal(book.Tours["archlinux"].Select(key => book.Lessons[key]), lessons);
        Assert.Same(book.Lessons["arch-boot"], lessons.Single(lesson => lesson.Image == "arch-boot"));
    }

    [Theory]
    [InlineData("fr", "boot.fr.png")]
    [InlineData("FR", "boot.fr.png")]
    [InlineData("en", "boot.en.png")]
    [InlineData("de", "boot.en.png")]
    [InlineData(null, "boot.en.png")]
    public void Localized_images_follow_the_language(string? language, string expected)
    {
        var lesson = Lesson(image: "boot", localized: true);

        Assert.Equal(expected, lesson.ImageFile(language));
    }

    [Fact]
    public void Other_images_are_the_same_in_every_language()
    {
        Assert.Equal("boot.png", Lesson(image: "boot").ImageFile("fr"));
        Assert.Null(Lesson().ImageFile("fr"));
    }

    [Fact]
    public void A_tour_naming_an_unknown_lesson_is_rejected()
    {
        const string json = """
            { "lessons": {}, "tours": { "generic": ["missing"] } }
            """;

        var error = Assert.Throws<InvalidDataException>(() => TourBook.Parse(json));
        Assert.Contains("generic: missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generic_tour_is_required()
    {
        const string json = """
            { "lessons": {}, "tours": { "ubuntu-desktop": [] } }
            """;

        Assert.Throws<InvalidDataException>(() => TourBook.Parse(json));
    }

    [Fact]
    public void Invalid_json_is_reported()
    {
        Assert.Throws<InvalidDataException>(() => TourBook.Parse("{ \"lessons\": "));
    }

    private static TourLesson Lesson(string? image = null, bool localized = false) => new()
    {
        Glyph = "",
        Image = image,
        LocalizedImage = localized,
        Title = new LocalizedText { En = "Title", Fr = "Titre" },
        Body = new LocalizedText { En = "Body", Fr = "Texte" },
    };
}
