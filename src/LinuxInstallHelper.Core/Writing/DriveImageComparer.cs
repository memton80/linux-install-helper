using System.Diagnostics;
using LinuxInstallHelper.Core.Download;

namespace LinuxInstallHelper.Core.Writing;

public enum DriveComparisonOutcome
{
    /// <summary>The drive starts with exactly the bytes of the image.</summary>
    Identical,

    /// <summary>
    /// A few sectors differ: Windows (its "System Volume Information" folder) or the distribution itself writes a little on
    /// a drive that is plugged in or started. The drive is fine.
    /// </summary>
    NearlyIdentical,

    /// <summary>The drive differs from the image: it holds something else, or it is faulty.</summary>
    Different,

    /// <summary>The drive is smaller than the image: it cannot hold it.</summary>
    DriveTooSmall,
}

/// <param name="Outcome">The verdict.</param>
/// <param name="FirstDifference">Offset of the first byte that differs, null when none does.</param>
/// <param name="DifferentSectors">How many 512-byte sectors differ (counting stops past <see cref="DriveImageComparer.MinorDifferenceLimit"/>).</param>
/// <param name="BytesCompared">Bytes read from the drive.</param>
public sealed record DriveComparison(DriveComparisonOutcome Outcome, long? FirstDifference, int DifferentSectors, long BytesCompared);

/// <summary>Opens a disk to read it only: nothing is locked, dismounted or written.</summary>
public interface IRawDiskReader
{
    IBlockDevice OpenForReading(Disks.DiskInfo disk);
}

/// <summary>
/// Reads a drive back and compares it with an image, byte for byte, to tell whether the drive was written correctly with
/// this image. Read only: used to check a drive that does not start.
/// </summary>
public sealed class DriveImageComparer
{
    /// <summary>Unit of comparison, whatever the sector size of the drive.</summary>
    public const int SectorSize = 512;

    /// <summary>
    /// Up to this many different sectors (128 KB, and at most 1 % of the image), the drive is
    /// <see cref="DriveComparisonOutcome.NearlyIdentical"/>: what Windows or a distribution writes on a drive is a few
    /// sectors, a faulty drive or another image differs much more.
    /// </summary>
    public const int MinorDifferenceLimit = 256;

    private readonly TimeSpan _progressInterval;

    public DriveImageComparer(TimeSpan? progressInterval = null)
    {
        _progressInterval = progressInterval ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Compares the first <paramref name="imageLength"/> bytes of <paramref name="device"/> with <paramref name="image"/>.</summary>
    public DriveComparison Compare(Stream image, long imageLength, IBlockDevice device, IProgress<UsbWriteProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(device);

        if (imageLength > device.Size)
        {
            return new DriveComparison(DriveComparisonOutcome.DriveTooSmall, null, 0, 0);
        }

        using var buffer = new AlignedBuffer(ImageWriteEngine.ChunkSize, device.SectorSize);
        var expected = new byte[ImageWriteEngine.ChunkSize];
        var meter = new SpeedMeter();
        var clock = Stopwatch.StartNew();
        TimeSpan? lastReport = null;
        long done = 0;
        long? firstDifference = null;
        var differentSectors = 0;

        while (done < imageLength)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(ImageWriteEngine.ChunkSize, imageLength - done);
            if (image.ReadAtLeast(expected.AsSpan(0, wanted), wanted, throwOnEndOfStream: false) != wanted)
            {
                throw new IOException($"The image ended before {imageLength} bytes.");
            }

            // The drive is read by whole sectors; the padding of the last sector is not compared.
            var aligned = (int)ImageWriteEngine.AlignUp(wanted, device.SectorSize);
            device.Read(done, buffer.Span[..aligned]);
            var actual = buffer.Span[..wanted];
            var original = expected.AsSpan(0, wanted);
            if (!actual.SequenceEqual(original))
            {
                for (var offset = 0; offset < wanted; offset += SectorSize)
                {
                    var length = Math.Min(SectorSize, wanted - offset);
                    var same = actual.Slice(offset, length).CommonPrefixLength(original.Slice(offset, length));
                    if (same < length)
                    {
                        firstDifference ??= done + offset + same;
                        differentSectors++;
                    }
                }

                // Past the limit the verdict is known: no need to read gigabytes more.
                if (differentSectors > MinorDifferenceLimit)
                {
                    return new DriveComparison(DriveComparisonOutcome.Different, firstDifference, differentSectors, done + wanted);
                }
            }

            done += wanted;
            var now = clock.Elapsed;
            if (lastReport is null || now - lastReport.Value >= _progressInterval || done == imageLength)
            {
                lastReport = now;
                meter.Add(now, done);
                progress?.Report(new UsbWriteProgress(UsbWriteStage.Verifying, done, imageLength, meter.BytesPerSecond, meter.Remaining(done, imageLength)));
            }
        }

        // A small image that differs everywhere is a different image, even below the limit.
        var sectors = (imageLength + SectorSize - 1) / SectorSize;
        var outcome = differentSectors == 0 ? DriveComparisonOutcome.Identical
            : differentSectors * 100L > sectors ? DriveComparisonOutcome.Different
            : DriveComparisonOutcome.NearlyIdentical;
        return new DriveComparison(outcome, firstDifference, differentSectors, done);
    }
}
