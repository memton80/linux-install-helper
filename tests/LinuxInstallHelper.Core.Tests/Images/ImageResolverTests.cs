using System.Net;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Verification;

namespace LinuxInstallHelper.Core.Tests.Images;

public class ImageResolverTests
{
    private const string SumsUrl = "https://releases.ubuntu.com/26.04/SHA256SUMS";
    private const string SigUrl = "https://releases.ubuntu.com/26.04/SHA256SUMS.gpg";
    private const string UbuntuHash = "601e30fbf5d97759367c632e2c33630665039b7e2158fd068403da3ccf1bda1f";

    private static ImageResolver Resolver(StubHttpHandler handler) => new(handler.CreateClient(), new PublicKeyStore());

    private static StubHttpHandler UbuntuServer() => new StubHttpHandler()
        .Add(SumsUrl, Fixtures.Bytes("ubuntu-SHA256SUMS"))
        .Add(SigUrl, Fixtures.Bytes("ubuntu-SHA256SUMS.gpg"));

    [Fact]
    public async Task Resolves_ubuntu_with_a_verified_signature()
    {
        var resolved = await Resolver(UbuntuServer()).ResolveAsync(TestDistros.Get("ubuntu-desktop"));

        Assert.Equal("ubuntu-26.04.1-desktop-amd64.iso", resolved.FileName);
        Assert.Equal(UbuntuHash, resolved.Hash);
        Assert.Equal(SignatureStatus.Verified, resolved.ChecksumSignature);
        Assert.Equal(Fixtures.UbuntuFingerprint, resolved.ChecksumSigner);
        Assert.Equal("26.04.1", resolved.Version);
        Assert.False(resolved.IsNewerThanCatalog);
        Assert.Equal(2, resolved.Urls.Count);
        Assert.Null(resolved.Warning);
    }

    [Fact]
    public async Task Refuses_a_checksum_file_with_a_bad_signature()
    {
        var tampered = Fixtures.Bytes("ubuntu-SHA256SUMS");
        tampered[3] ^= 0x01;
        var handler = new StubHttpHandler().Add(SumsUrl, tampered).Add(SigUrl, Fixtures.Bytes("ubuntu-SHA256SUMS.gpg"));

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Resolver(handler).ResolveAsync(TestDistros.Get("ubuntu-desktop")));

