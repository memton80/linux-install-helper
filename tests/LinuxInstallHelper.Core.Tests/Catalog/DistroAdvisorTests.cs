using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Catalog;

public class DistroAdvisorTests
{
    private static readonly IReadOnlyList<Distro> Catalog = TestDistros.Embedded().Distros;

    private static AdvisorAnswers Answers(
        LinuxExperience experience = LinuxExperience.None,
        ComputerPower power = ComputerPower.Recent,
        MainUse use = MainUse.Everyday,
        DesktopLook look = DesktopLook.NoPreference,
        UpdatePace updates = UpdatePace.NoPreference,
        bool avoidFirmware = true) => new(experience, power, use, look, updates, avoidFirmware);

    private static string Best(AdvisorAnswers answers) => DistroAdvisor.Recommend(Catalog, answers)[0].Distro.Id;

    [Fact]
    public void Servers_security_toolkits_and_text_installers_are_never_suggested()
    {
        var everyone = DistroAdvisor.Recommend(Catalog, Answers(LinuxExperience.Comfortable, use: MainUse.Development), count: 100)
            .Select(r => r.Distro.Id)
            .ToList();

        Assert.DoesNotContain("ubuntu-server", everyone);
        Assert.DoesNotContain("debian-netinst", everyone);
        Assert.DoesNotContain("archlinux", everyone);
        Assert.DoesNotContain("kali", everyone);
        Assert.Contains("ubuntu-desktop", everyone);
    }

    [Fact]
    public void A_beginner_who_wants_windows_looks_gets_a_beginner_distribution_that_looks_like_windows()
    {
        var best = DistroAdvisor.Recommend(Catalog, Answers(look: DesktopLook.LikeWindows, updates: UpdatePace.Stable))[0];

        Assert.Equal("linuxmint-cinnamon", best.Distro.Id);
        Assert.Contains(AdvisorReason.Beginner, best.Reasons);
        Assert.Contains(AdvisorReason.LikeWindows, best.Reasons);
        Assert.Contains(AdvisorReason.SecureBoot, best.Reasons);
    }

    [Fact]
    public void An_old_computer_gets_a_lightweight_distribution()
    {
        var best = DistroAdvisor.Recommend(Catalog, Answers(power: ComputerPower.Modest))[0];

        Assert.True(best.Distro.HasCategory(DistroCategories.Lightweight));
        Assert.Contains(AdvisorReason.Lightweight, best.Reasons);
    }

    [Fact]
    public void A_developer_who_wants_the_latest_versions_gets_fedora()
    {
        Assert.Equal("fedora-workstation", Best(Answers(LinuxExperience.Comfortable, use: MainUse.Development, updates: UpdatePace.Latest, avoidFirmware: false)));
    }

    [Fact]
    public void Distributions_without_secure_boot_are_not_suggested_to_whoever_avoids_firmware_settings()
    {
        var withoutBios = DistroAdvisor.Recommend(Catalog, Answers(use: MainUse.Gaming, avoidFirmware: true));
        var withBios = DistroAdvisor.Recommend(Catalog, Answers(use: MainUse.Gaming, avoidFirmware: false));

        Assert.All(withoutBios, r => Assert.True(r.Distro.SecureBoot));
        Assert.Contains(withBios, r => r.Distro.Id == "popos");
        Assert.All(withBios.Where(r => r.Distro.HasCategory(DistroCategories.Gaming)), r => Assert.Contains(AdvisorReason.Gaming, r.Reasons));
    }

    [Fact]
    public void Rolling_releases_are_kept_away_from_beginners_who_want_stability()
    {
        var suggestions = DistroAdvisor.Recommend(Catalog, Answers(updates: UpdatePace.Stable));

        Assert.All(suggestions, r => Assert.False(r.Distro.HasCategory(DistroCategories.Rolling)));
    }

    [Fact]
    public void Returns_the_requested_number_of_distinct_suggestions_best_first()
    {
        var suggestions = DistroAdvisor.Recommend(Catalog, Answers(), count: 3);

        Assert.Equal(3, suggestions.Count);
        Assert.Equal(3, suggestions.Select(r => r.Distro.Id).Distinct().Count());
        Assert.True(suggestions[0].Score >= suggestions[1].Score && suggestions[1].Score >= suggestions[2].Score);
    }
}
