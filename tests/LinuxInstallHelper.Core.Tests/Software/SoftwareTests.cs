using LinuxInstallHelper.Core.Software;

namespace LinuxInstallHelper.Core.Tests.Software;

public class SoftwareTests
{
    [Fact]
    public void Keeps_the_programs_of_the_user_once_each()
    {
        UninstallEntry[] entries =
        [
            new() { DisplayName = "VLC media player", Publisher = "VideoLAN", DisplayVersion = "3.0.21" },
            new() { DisplayName = "vlc media player", Publisher = "VideoLAN", DisplayVersion = "3.0.20" },
            new() { DisplayName = "Mozilla Firefox (x64 fr)", Publisher = "Mozilla", DisplayVersion = "131.0" },
            new() { DisplayName = "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810", Publisher = "Microsoft Corporation" },
            new() { DisplayName = "Microsoft Windows Desktop Runtime - 8.0.8 (x64)" },
            new() { DisplayName = "Microsoft Edge WebView2 Runtime" },
            new() { DisplayName = "Realtek Audio Driver" },
            new() { DisplayName = "Some Component", SystemComponent = true },
            new() { DisplayName = "Security Update for Office (KB5002623)", ParentKeyName = "Office16.PROPLUS" },
            new() { DisplayName = "Hotfix thing", ReleaseType = "Hotfix" },
            new() { DisplayName = "   " },
            new() { DisplayName = null },
            new() { DisplayName = " 7-Zip 24.08 (x64) ", Publisher = " Igor Pavlov ", DisplayVersion = " " },
        ];

        var programs = InstalledPrograms.Clean(entries);

        Assert.Equal(["7-Zip 24.08 (x64)", "Mozilla Firefox (x64 fr)", "VLC media player"], programs.Select(p => p.Name));
        Assert.Equal("Igor Pavlov", programs[0].Publisher);
        Assert.Null(programs[0].Version);
        Assert.Equal("3.0.21", programs[2].Version);
    }

    [Theory]
    [InlineData("Google Chrome", SoftwareVerdict.Native)]
    [InlineData("Mozilla Firefox (x64 fr)", SoftwareVerdict.Native)]
    [InlineData("Steam", SoftwareVerdict.Native)]
    [InlineData("Microsoft Visual Studio Code (User)", SoftwareVerdict.Native)]
    [InlineData("Microsoft Visual Studio Community 2022", SoftwareVerdict.Alternative)]
    [InlineData("Microsoft 365 - fr-fr", SoftwareVerdict.Alternative)]
    [InlineData("Microsoft Office Famille et Petite Entreprise 2021 - fr-fr", SoftwareVerdict.Alternative)]
    [InlineData("Microsoft Teams", SoftwareVerdict.Web)]
    [InlineData("Adobe Photoshop 2024", SoftwareVerdict.Alternative)]
    [InlineData("Adobe Photoshop Lightroom Classic", SoftwareVerdict.Alternative)]
    [InlineData("Notepad++ (64-bit x64)", SoftwareVerdict.Alternative)]
    [InlineData("Git", SoftwareVerdict.Native)]
    [InlineData("GitHub Desktop", SoftwareVerdict.Alternative)]
    [InlineData("Riot Vanguard", SoftwareVerdict.WindowsOnly)]
    [InlineData("Avast Free Antivirus", SoftwareVerdict.NotNeeded)]
    [InlineData("Autodesk AutoCAD 2025 - Français", SoftwareVerdict.WindowsOnly)]
    [InlineData("Some Unknown Tool 2.0", SoftwareVerdict.Unknown)]
    public void Knows_well_known_programs(string name, SoftwareVerdict verdict)
    {
        Assert.Equal(verdict, SoftwareEquivalents.For(name).Verdict);
    }

    [Fact]
    public void Suggests_programs_for_the_alternatives()
    {
        Assert.Equal("Pinta", SoftwareEquivalents.For("paint.net").Suggestions);
        Assert.Contains("LibreOffice", SoftwareEquivalents.For("Microsoft Office Professional Plus 2019").Suggestions);
        Assert.Contains("darktable", SoftwareEquivalents.For("Adobe Photoshop Lightroom Classic").Suggestions);
    }

    [Theory]
    [InlineData("TortoiseGit 2.15")]
    [InlineData("SignalRGB")]
    [InlineData("Adobe Photoshop Elements 2024")]
    public void Names_are_matched_as_whole_words(string name)
    {
        var advice = SoftwareEquivalents.For(name);

        Assert.NotEqual(SoftwareVerdict.Native, advice.Verdict);
    }
}
