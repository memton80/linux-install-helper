using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Disks.Windows;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace LinuxInstallHelper.Core.Tests.Disks;

/// <summary>
/// Raw writes through Windows itself, on the virtual disks that the CI attaches with <c>tools/ci/attach-test-disks.ps1</c>.
/// Skipped when their numbers are not in the environment.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRawDiskAccessTests
{
    private const string BlankDisk = "LIH_TEST_BLANK_DISK";
    private const string MountedDisk = "LIH_TEST_MOUNTED_DISK";

    private readonly ITestOutputHelper _output;

    public WindowsRawDiskAccessTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [VirtualDiskFact(BlankDisk)]
    public async Task Writes_a_drive_without_partition_table()
    {
        var disk = await VirtualDisk(BlankDisk);
        Assert.Empty(disk.DriveLetters);
        Assert.Empty(disk.VolumePaths);

        WriteAndReadBack(disk);
    }

    [VirtualDiskFact(MountedDisk)]
    public async Task Writes_a_drive_with_a_mounted_volume()
    {
        var disk = await VirtualDisk(MountedDisk);
        Assert.NotEmpty(disk.DriveLetters);

        WriteAndReadBack(disk);
    }

    private void WriteAndReadBack(DiskInfo disk)
    {
        var image = new byte[(9 * 1024 * 1024) + 1000];
        new Random(7).NextBytes(image);
        var engine = new ImageWriteEngine(TimeSpan.Zero);
        var access = new WindowsRawDiskAccess(new OutputLogger<WindowsRawDiskAccess>(_output));

        using (var device = access.Open(disk))
        {
            Assert.Equal(disk.Size, device.Size);
            Write(device, engine, image);
        }

        // Read only, like "Check my drive": the volumes Windows mounts again over the image stay mounted.
        using var reader = access.OpenForReading(disk);
        Assert.Equal(disk.Size, reader.Size);
        Assert.Throws<InvalidOperationException>(() => reader.Write(0, new byte[reader.SectorSize]));
        var comparison = new DriveImageComparer(TimeSpan.Zero).Compare(new MemoryStream(image), image.Length, reader, null, CancellationToken.None);
        Assert.Equal(DriveComparisonOutcome.Identical, comparison.Outcome);

        image[5 * 1024 * 1024] ^= 0xFF;
        var different = new DriveImageComparer(TimeSpan.Zero).Compare(new MemoryStream(image), image.Length, reader, null, CancellationToken.None);
        Assert.Equal(DriveComparisonOutcome.NearlyIdentical, different.Outcome);
        Assert.Equal(5 * 1024 * 1024, different.FirstDifference);
    }

    private static void Write(IBlockDevice device, ImageWriteEngine engine, byte[] image)
    {
        // The tail is written first, like RawDiskWriter does: with a mounted volume, it lies inside that volume.
        var tail = AlignedBuffer(ImageWriteEngine.TailWipeSize);
        tail.Span.Fill(0xA5);
        device.Write(device.Size - tail.Length, tail.Span);
        ImageWriteEngine.WipeTail(device);
        device.Read(device.Size - tail.Length, tail.Span);
        Assert.True(tail.Span.IndexOfAnyExcept((byte)0) < 0, "The end of the drive was not wiped.");

        var sha256 = engine.Write(new MemoryStream(image), image.Length, device, null, CancellationToken.None);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant(), sha256);
        engine.Verify(device, image.Length, sha256, null, CancellationToken.None);
    }

    private static async Task<DiskInfo> VirtualDisk(string variable)
    {
        var number = int.Parse(Environment.GetEnvironmentVariable(variable)!, CultureInfo.InvariantCulture);
        var disk = (await new WindowsDiskService().GetDisksAsync()).Single(d => d.Number == number);

        // Never write to anything but a small virtual disk.
        Assert.Equal(DiskBusType.FileBackedVirtual, disk.BusType);
        Assert.InRange(disk.Size, 64L * 1024 * 1024, 1024L * 1024 * 1024);
        return disk;
    }

    /// <summary>Unbuffered raw I/O needs memory aligned on the sector size: a pinned array, used from an aligned offset.</summary>
    private static Memory<byte> AlignedBuffer(int length)
    {
        const int alignment = 4096;
        var array = GC.AllocateArray<byte>(length + alignment, pinned: true);
        var address = Marshal.UnsafeAddrOfPinnedArrayElement(array, 0).ToInt64();
        var offset = (int)((alignment - (address % alignment)) % alignment);
        return array.AsMemory(offset, length);
    }

    private sealed class OutputLogger<T>(ITestOutputHelper output) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            output.WriteLine($"[{logLevel}] {formatter(state, exception)}{(exception is null ? string.Empty : $" ({exception.Message})")}");
    }
}

/// <summary>A test that needs the virtual disk whose number is in the environment variable <c>variable</c>.</summary>
public sealed class VirtualDiskFactAttribute : FactAttribute
{
    public VirtualDiskFactAttribute(string variable)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable)))
        {
            Skip = $"Needs the virtual disk attached by tools/ci/attach-test-disks.ps1 ({variable}).";
        }
    }
}