        Assert.Equal(VerificationFailure.BadSignature, ex.Failure);
    }

    [Fact]
    public async Task Continues_with_a_warning_when_the_signature_cannot_be_downloaded()
    {
        var handler = new StubHttpHandler().Add(SumsUrl, Fixtures.Bytes("ubuntu-SHA256SUMS")).AddStatus(SigUrl, HttpStatusCode.NotFound);

        var resolved = await Resolver(handler).ResolveAsync(TestDistros.Get("ubuntu-desktop"));

        Assert.Equal(SignatureStatus.Unavailable, resolved.ChecksumSignature);
        Assert.NotNull(resolved.Warning);
        Assert.Equal(UbuntuHash, resolved.Hash);
    }

    [Fact]
    public async Task Fails_when_the_checksum_file_is_unavailable()
    {
        var handler = new StubHttpHandler().AddStatus(SumsUrl, HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Resolver(handler).ResolveAsync(TestDistros.Get("ubuntu-desktop")));

        Assert.Equal(VerificationFailure.ChecksumUnavailable, ex.Failure);
    }

    [Fact]
    public async Task Follows_a_point_release_and_rewrites_the_urls()
    {
        var ubuntu = TestDistros.Get("ubuntu-desktop");
        var old = TestDistros.WithImage(ubuntu, TestDistros.Copy(
            ubuntu.Image,
            fileName: "ubuntu-26.04.0-desktop-amd64.iso",
            urls: ["https://releases.ubuntu.com/26.04/ubuntu-26.04.0-desktop-amd64.iso", "https://mirror.example/ubuntu/other-name.iso"]));

        var resolved = await Resolver(UbuntuServer()).ResolveAsync(old);

        Assert.Equal("ubuntu-26.04.1-desktop-amd64.iso", resolved.FileName);
        Assert.True(resolved.IsNewerThanCatalog);
        Assert.Equal("26.04.1", resolved.DisplayVersion);
        Assert.Null(resolved.Size);
        Assert.Equal("https://releases.ubuntu.com/26.04/ubuntu-26.04.1-desktop-amd64.iso", resolved.Urls[0].ToString());
        Assert.Equal("https://mirror.example/ubuntu/other-name.iso", resolved.Urls[1].ToString());
    }

    [Fact]
    public void Newest_pattern_match_wins()
    {
        var image = TestDistros.Copy(
            TestDistros.Get("debian-live").Image,
            fileName: "debian-live-13.7.0-amd64-gnome.iso");
        var checksums = ChecksumFile.Parse(string.Join('\n',
            $"{new string('1', 64)}  debian-live-13.9.0-amd64-gnome.iso",
            $"{new string('2', 64)}  debian-live-13.10.0-amd64-gnome.iso",
            $"{new string('3', 64)}  debian-live-13.10.0-amd64-kde.iso"));

        var (fileName, version) = ImageResolver.SelectFile(image, checksums);

        Assert.Equal("debian-live-13.10.0-amd64-gnome.iso", fileName);
        Assert.Equal("13.10.0", version);
    }

    [Fact]
    public void Missing_file_without_pattern_is_an_error()
    {
        var image = TestDistros.Copy(TestDistros.Get("linuxmint-cinnamon").Image);
        var checksums = ChecksumFile.Parse($"{new string('1', 64)} *linuxmint-99-cinnamon-64bit.iso");

        var ex = Assert.Throws<VerificationException>(() => ImageResolver.SelectFile(image, checksums));

        Assert.Equal(VerificationFailure.NotListed, ex.Failure);
    }

    [Fact]
    public async Task Uses_an_inline_sha256_when_there_is_no_checksum_file()
    {
        var ubuntu = TestDistros.Get("ubuntu-desktop");
        var inline = TestDistros.WithImage(ubuntu, TestDistros.Copy(ubuntu.Image, sha256: UbuntuHash.ToUpperInvariant(), clearChecksum: true, clearSignature: true, clearResolve: true));

        var resolved = await Resolver(new StubHttpHandler()).ResolveAsync(inline);

        Assert.Equal(UbuntuHash, resolved.Hash);
        Assert.Equal(SignatureStatus.NotProvided, resolved.ChecksumSignature);
    }

    [Fact]
    public async Task Catalog_hash_that_contradicts_the_official_file_is_refused()
    {
        var ubuntu = TestDistros.Get("ubuntu-desktop");
        var wrong = TestDistros.WithImage(ubuntu, TestDistros.Copy(ubuntu.Image, sha256: new string('a', 64)));

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Resolver(UbuntuServer()).ResolveAsync(wrong));

        Assert.Equal(VerificationFailure.ChecksumMismatch, ex.Failure);
    }

    [Fact]
    public async Task Resolves_from_a_json_api()
    {
        var pop = TestDistros.Get("popos");
        var api = pop.Image.Resolve!.Url!;
        var handler = new StubHttpHandler().Add(api, $$"""
            {"build":"31","channel":"generic","sha_sum":"{{UbuntuHash.ToUpperInvariant()}}","size":3221225472,
             "url":"https://iso.pop-os.org/24.04/amd64/generic/31/pop-os_24.04_amd64_generic_31.iso","version":"24.04"}
            """);

        var resolved = await Resolver(handler).ResolveAsync(pop);

        Assert.Equal("pop-os_24.04_amd64_generic_31.iso", resolved.FileName);
        Assert.Equal("31", resolved.Build);
        Assert.Equal(3221225472, resolved.Size);
        Assert.Equal(UbuntuHash, resolved.Hash);
        Assert.Equal("https://iso.pop-os.org/24.04/amd64/generic/31/pop-os_24.04_amd64_generic_31.iso", resolved.Urls[0].ToString());
        Assert.Contains(resolved.Urls, u => u.ToString() == pop.Image.Urls[0]);
    }

    [Fact]
    public async Task Json_api_returning_an_http_url_is_refused()
    {
        var pop = TestDistros.Get("popos");
        var handler = new StubHttpHandler().Add(pop.Image.Resolve!.Url!, $$"""{"build":"1","sha_sum":"{{UbuntuHash}}","url":"http://iso.example/pop.iso"}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Resolver(handler).ResolveAsync(pop));
    }

    [Fact]
    public async Task Image_signature_url_uses_the_resolved_file_name()
    {
        var arch = TestDistros.Get("archlinux");
        var hash = new string('c', 64);
        var handler = new StubHttpHandler().Add(arch.Image.Checksum!.Url, $"{hash}  archlinux-2027.01.01-x86_64.iso\n{hash}  archlinux-x86_64.iso\n");

        var resolved = await Resolver(handler).ResolveAsync(arch);

        Assert.Equal("archlinux-2027.01.01-x86_64.iso", resolved.FileName);
        Assert.Equal("2027.01.01", resolved.Version);
        Assert.Equal("https://geo.mirror.pkgbuild.com/iso/latest/archlinux-2027.01.01-x86_64.iso.sig", resolved.ImageSignatureUrl!.ToString());
        Assert.Equal(arch.Image.Signature!.Fingerprints, resolved.ImageSignatureFingerprints);
    }

    [Fact]
    public async Task Latest_alias_has_no_expected_size()
    {
        var tumbleweed = TestDistros.Get("opensuse-tumbleweed");
        var hash = new string('d', 64);
        var unsigned = TestDistros.WithImage(tumbleweed, TestDistros.Copy(tumbleweed.Image, clearSignature: true));
        var handler = new StubHttpHandler().Add(tumbleweed.Image.Checksum!.Url, $"{hash}  {tumbleweed.Image.FileName}\n");

        var resolved = await Resolver(handler).ResolveAsync(unsigned);

        Assert.True(tumbleweed.Image.LatestAlias);
        Assert.Null(resolved.Size);
        Assert.Equal(hash, resolved.Hash);
    }

    [Fact]
    public async Task Latest_alias_follows_the_build_named_in_its_checksum_file()
    {
        var leap = TestDistros.Get("opensuse-leap");
        var unsigned = TestDistros.WithImage(leap, TestDistros.Copy(leap.Image, clearSignature: true));
        var sha512 = new string('e', 128);
        var handler = new StubHttpHandler().Add(leap.Image.Checksum!.Url, $"{sha512}  Leap-16.0-offline-installer-x86_64-Build178.27.install.iso\n");

        var resolved = await Resolver(handler).ResolveAsync(unsigned);

        Assert.Equal("Leap-16.0-offline-installer-x86_64-Build178.27.install.iso", resolved.FileName);
        Assert.Equal(HashAlgorithmKind.Sha512, resolved.HashAlgorithm);
        Assert.Equal(sha512, resolved.Hash);
        Assert.EndsWith("/offline/Leap-16.0-offline-installer-x86_64-Build178.27.install.iso", resolved.Urls[0].ToString());
    }
}
