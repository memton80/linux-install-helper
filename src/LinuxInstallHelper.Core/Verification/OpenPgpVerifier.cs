using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace LinuxInstallHelper.Core.Verification;

/// <summary>Result of an OpenPGP signature check.</summary>
/// <param name="IsValid">True when a signature made by one of the pinned keys verifies.</param>
/// <param name="SignerFingerprint">Fingerprint of the primary key that signed, when valid.</param>
/// <param name="SignatureKeyIds">Key ids announced by the signatures (useful to diagnose an unknown signer).</param>
/// <param name="Error">Why the signature is not valid.</param>
public sealed record SignatureCheckResult(bool IsValid, string? SignerFingerprint, IReadOnlyList<string> SignatureKeyIds, string? Error)
{
    public static SignatureCheckResult Invalid(string error, IReadOnlyList<string>? keyIds = null) => new(false, null, keyIds ?? [], error);
}

/// <summary>Result of a clearsigned message check.</summary>
public sealed record ClearSignedResult(SignatureCheckResult Signature, string Content);

/// <summary>
/// Verifies OpenPGP signatures (detached or clearsigned) against a set of pinned public keys.
/// Only keys whose primary fingerprint is pinned are ever used.
/// </summary>
public static class OpenPgpVerifier
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Loads one or more armored or binary public key rings.</summary>
    public static IReadOnlyList<PgpPublicKeyRing> LoadKeyRings(string armoredKeys)
    {
        using var input = new MemoryStream(Encoding.ASCII.GetBytes(armoredKeys));
        return LoadKeyRings(input);
    }

    public static IReadOnlyList<PgpPublicKeyRing> LoadKeyRings(Stream input)
    {
        using var decoder = PgpUtilities.GetDecoderStream(input);
        var bundle = new PgpPublicKeyRingBundle(decoder);
        return bundle.GetKeyRings().ToList();
    }

    /// <summary>Uppercase hexadecimal fingerprint of the primary key of a ring.</summary>
    public static string Fingerprint(PgpPublicKeyRing ring) => Convert.ToHexString(ring.GetPublicKey().GetFingerprint());

    /// <summary>Verifies a detached signature over <paramref name="data"/>, reading it in a single pass.</summary>
    public static async Task<SignatureCheckResult> VerifyDetachedAsync(
        Stream data,
        byte[] signature,
        IReadOnlyCollection<PgpPublicKeyRing> pinnedKeys,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(pinnedKeys);

        IReadOnlyList<PgpSignature> signatures;
        try
        {
            signatures = ReadSignatures(new MemoryStream(signature));
        }
        catch (Exception ex) when (ex is IOException or PgpException or ArgumentException)
        {
            return SignatureCheckResult.Invalid($"The signature file cannot be read: {ex.Message}");
        }

        var candidates = SelectCandidates(signatures, pinnedKeys, out var keyIds, out var selectionError);
        if (candidates.Count == 0)
        {
            return SignatureCheckResult.Invalid(selectionError, keyIds);
        }

        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await data.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            foreach (var candidate in candidates)
            {
                candidate.Signature.Update(buffer, 0, read);
            }

            total += read;
            progress?.Report(total);
        }

        return Conclude(candidates, keyIds);
    }

    /// <summary>Verifies a detached signature over an in-memory payload.</summary>
    public static SignatureCheckResult VerifyDetached(byte[] data, byte[] signature, IReadOnlyCollection<PgpPublicKeyRing> pinnedKeys)
    {
        using var stream = new MemoryStream(data, writable: false);
        return VerifyDetachedAsync(stream, signature, pinnedKeys).GetAwaiter().GetResult();
    }

    /// <summary>True when the text is an OpenPGP clearsigned message.</summary>
    public static bool IsClearSigned(string text) =>
        text.TrimStart().StartsWith("-----BEGIN PGP SIGNED MESSAGE-----", StringComparison.Ordinal);

    /// <summary>Verifies a clearsigned message and returns its content (with <c>\n</c> line endings).</summary>
    public static ClearSignedResult VerifyClearSigned(string message, IReadOnlyCollection<PgpPublicKeyRing> pinnedKeys)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(pinnedKeys);

        if (!IsClearSigned(message))
        {
            return new ClearSignedResult(SignatureCheckResult.Invalid("The file is not a clearsigned OpenPGP message."), message);
        }

        try
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(message));
            using var armored = new ArmoredInputStream(input);

            var lines = ReadClearTextLines(armored);
            var content = string.Join("\n", lines.Select(l => Encoding.UTF8.GetString(l))) + "\n";

            var signatures = ReadSignatures(new PgpObjectFactory(armored));
            var candidates = SelectCandidates(signatures, pinnedKeys, out var keyIds, out var selectionError);
            if (candidates.Count == 0)
            {
                return new ClearSignedResult(SignatureCheckResult.Invalid(selectionError, keyIds), content);
            }

            // RFC 4880 §7.1: lines are joined with CRLF, trailing whitespace removed, no final line ending.
            var crlf = "\r\n"u8.ToArray();
            for (var i = 0; i < lines.Count; i++)
            {
                foreach (var candidate in candidates)
                {
                    if (i > 0)
                    {
                        candidate.Signature.Update(crlf);
                    }

                    candidate.Signature.Update(lines[i]);
                }
            }

            return new ClearSignedResult(Conclude(candidates, keyIds), content);
        }
        catch (Exception ex) when (ex is IOException or PgpException or InvalidCastException or ArgumentException)
        {
            return new ClearSignedResult(SignatureCheckResult.Invalid($"The clearsigned message cannot be read: {ex.Message}"), message);
        }
    }

    private static SignatureCheckResult Conclude(IReadOnlyList<Candidate> candidates, IReadOnlyList<string> keyIds)
    {
        string? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                if (candidate.Signature.Verify())
                {
                    return new SignatureCheckResult(true, Fingerprint(candidate.Ring), keyIds, null);
                }

                lastError = $"BAD signature from key {candidate.KeyId:X16}: the file was modified or corrupted.";
            }
            catch (PgpException ex)
            {
                lastError = $"The signature from key {candidate.KeyId:X16} cannot be checked: {ex.Message}";
            }
        }

        return SignatureCheckResult.Invalid(lastError ?? "No valid signature.", keyIds);
    }

    private sealed record Candidate(PgpSignature Signature, PgpPublicKeyRing Ring, long KeyId);

    private static List<Candidate> SelectCandidates(
        IReadOnlyList<PgpSignature> signatures,
        IReadOnlyCollection<PgpPublicKeyRing> pinnedKeys,
        out IReadOnlyList<string> keyIds,
        out string error)
    {
        keyIds = signatures.Select(s => s.KeyId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        error = signatures.Count == 0
            ? "The signature file does not contain any signature."
            : $"The signature was made by an unknown key ({string.Join(", ", keyIds)}), not by a pinned key.";

        var candidates = new List<Candidate>();
        foreach (var signature in signatures)
        {
            if (signature.SignatureType is not (PgpSignature.BinaryDocument or PgpSignature.CanonicalTextDocument))
            {
                continue;
            }

            foreach (var ring in pinnedKeys)
            {
                var key = ring.GetPublicKey(signature.KeyId);
                if (key is null)
                {
                    continue;
                }

                var keyError = CheckSigningKey(ring, key, signature.CreationTime);
                if (keyError is not null)
                {
                    error = keyError;
                    continue;
                }

                signature.InitVerify(key);
                candidates.Add(new Candidate(signature, ring, signature.KeyId));
                break;
            }
        }

        return candidates;
    }

    /// <summary>Checks that the key was valid when the signature was made (not revoked, not expired, bound to the primary).</summary>
    private static string? CheckSigningKey(PgpPublicKeyRing ring, PgpPublicKey key, DateTime signatureTime)
    {
        var primary = ring.GetPublicKey();
        if (primary.HasRevocation())
        {
            return $"The key {Fingerprint(ring)} has been revoked.";
        }

        if (!key.IsMasterKey)
        {
            if (key.HasRevocation())
            {
                return $"The subkey {key.KeyId:X16} has been revoked.";
            }

            if (!HasValidBinding(primary, key))
            {
                return $"The subkey {key.KeyId:X16} is not validly bound to {Fingerprint(ring)}.";
            }
        }

        if (signatureTime < key.CreationTime.AddMinutes(-5))
        {
            return $"The signature predates the key {key.KeyId:X16}.";
        }

        var validSeconds = key.GetValidSeconds();
        if (validSeconds > 0 && signatureTime > key.CreationTime.AddSeconds(validSeconds))
        {
            return $"The key {key.KeyId:X16} had expired when the signature was made.";
        }

        return null;
    }

    private static bool HasValidBinding(PgpPublicKey primary, PgpPublicKey subKey)
    {
        foreach (var signature in subKey.GetSignaturesOfType(PgpSignature.SubkeyBinding))
        {
            try
            {
                if (signature.KeyId != primary.KeyId)
                {
                    continue;
                }

                signature.InitVerify(primary);
                if (signature.VerifyCertification(primary, subKey))
                {
                    return true;
                }
            }
            catch (PgpException)
            {
                // Try the next binding signature.
            }
        }

        return false;
    }

    private static IReadOnlyList<PgpSignature> ReadSignatures(Stream input)
    {
        using var decoder = PgpUtilities.GetDecoderStream(input);
        return ReadSignatures(new PgpObjectFactory(decoder));
    }

    private static IReadOnlyList<PgpSignature> ReadSignatures(PgpObjectFactory factory)
    {
        var result = new List<PgpSignature>();

        PgpObject? obj;
        while ((obj = factory.NextPgpObject()) is not null)
        {
            switch (obj)
            {
                case PgpSignatureList list:
                    for (var i = 0; i < list.Count; i++)
                    {
                        result.Add(list[i]);
                    }

                    break;

                case PgpCompressedData compressed:
                    var inner = new PgpObjectFactory(compressed.GetDataStream());
                    if (inner.NextPgpObject() is PgpSignatureList innerList)
                    {
                        for (var i = 0; i < innerList.Count; i++)
                        {
                            result.Add(innerList[i]);
                        }
                    }

                    break;

                case PgpMarker:
                    break;

                default:
                    return result;
            }
        }

        return result;
    }

    /// <summary>Reads the cleartext part of a clearsigned message, one entry per line, trailing whitespace removed.</summary>
    private static List<byte[]> ReadClearTextLines(ArmoredInputStream armored)
    {
        var lines = new List<byte[]>();
        var current = new MemoryStream();
        var pendingCarriageReturn = false;

        int ch;
        while (armored.IsClearText() && (ch = armored.ReadByte()) >= 0)
        {
            if (pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
                if (ch == '\n')
                {
                    continue;
                }
            }

            if (ch == '\r' || ch == '\n')
            {
                lines.Add(TrimTrailingWhitespace(current.ToArray()));
                current.SetLength(0);
                pendingCarriageReturn = ch == '\r';
                continue;
            }

            current.WriteByte((byte)ch);
        }

        // The reader hands out the first byte of the "-----BEGIN PGP SIGNATURE-----" line before it
        // leaves the cleartext state: that partial line belongs to the armor, not to the message.
        if (current.Length > 0 && armored.IsClearText())
        {
            lines.Add(TrimTrailingWhitespace(current.ToArray()));
        }

        return lines;
    }

    private static byte[] TrimTrailingWhitespace(byte[] line)
    {
        var end = line.Length;
        while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t'))
        {
            end--;
        }

        return end == line.Length ? line : line[..end];
    }
}
