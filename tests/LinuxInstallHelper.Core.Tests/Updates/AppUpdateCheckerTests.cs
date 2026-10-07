using System.Net;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Updates;

namespace LinuxInstallHelper.Core.Tests.Updates;

public class AppUpdateCheckerTests
{
    private const string Page = "https://github.com/memton80/linux-install-helper/releases/tag/v1.0.4";

    [Fact]
    public async Task Finds_a_newer_release()
    {
        var handler = new StubHttpHandler().Add(AppUpdateChecker.LatestReleaseUrl, Release("v1.0.4"));

        var release = await new AppUpdateChecker(handler.CreateClient()).FindNewerAsync("1.0.3");

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 0, 4, 0), release.Version);
        Assert.Equal("v1.0.4", release.Tag);
        Assert.Equal(new Uri(Page), release.Page);
        var request = Assert.Single(handler.Requests);
        Assert.Contains(request.Headers.Accept, a => a.MediaType == "application/vnd.github+json");
    }

    [Theory]
    [InlineData("1.0.4")]
    [InlineData("1.1.0")]
    public async Task The_same_or_an_older_release_is_not_offered(string current)
    {
        var handler = new StubHttpHandler().Add(AppUpdateChecker.LatestReleaseUrl, Release("V1.0.4"));

        Assert.Null(await new AppUpdateChecker(handler.CreateClient()).FindNewerAsync(current));
    }

    [Fact]
    public async Task Offline_or_refused_answers_are_ignored()
    {
        var refused = new StubHttpHandler().AddStatus(AppUpdateChecker.LatestReleaseUrl, HttpStatusCode.Forbidden);
        var offline = new StubHttpHandler().AddHandler(AppUpdateChecker.LatestReleaseUrl, _ => throw new HttpRequestException("No network"));

        Assert.Null(await new AppUpdateChecker(refused.CreateClient()).FindNewerAsync("1.0.3"));
        Assert.Null(await new AppUpdateChecker(offline.CreateClient()).FindNewerAsync("1.0.3"));
    }

    [Fact]
    public void Drafts_pre_releases_and_odd_answers_are_not_releases()
    {
        Assert.Null(AppUpdateChecker.ParseRelease(Release("v2.0.0", prerelease: true)));
        Assert.Null(AppUpdateChecker.ParseRelease(Release("v2.0.0", draft: true)));
        Assert.Null(AppUpdateChecker.ParseRelease(Release("nightly")));
        Assert.Null(AppUpdateChecker.ParseRelease("[]"));
        Assert.Null(AppUpdateChecker.ParseRelease("{"));
    }

    [Fact]
    public void A_page_outside_the_repository_is_replaced_by_the_releases_page()
    {
        var release = AppUpdateChecker.ParseRelease(Release("v1.0.4", page: "https://example.com/download"));

        Assert.Equal(new Uri("https://github.com/memton80/linux-install-helper/releases/latest"), release?.Page);
    }

    [Theory]
    [InlineData("1.0.3", 1, 0, 3)]
    [InlineData("v1.2", 1, 2, 0)]
    [InlineData("V2.0.1-beta", 2, 0, 1)]
    [InlineData("1.0.3+abcdef", 1, 0, 3)]
    public void Reads_versions_and_tags(string text, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build, 0), AppUpdateChecker.ParseVersion(text));
    }

    private static string Release(string tag, bool draft = false, bool prerelease = false, string page = Page) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Linux Install Helper {{tag}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "html_url": "{{page}}"
        }
        """;
}
