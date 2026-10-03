namespace LinuxInstallHelper.Core.Catalog;

public enum LinuxExperience
{
    /// <summary>Never used Linux.</summary>
    None,

    /// <summary>Tried it a little.</summary>
    Some,

    /// <summary>At ease, terminal included.</summary>
    Comfortable,
}

public enum ComputerPower
{
    /// <summary>4 GB of memory or more.</summary>
    Recent,

    /// <summary>Old or modest: less than 4 GB of memory.</summary>
    Modest,
}

public enum MainUse
{
    /// <summary>Web, office, photos and videos.</summary>
    Everyday,

    Gaming,

    Development,
}

public enum DesktopLook
{
    /// <summary>A taskbar and a start menu at the bottom left.</summary>
    LikeWindows,

    /// <summary>Clean and modern, a bit like macOS.</summary>
    Modern,

    NoPreference,
}

public enum UpdatePace
{
    /// <summary>Tested updates that do not change things.</summary>
    Stable,

    /// <summary>The latest versions as soon as they are out.</summary>
    Latest,

    NoPreference,
}

/// <param name="AvoidFirmwareSettings">The user would rather not change a setting of the UEFI/BIOS (Secure Boot).</param>
public sealed record AdvisorAnswers(
    LinuxExperience Experience,
    ComputerPower Power,
    MainUse Use,
    DesktopLook Look,
    UpdatePace Updates,
    bool AvoidFirmwareSettings);

/// <summary>Why a distribution suits the answers, in the order shown to the user.</summary>
public enum AdvisorReason
{
    Beginner,
    Lightweight,
    Gaming,
    Development,
    LikeWindows,
    Modern,
    Stable,
    Latest,
    SecureBoot,
}

public sealed record DistroRecommendation(Distro Distro, int Score, IReadOnlyList<AdvisorReason> Reasons);

/// <summary>
/// Suggests the distributions that best fit the answers of the questionnaire, from what the catalog says about them
/// (categories, desktop, memory needed, Secure Boot). Only distributions with a graphical desktop are suggested.
/// </summary>
public static class DistroAdvisor
{
    /// <summary>Below this amount of memory, a computer is considered modest.</summary>
    public const long ModestMemoryBytes = 4L * 1024 * 1024 * 1024;

    private static readonly string[] WindowsLikeDesktops = ["Cinnamon", "KDE", "Xfce", "LXQt", "Zorin"];
    private static readonly string[] ModernDesktops = ["GNOME", "COSMIC"];

    /// <summary>The <paramref name="count"/> best suggestions, best first (catalog order between equal scores).</summary>
    public static IReadOnlyList<DistroRecommendation> Recommend(IEnumerable<Distro> distros, AdvisorAnswers answers, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(distros);
        ArgumentNullException.ThrowIfNull(answers);

        return distros
            .Where(IsCandidate)
            .Select((distro, index) => (Recommendation: Evaluate(distro, answers), Index: index))
            .OrderByDescending(r => r.Recommendation.Score)
            .ThenBy(r => r.Index)
            .Take(count)
            .Select(r => r.Recommendation)
            .ToList();
    }

    /// <summary>A desktop distribution for everyday use: no server, security toolkit or text-only installer.</summary>
    public static bool IsCandidate(Distro distro)
    {
        ArgumentNullException.ThrowIfNull(distro);
        return HasDesktop(distro)
            && !distro.HasCategory(DistroCategories.Security)
            && (distro.HasCategory(DistroCategories.Desktop) || distro.HasCategory(DistroCategories.Beginner));
    }

    public static DistroRecommendation Evaluate(Distro distro, AdvisorAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(distro);
        ArgumentNullException.ThrowIfNull(answers);

        var score = 0;
        var reasons = new List<AdvisorReason>();
        var beginner = distro.HasCategory(DistroCategories.Beginner);
        var rolling = distro.HasCategory(DistroCategories.Rolling);
        var lightweight = distro.HasCategory(DistroCategories.Lightweight);
        var ramMb = distro.Requirements?.RamMb ?? 4096;
        // Fedora is not rolling but ships new versions of everything twice a year.
        var fresh = rolling || string.Equals(distro.Family, DistroFamilies.Fedora, StringComparison.Ordinal);

        switch (answers.Experience)
        {
            case LinuxExperience.None:
                score += beginner ? 40 : -20;
                score -= rolling ? 25 : 0;
                break;
            case LinuxExperience.Some:
                score += beginner ? 20 : 0;
                score -= rolling ? 10 : 0;
                break;
            case LinuxExperience.Comfortable:
                score += distro.HasCategory(DistroCategories.Developer) ? 5 : 0;
                break;
        }

        if (beginner && answers.Experience != LinuxExperience.Comfortable)
        {
            reasons.Add(AdvisorReason.Beginner);
        }

        if (answers.Power == ComputerPower.Modest)
        {
            if (lightweight)
            {
                score += 35;
                reasons.Add(AdvisorReason.Lightweight);
            }

            score -= ramMb > 2048 ? 30 : 0;
        }
        else
        {
            score -= lightweight ? 5 : 0;
        }

        switch (answers.Use)
        {
            case MainUse.Gaming when distro.HasCategory(DistroCategories.Gaming):
                score += 30;
                reasons.Add(AdvisorReason.Gaming);
                break;
            case MainUse.Development when distro.HasCategory(DistroCategories.Developer):
                score += 30;
                reasons.Add(AdvisorReason.Development);
                break;
            case MainUse.Everyday:
                score += beginner ? 5 : 0;
                break;
        }

        if (answers.Look == DesktopLook.LikeWindows && DesktopIs(distro, WindowsLikeDesktops))
        {
            score += 20;
            reasons.Add(AdvisorReason.LikeWindows);
        }
        else if (answers.Look == DesktopLook.Modern && DesktopIs(distro, ModernDesktops) && !DesktopIs(distro, WindowsLikeDesktops))
        {
            score += 20;
            reasons.Add(AdvisorReason.Modern);
        }

        if (answers.Updates == UpdatePace.Stable)
        {
            if (!fresh)
            {
                score += 15;
                reasons.Add(AdvisorReason.Stable);
            }

            score -= rolling ? 15 : 0;
        }
        else if (answers.Updates == UpdatePace.Latest && fresh)
        {
            score += 15;
            reasons.Add(AdvisorReason.Latest);
        }

        if (answers.AvoidFirmwareSettings)
        {
            if (distro.SecureBoot)
            {
                reasons.Add(AdvisorReason.SecureBoot);
            }
            else
            {
                score -= 40;
            }
        }

        return new DistroRecommendation(distro, score, reasons);
    }

    private static bool HasDesktop(Distro distro) =>
        !string.IsNullOrWhiteSpace(distro.Desktop) && !string.Equals(distro.Desktop, "None", StringComparison.OrdinalIgnoreCase);

    // "GNOME (Zorin)" looks like Windows: the Windows-like test comes first wherever both match.
    private static bool DesktopIs(Distro distro, string[] desktops) =>
        distro.Desktop is { } desktop && desktops.Any(d => desktop.Contains(d, StringComparison.OrdinalIgnoreCase));
}
