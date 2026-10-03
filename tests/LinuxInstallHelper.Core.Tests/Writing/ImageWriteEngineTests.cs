using System.Security.Cryptography;
using LinuxInstallHelper.Core.Tests.Verification;
using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Writing;

public class ImageWriteEngineTests
{
    private static byte[] Image(int length)
    {
        var bytes = new byte[length];
        new Random(7).NextBytes(bytes);
        return bytes;
    }

    private static readonly ImageWriteEngine Engine = new(TimeSpan.Zero);

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    public void Writes_an_unaligned_image_and_pads_the_last_sector(int sectorSize)
    {
        var image = Image((9 * 1024 * 1024) + 123);
        var device = new MemoryBlockDevice(16 * 1024 * 1024, sectorSize);
        var reports = new List<UsbWriteProgress>();

        var hash = Engine.Write(new MemoryStream(image), image.Length, device, new SynchronousProgress<UsbWriteProgress>(reports.Add), CancellationToken.None);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(), hash);
        Assert.Equal(image, device.Data[..image.Length]);
        var paddedEnd = (int)ImageWriteEngine.AlignUp(image.Length, sectorSize);
        Assert.All(device.Data[image.Length..paddedEnd], b => Assert.Equal(0, b));
        Assert.Equal(0xAA, device.Data[paddedEnd]);
        Assert.True(device.Flushed);
        Assert.Contains(reports, r => r.Stage == UsbWriteStage.Writing && r.BytesDone == image.Length);
    }

    [Fact]
    public void Verification_passes_for_a_good_write()
    {
        var image = Image(5 * 1024 * 1024);
        var device = new MemoryBlockDevice(8 * 1024 * 1024);
        var hash = Engine.Write(new MemoryStream(image), image.Length, device, null, CancellationToken.None);

        Engine.Verify(device, image.Length, hash, null, CancellationToken.None);
    }

    [Fact]
    public void Verification_detects_a_faulty_drive()
    {
        var image = Image(5 * 1024 * 1024);
        var device = new MemoryBlockDevice(8 * 1024 * 1024) { CorruptAt = 4_500_000 };
        var hash = Engine.Write(new MemoryStream(image), image.Length, device, null, CancellationToken.None);

        var ex = Assert.Throws<UsbWriteException>(() => Engine.Verify(device, image.Length, hash, null, CancellationToken.None));

        Assert.Equal(UsbWriteFailure.VerificationFailed, ex.Failure);
    }

    [Fact]
    public void Refuses_an_image_larger_than_the_drive()
    {
        var device = new MemoryBlockDevice(1024 * 1024);

        var ex = Assert.Throws<UsbWriteException>(() => Engine.Write(new MemoryStream(Image(2 * 1024 * 1024)), 2 * 1024 * 1024, device, null, CancellationToken.None));

        Assert.Equal(UsbWriteFailure.ImageTooLarge, ex.Failure);
    }

    [Fact]
    public void Truncated_image_is_an_error()
    {
        var device = new MemoryBlockDevice(8 * 1024 * 1024);

        Assert.Throws<IOException>(() => Engine.Write(new MemoryStream(Image(1000)), 5000, device, null, CancellationToken.None));
    }

    [Fact]
    public void Cancellation_stops_between_chunks()
    {
        var device = new MemoryBlockDevice(16 * 1024 * 1024);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => Engine.Write(new MemoryStream(Image(9 * 1024 * 1024)), 9 * 1024 * 1024, device, null, cts.Token));
    }

    [Fact]
    public void WipeTail_zeroes_the_last_megabyte_only()
    {
        var device = new MemoryBlockDevice(4 * 1024 * 1024);

        ImageWriteEngine.WipeTail(device);

        Assert.All(device.Data[^ImageWriteEngine.TailWipeSize..], b => Assert.Equal(0, b));
        Assert.Equal(0xAA, device.Data[^(ImageWriteEngine.TailWipeSize + 1)]);
    }

    [Theory]
    [InlineData(0, 512, 0)]
    [InlineData(1, 512, 512)]
    [InlineData(512, 512, 512)]
    [InlineData(513, 4096, 4096)]
    public void AlignUp_rounds_to_the_next_sector(long value, int alignment, long expected)
    {
        Assert.Equal(expected, ImageWriteEngine.AlignUp(value, alignment));
    }
}
