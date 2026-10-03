using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LinuxInstallHelper.Core.Download;

namespace LinuxInstallHelper.Core.Writing;

/// <summary>
/// Copies an image to a block device in sector-aligned chunks, hashing what is written, then optionally
/// reads it back to compare. Device independent (unit tested with an in-memory device).
/// </summary>
public sealed class ImageWriteEngine
{
    public const int ChunkSize = 4 * 1024 * 1024;

    /// <summary>Area zeroed at the end of the drive so that an old backup GPT cannot confuse firmwares.</summary>
    public const int TailWipeSize = 1024 * 1024;

    private readonly TimeSpan _progressInterval;

    public ImageWriteEngine(TimeSpan? progressInterval = null)
    {
        _progressInterval = progressInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public static long AlignUp(long value, int alignment) => (value + alignment - 1) / alignment * alignment;

    public static long AlignDown(long value, int alignment) => value / alignment * alignment;

    /// <summary>Zeroes the last megabyte of the device.</summary>
    public static void WipeTail(IBlockDevice device)
    {
        var length = (int)Math.Min(TailWipeSize, AlignDown(device.Size, device.SectorSize));
        Zero(device, AlignDown(device.Size - length, device.SectorSize), length);
    }

    /// <summary>Zeroes the first megabyte of the device, where the MBR and the primary GPT live.</summary>
    public static void WipeHead(IBlockDevice device) =>
        Zero(device, 0, (int)Math.Min(TailWipeSize, AlignDown(device.Size, device.SectorSize)));

    private static void Zero(IBlockDevice device, long offset, int length)
    {
        if (length <= 0)
        {
            return;
        }

        using var buffer = new AlignedBuffer(length, device.SectorSize);
        buffer.Span.Clear();
        device.Write(offset, buffer.Span[..length]);
    }

    /// <summary>Writes the image and returns the SHA-256 of the image bytes.</summary>
    public string Write(Stream image, long imageLength, IBlockDevice device, IProgress<UsbWriteProgress>? progress, CancellationToken cancellationToken)
    {
        if (imageLength > device.Size)
        {
            throw new UsbWriteException(UsbWriteFailure.ImageTooLarge, $"The image ({imageLength} bytes) is larger than the drive ({device.Size} bytes).");
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new AlignedBuffer(ChunkSize, device.SectorSize);
        var meter = new SpeedMeter();
        var clock = Stopwatch.StartNew();
        TimeSpan? lastReport = null;
        long written = 0;

        while (written < imageLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(ChunkSize, imageLength - written);
            var span = buffer.Span[..wanted];
            var read = image.ReadAtLeast(span, wanted, throwOnEndOfStream: false);
            if (read != wanted)
            {
                throw new IOException($"The image ended after {written + read} bytes instead of {imageLength}.");
            }

            sha.AppendData(span);

            // The last chunk is padded with zeros up to a whole sector.
            var aligned = (int)AlignUp(wanted, device.SectorSize);
            buffer.Span[wanted..aligned].Clear();
            device.Write(written, buffer.Span[..aligned]);
            written += wanted;

            var now = clock.Elapsed;
            if (lastReport is null || now - lastReport.Value >= _progressInterval || written == imageLength)
            {
                lastReport = now;
                meter.Add(now, written);
                progress?.Report(new UsbWriteProgress(UsbWriteStage.Writing, written, imageLength, meter.BytesPerSecond, meter.Remaining(written, imageLength)));
            }
        }

        progress?.Report(new UsbWriteProgress(UsbWriteStage.Flushing, written, imageLength));
        device.Flush();
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Reads the first <paramref name="imageLength"/> bytes back and compares their SHA-256.</summary>
    public void Verify(IBlockDevice device, long imageLength, string expectedSha256, IProgress<UsbWriteProgress>? progress, CancellationToken cancellationToken)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var buffer = new AlignedBuffer(ChunkSize, device.SectorSize);
        var meter = new SpeedMeter();
        var clock = Stopwatch.StartNew();
        TimeSpan? lastReport = null;
        long done = 0;

        while (done < imageLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(ChunkSize, imageLength - done);
            var aligned = (int)AlignUp(wanted, device.SectorSize);
            device.Read(done, buffer.Span[..aligned]);
            sha.AppendData(buffer.Span[..wanted]);
            done += wanted;

            var now = clock.Elapsed;
            if (lastReport is null || now - lastReport.Value >= _progressInterval || done == imageLength)
            {
                lastReport = now;
                meter.Add(now, done);
                progress?.Report(new UsbWriteProgress(UsbWriteStage.Verifying, done, imageLength, meter.BytesPerSecond, meter.Remaining(done, imageLength)));
            }
        }

        var actual = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsbWriteException(
                UsbWriteFailure.VerificationFailed,
                "The data read back from the USB drive differs from the image. The drive may be faulty or counterfeit.");
        }
    }

    /// <summary>Native memory aligned on the sector size, as required by unbuffered raw I/O.</summary>
    private sealed unsafe class AlignedBuffer : IDisposable
    {
        private readonly void* _pointer;
        private readonly int _length;

        public AlignedBuffer(int length, int alignment)
        {
            var align = (nuint)Math.Max(alignment, 4096);
            _length = (int)AlignUp(length, (int)align);
            _pointer = NativeMemory.AlignedAlloc((nuint)_length, align);
        }

        public Span<byte> Span => new(_pointer, _length);

        public void Dispose() => NativeMemory.AlignedFree(_pointer);
    }
}
