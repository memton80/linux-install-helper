using LinuxInstallHelper.App.ViewModels;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// The difficult words of the application explained in plain words, from the strings <c>Word_&lt;id&gt;</c> and
/// <c>Word_&lt;id&gt;_Meaning</c>. Shown behind the "?" buttons of the questionnaire, its suggestions and the
/// distribution page.
/// </summary>
public sealed class Glossary
{
    private readonly ILocalizer _localizer;

    public Glossary(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    /// <summary>The words with these ids, in this order; an id missing from the strings is skipped.</summary>
    public IReadOnlyList<ExplainedWord> Get(IEnumerable<string> ids) => ids
        .Distinct(StringComparer.Ordinal)
        .Select(id => (Word: Optional($"Word_{id}"), Meaning: Optional($"Word_{id}_Meaning")))
        .Where(w => w.Word is not null && w.Meaning is not null)
        .Select(w => new ExplainedWord(w.Word!, w.Meaning!))
        .ToList();

    /// <summary>The words a beginner may not know in what the application shows about a distribution.</summary>
    /// <param name="distro">The distribution.</param>
    /// <param name="mentionsSecureBoot">Whether the BIOS and Secure Boot are mentioned next to it.</param>
    /// <param name="mentionsDownload">Whether the size of the download is shown.</param>
    public IReadOnlyList<ExplainedWord> ForDistro(Distro distro, bool mentionsSecureBoot, bool mentionsDownload)
    {
        ArgumentNullException.ThrowIfNull(distro);

        var ids = new List<string>();
        if (!string.IsNullOrWhiteSpace(distro.Desktop) && distro.Desktop != "None")
        {
            ids.Add("desktop");
        }

        if (distro.Version.Contains("LTS", StringComparison.Ordinal))
        {
            ids.Add("lts");
        }

        ids.Add(distro.HasCategory(DistroCategories.Rolling) ? "rolling" : "update");
        if (distro.HasCategory(DistroCategories.Lightweight))
        {
            ids.Add("resources");
        }

        if (distro.HasCategory(DistroCategories.Gaming))
        {
            ids.AddRange(["drivers", "steam"]);
        }

        if (mentionsDownload)
        {
            ids.Add("download");
        }

        if (distro.Requirements is not null)
        {
            ids.AddRange(["memory", "gb", "disk"]);
        }

        if (mentionsSecureBoot || !distro.SecureBoot)
        {
            ids.AddRange(["bios", "secureboot"]);
        }

        return Get(ids);
    }

    private string? Optional(string key) => _localizer.Get(key) is var text && text != key ? text : null;
}
