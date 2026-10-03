using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Verification;

namespace LinuxInstallHelper.Core.Tests.Verification;

public class ChecksumFileTests
{
    private const string HashA = "487f87faaf547ea30e0aba4d5b53346292571256b25333a978db1692bcee9dd2";
    private const string HashB = "601E30FBF5D97759367C632E2C33630665039B7E2158FD068403DA3CCF1BDA1F";

    [Fact]
    public void Parses_gnu_format_with_binary_marker()
    {
        var file = ChecksumFile.Parse($"{HashA} *ubuntu.iso\n{HashB}  debian.iso\n");

        Assert.Equal(HashA, file.Find("ubuntu.iso"));
        Assert.Equal(HashB.ToLowerInvariant(), file.Find("debian.iso"));
    }

    [Fact]
    public void Parses_bsd_format_and_ignores_comments()
    {
        var file = ChecksumFile.Parse($"# Fedora.iso: 123 bytes\nSHA256 (Fedora-Workstation-Live.iso) = {HashA}\n");

        Assert.Equal(HashA, file.Find("Fedora-Workstation-Live.iso"));
        Assert.Single(file.Entries);
    }

    [Fact]
    public void Ignores_other_algorithms()
    {
        var sha512 = new string('a', 128);
        var sha1 = new string('b', 40);
        var file = ChecksumFile.Parse($"{sha512}  big.iso\n{sha1}  old.iso\nSHA512 (x.iso) = {sha512}\n{HashA}  good.iso");

        Assert.Equal(["good.iso"], file.FileNames);
    }

    [Fact]
    public void Strips_directories_and_handles_crlf()
    {
        var file = ChecksumFile.Parse($"{HashA}  ./iso/arch.iso\r\n{HashB} *sub\\win.iso\r\n");

        Assert.Equal(HashA, file.Find("arch.iso"));
        Assert.NotNull(file.Find("win.iso"));
    }

    [Fact]
    public void Single_unnamed_hash_is_used_as_fallback()
    {
        var file = ChecksumFile.Parse(HashA + "\n");

        Assert.Equal(HashA, file.Find("anything.iso"));
    }

    [Fact]
    public void Unknown_file_returns_null_when_names_are_listed()
    {
        var file = ChecksumFile.Parse($"{HashA}  a.iso\n");

        Assert.Null(file.Find("b.iso"));
    }

    [Fact]
    public void Parses_the_real_ubuntu_checksum_file()
    {
        var file = ChecksumFile.Parse(Fixtures.Text("ubuntu-SHA256SUMS"));

        Assert.Equal("601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f", file.Find("ubuntu-26.04.1-desktop-amd64.iso"));
        Assert.Equal(6, file.Entries.Count);
    }
}
