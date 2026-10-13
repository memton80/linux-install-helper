using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Writing;

public class DriveImageComparerTests
{
    private static readonly DriveImageComparer Comparer = new(TimeSpan.Zero);

    [Fact]
    public void A_drive_written_with_the_image_is_identical()
    {
        var image = Image((ImageWriteEngine.ChunkSize * 2) + 1000);
        var device = Written(image, 16 * 1024 * 1024);
        var reports = new List<UsbWriteProgress>();

        var result = Comparer.Compare(new MemoryStream(image), image.Length, device, new SyncProgress(reports), CancellationToken.None);

        Assert.Equal(DriveComparisonOutcome.Identical, result.Outcome);
        Assert.Null(result.FirstDifference);
        Assert.Equal(image.Length, result.BytesCompared);
        Assert.Equal(image.Length, reports[^1].BytesDone);
        Assert.All(reports, r => Assert.Equal(UsbWriteStage.Verifying, r.Stage));
    }

    [Fact]
    public void A_few_changed_sectors_are_a_nearly_identical_drive()
    {
        var image = Image((ImageWriteEngine.ChunkSize * 2) + 1000);
        var device = Written(image, 16 * 1024 * 1024);
        device.Data[ImageWriteEngine.ChunkSize + 77] ^= 0x01;
        device.Data[ImageWriteEngine.ChunkSize + 80] ^= 0x01;
        device.Data[(ImageWriteEngine.ChunkSize * 2) + 999] ^= 0x01;

        var result = Comparer.Compare(new MemoryStream(image), image.Length, device, null, CancellationToken.None);

        Assert.Equal(DriveComparisonOutcome.NearlyIdentical, result.Outcome);
        Assert.Equal(ImageWriteEngine.ChunkSize + 77, result.FirstDifference);
        Assert.Equal(2, result.DifferentSectors);
        Assert.Equal(image.Length, result.BytesCompared);
    }

    [Fact]
    public void Many_changed_sectors_are_a_different_drive()
    {
        var image = Image(ImageWriteEngine.ChunkSize * 3);
        var device = Written(image, 16 * 1024 * 1024);
        for (var sector = 0; sector <= DriveImageComparer.MinorDifferenceLimit; sector++)
        {
            device.Data[(sector * DriveImageComparer.SectorSize) + 3] ^= 0xFF;
        }

        var result = Comparer.Compare(new MemoryStream(image), image.Length, device, null, CancellationToken.None);

        Assert.Equal(DriveComparisonOutcome.Different, result.Outcome);
        Assert.Equal(3, result.FirstDifference);
        Assert.Equal(DriveImageComparer.MinorDifferenceLimit + 1, result.DifferentSectors);
        Assert.Equal(ImageWriteEngine.ChunkSize, result.BytesCompared);
    }

    [Fact]
    public void The_padding_of_the_last_sector_is_not_compared()
    {
        var image = Image(1000);
        var device = Written(image, 1024 * 1024);
        device.Data[1000] = 0x42;

        Assert.Equal(DriveComparisonOutcome.Identical, Comparer.Compare(new MemoryStream(image), image.Length, device, null, CancellationToken.None).Outcome);
    }

    [Fact]
    public void A_drive_smaller_than_the_image_cannot_hold_it()
    {
        var image = Image(4096);
        var device = new MemoryBlockDevice(2048);

        var result = Comparer.Compare(new MemoryStream(image), image.Length, device, null, CancellationToken.None);

        Assert.Equal(DriveComparisonOutcome.DriveTooSmall, result.Outcome);
        Assert.Equal(0, result.BytesCompared);
    }

    [Fact]
    public void Another_image_is_different_from_the_first_byte()
    {
        var device = Written(Image(8192), 1024 * 1024);
        var other = Image(8192, seed: 2);

        var result = Comparer.Compare(new MemoryStream(other), other.Length, device, null, CancellationToken.None);

        Assert.Equal(DriveComparisonOutcome.Different, result.Outcome);
        Assert.Equal(0, result.FirstDifference);
    }

    [Fact]
    public void Can_be_cancelled()
    {
        var image = Image(8192);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => Comparer.Compare(new MemoryStream(image), image.Length, Written(image, 1024 * 1024), null, cancelled.Token));
    }

    private static byte[] Image(int length, int seed = 1)
    {
        var image = new byte[length];
        new Random(seed).NextBytes(image);
        return image;
    }

    private static MemoryBlockDevice Written(byte[] image, long size)
    {
        var device = new MemoryBlockDevice(size);
        image.CopyTo(device.Data, 0);
        return device;
    }

    private sealed class SyncProgress(List<UsbWriteProgress> reports) : IProgress<UsbWriteProgress>
    {
        public void Report(UsbWriteProgress value) => reports.Add(value);
    }
}
