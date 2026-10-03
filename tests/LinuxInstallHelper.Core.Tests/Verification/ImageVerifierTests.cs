using System.Net;
using System.Security.Cryptography;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Verification;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace LinuxInstallHelper.Core.Tests.Verification;

public sealed class ImageVerifierTests : IDisposable
{
    private const string SignatureUrl = "https://mirror.example/distro.iso.sig";
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Iso => Fixtures.PathOf("payload.bin");

    private static string Sha256Of(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static ResolvedImage Image(string hash, HashAlgorithmKind algorithm = HashAlgorithmKind.Sha256, bool signed = false) => new()
    {
        Distro = TestDistros.Get("archlinux"),
        FileName = "distro.iso",
        Urls = [new Uri("https://mirror.example/distro.iso")],
        Hash = hash,
        HashAlgorithm = algorithm,
        ImageSignatureUrl = signed ? new Uri(SignatureUrl) : null,
        ImageSignatureFingerprints = signed ? [Fixtures.Fingerprint("ed25519")] : [],
    };

    private static ImageVerifier Verifier(StubHttpHandler? handler = null) =>
        new((handler ?? new StubHttpHandler()).CreateClient(), new FixtureKeyStore());

    [Fact]
    public async Task Accepts_a_matching_sha256_and_reports_progress()
    {
        var reports = new List<VerificationProgress>();

        var result = await Verifier().VerifyAsync(Iso, Image(Sha256Of(Iso).ToUpperInvariant()), new SynchronousProgress<VerificationProgress>(reports.Add));

        Assert.Equal(Sha256Of(Iso), result.Hash);
        Assert.Equal(SignatureStatus.NotProvided, result.ImageSignature);
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public async Task Accepts_a_matching_sha512()
    {
        var sha512 = Convert.ToHexString(SHA512.HashData(File.ReadAllBytes(Iso))).ToLowerInvariant();

        var result = await Verifier().VerifyAsync(Iso, Image(sha512, HashAlgorithmKind.Sha512));

        Assert.Equal(HashAlgorithmKind.Sha512, result.Algorithm);
    }

    [Fact]
    public async Task Refuses_a_corrupted_image()
    {
        var corrupted = _temp.File("corrupted.iso");
        var bytes = File.ReadAllBytes(Iso);
        bytes[12345] ^= 0xFF;
        File.WriteAllBytes(corrupted, bytes);

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Verifier().VerifyAsync(corrupted, Image(Sha256Of(Iso))));

        Assert.Equal(VerificationFailure.ChecksumMismatch, ex.Failure);
        Assert.Contains(Sha256Of(Iso), ex.Message);
    }

    [Fact]
    public async Task Verifies_the_image_signature_in_the_same_pass()
    {
        var handler = new StubHttpHandler().Add(SignatureUrl, Fixtures.Bytes("payload.bin.sig"));

        var result = await Verifier(handler).VerifyAsync(Iso, Image(Sha256Of(Iso), signed: true));

        Assert.Equal(SignatureStatus.Verified, result.ImageSignature);
        Assert.Equal(Fixtures.Fingerprint("ed25519"), result.Signer);
    }

    [Fact]
    public async Task Refuses_an_image_whose_signature_does_not_match()
    {
        var other = _temp.File("other.iso");
        var bytes = File.ReadAllBytes(Iso);
        bytes[0] ^= 0x01;
        File.WriteAllBytes(other, bytes);
        var handler = new StubHttpHandler().Add(SignatureUrl, Fixtures.Bytes("payload.bin.sig"));

        var ex = await Assert.ThrowsAsync<VerificationException>(() => Verifier(handler).VerifyAsync(other, Image(Sha256Of(other), signed: true)));

        Assert.Equal(VerificationFailure.BadSignature, ex.Failure);
    }

    [Fact]
    public async Task Missing_image_signature_only_warns()
    {
        var handler = new StubHttpHandler().AddStatus(SignatureUrl, HttpStatusCode.NotFound);

        var result = await Verifier(handler).VerifyAsync(Iso, Image(Sha256Of(Iso), signed: true));

        Assert.Equal(SignatureStatus.Unavailable, result.ImageSignature);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task FileHasher_matches_the_framework_implementation()
    {
        long last = 0;

        var hash = await FileHasher.ComputeAsync(Iso, HashAlgorithmKind.Sha256, new SynchronousProgress<long>(v => last = v));

        Assert.Equal(Sha256Of(Iso), hash);
        Assert.Equal(new FileInfo(Iso).Length, last);
        Assert.True(FileHasher.HashEquals(hash.ToUpperInvariant(), hash));
    }

    /// <summary>Key store serving the test keys of the fixtures folder.</summary>
    private sealed class FixtureKeyStore : IPublicKeyStore
    {
        public Task<IReadOnlyList<PgpPublicKeyRing>> GetKeysAsync(IEnumerable<string> fingerprints, CancellationToken cancellationToken = default)
        {
            var all = new[] { "test-rsa.asc", "test-ed25519.asc", "test-subkey.asc" }
                .SelectMany(f => OpenPgpVerifier.LoadKeyRings(Fixtures.Text(f)));
            var wanted = fingerprints.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<PgpPublicKeyRing> result = all.Where(r => wanted.Contains(OpenPgpVerifier.Fingerprint(r))).ToList();
            return Task.FromResult(result);
        }
    }
}
