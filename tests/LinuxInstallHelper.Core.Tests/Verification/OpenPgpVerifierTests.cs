using System.Text;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Verification;

namespace LinuxInstallHelper.Core.Tests.Verification;

public class OpenPgpVerifierTests
{
    private static IReadOnlyList<Org.BouncyCastle.Bcpg.OpenPgp.PgpPublicKeyRing> Keys(string fixture) =>
        OpenPgpVerifier.LoadKeyRings(Fixtures.Text(fixture));

    [Fact]
    public void Verifies_the_real_ubuntu_signature_with_the_embedded_key()
    {
        var keys = OpenPgpVerifier.LoadKeyRings(EmbeddedResources.TryReadKey(Fixtures.UbuntuFingerprint)!);

        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("ubuntu-SHA256SUMS"), Fixtures.Bytes("ubuntu-SHA256SUMS.gpg"), keys);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Fixtures.UbuntuFingerprint, result.SignerFingerprint);
    }

    [Fact]
    public void Detects_a_modified_checksum_file()
    {
        var keys = OpenPgpVerifier.LoadKeyRings(EmbeddedResources.TryReadKey(Fixtures.UbuntuFingerprint)!);
        var data = Fixtures.Bytes("ubuntu-SHA256SUMS");
        data[0] = data[0] == (byte)'0' ? (byte)'1' : (byte)'0';

        var result = OpenPgpVerifier.VerifyDetached(data, Fixtures.Bytes("ubuntu-SHA256SUMS.gpg"), keys);

        Assert.False(result.IsValid);
        Assert.Contains("BAD signature", result.Error);
    }

    [Fact]
    public void Rejects_a_signature_from_a_key_that_is_not_pinned()
    {
        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("payload.bin"), Fixtures.Bytes("payload.bin.sig"), Keys("test-rsa.asc"));

        Assert.False(result.IsValid);
        Assert.Contains("unknown key", result.Error);
        Assert.NotEmpty(result.SignatureKeyIds);
    }

    [Fact]
    public void Verifies_an_ed25519_binary_signature()
    {
        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("payload.bin"), Fixtures.Bytes("payload.bin.sig"), Keys("test-ed25519.asc"));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Fixtures.Fingerprint("ed25519"), result.SignerFingerprint);
    }

    [Fact]
    public void Verifies_an_armored_rsa_signature()
    {
        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("payload.bin"), Fixtures.Bytes("payload.bin.rsa.asc"), Keys("test-rsa.asc"));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Verifies_a_signature_made_by_a_signing_subkey()
    {
        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("payload.bin"), Fixtures.Bytes("payload.bin.subkey.sig"), Keys("test-subkey.asc"));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(Fixtures.Fingerprint("subkey"), result.SignerFingerprint);
    }

    [Fact]
    public async Task Streams_large_data_and_reports_progress()
    {
        long reported = 0;
        await using var stream = File.OpenRead(Fixtures.PathOf("payload.bin"));

        var result = await OpenPgpVerifier.VerifyDetachedAsync(
            stream,
            Fixtures.Bytes("payload.bin.sig"),
            Keys("test-ed25519.asc"),
            new SynchronousProgress<long>(v => reported = v));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(new FileInfo(Fixtures.PathOf("payload.bin")).Length, reported);
    }

    [Fact]
    public void Garbage_signature_is_reported_not_thrown()
    {
        var result = OpenPgpVerifier.VerifyDetached(Fixtures.Bytes("payload.bin"), Encoding.ASCII.GetBytes("not a signature"), Keys("test-rsa.asc"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Verifies_a_clearsigned_message_and_returns_its_content()
    {
        var result = OpenPgpVerifier.VerifyClearSigned(Fixtures.Text("clearsigned-CHECKSUM"), Keys("test-rsa.asc"));

        Assert.True(result.Signature.IsValid, result.Signature.Error);
        Assert.Contains("SHA256 (Fedora-Like-1.0.x86_64.iso) = 0361c13141e6f57e24d6ee5227066c33a45f7f92a95f41d0bbd343e4fd05da18", result.Content);
        Assert.Contains("\n- dash line that must be escaped", result.Content);
        Assert.Equal(
            "0361c13141e6f57e24d6ee5227066c33a45f7f92a95f41d0bbd343e4fd05da18",
            ChecksumFile.Parse(result.Content).Find("Fedora-Like-1.0.x86_64.iso"));
    }

    [Fact]
    public void Detects_a_modified_clearsigned_message()
    {
        var tampered = Fixtures.Text("clearsigned-CHECKSUM").Replace("0361c131", "0361c132", StringComparison.Ordinal);

        var result = OpenPgpVerifier.VerifyClearSigned(tampered, Keys("test-rsa.asc"));

        Assert.False(result.Signature.IsValid);
    }

    [Fact]
    public void Clearsigned_message_from_another_key_is_rejected()
    {
        var result = OpenPgpVerifier.VerifyClearSigned(Fixtures.Text("clearsigned-CHECKSUM"), Keys("test-ed25519.asc"));

        Assert.False(result.Signature.IsValid);
    }

    [Fact]
    public void Plain_text_is_not_clearsigned()
    {
        Assert.False(OpenPgpVerifier.IsClearSigned("abc  file.iso"));
        Assert.True(OpenPgpVerifier.IsClearSigned(Fixtures.Text("clearsigned-CHECKSUM")));
    }
}

/// <summary>IProgress that reports synchronously (Progress&lt;T&gt; posts to the thread pool).</summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
