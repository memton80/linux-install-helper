using System.Security.Cryptography;

namespace LinuxInstallHelper.Core.Verification;

/// <summary>Streaming SHA-256 / SHA-512 of large files, with progress.</summary>
public static class FileHasher
{
    public const int BufferSize = 4 * 1024 * 1024;

    public static HashAlgorithmName ToName(HashAlgorithmKind kind) =>
        kind == HashAlgorithmKind.Sha512 ? HashAlgorithmName.SHA512 : HashAlgorithmName.SHA256;

    /// <summary>Computes the lowercase hexadecimal hash of a file.</summary>
    public static async Task<string> ComputeAsync(
        string path,
        HashAlgorithmKind algorithm = HashAlgorithmKind.Sha256,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var file = OpenSequential(path);
        using var hashing = new HashingStream(file, algorithm);
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await hashing.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            progress?.Report(total);
        }

        return hashing.GetHashHex();
    }

    /// <summary>Opens a file for one fast sequential read.</summary>
    public static FileStream OpenSequential(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Case-insensitive comparison of two hexadecimal hashes.</summary>
    public static bool HashEquals(string expected, string actual) =>
        string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Read-only stream wrapper that hashes everything read through it.</summary>
public sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;

    public HashingStream(Stream inner, HashAlgorithmKind algorithm)
    {
        _inner = inner;
        _hash = IncrementalHash.CreateHash(FileHasher.ToName(algorithm));
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <summary>Lowercase hexadecimal hash of the bytes read so far (call once, at the end).</summary>
    public string GetHashHex() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        _hash.AppendData(buffer, offset, read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
