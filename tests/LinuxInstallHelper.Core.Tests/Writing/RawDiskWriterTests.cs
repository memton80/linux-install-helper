using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Tests.Disks;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Writing;

namespace LinuxInstallHelper.Core.Tests.Writing;

public sealed class RawDiskWriterTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _iso;
    private readonly byte[] _image;

    public RawDiskWriterTests()
    {
        _image = new byte[3 * 1024 * 1024 + 77];
        new Random(3).NextBytes(_image);
        _iso = _temp.File("distro.iso");
        File.WriteAllBytes(_iso, _image);
    }

    public void Dispose() => _temp.Dispose();

    private const long KeySize = 1_000_000_000;

    private static DiskInfo Key(long size = KeySize) => DiskFilterTests.UsbKey(size: size);

    [Fact]
    public async Task Writes_and_verifies_the_confirmed_drive()
    {
        var device = new SparseBlockDevice(KeySize);
        var disks = new FakeDiskService([Key()]);
        var writer = new RawDiskWriter(disks, new FakeAccess(device), new ImageWriteEngine(TimeSpan.Zero));

        var result = await writer.WriteAsync(new UsbWriteRequest(_iso, Key()));

        Assert.True(result.Verified);
        Assert.Equal(_image.Length, result.BytesWritten);
        Assert.Equal(_image, device.ReadRange(0, _image.Length));
        Assert.All(device.ReadRange(KeySize - ImageWriteEngine.TailWipeSize, ImageWriteEngine.TailWipeSize), b => Assert.Equal(0, b));
        Assert.True(device.Disposed);
        Assert.Contains(_iso, disks.LastExtraPaths);
    }

    [Fact]
    public async Task Refuses_a_device_whose_size_changed()
    {
        var device = new SparseBlockDevice(KeySize - 512);
        var writer = new RawDiskWriter(new FakeDiskService([Key()]), new FakeAccess(device));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => writer.WriteAsync(new UsbWriteRequest(_iso, Key())));

        Assert.Equal(UsbWriteFailure.TargetChanged, ex.Failure);
    }

    [Fact]
    public async Task Refuses_a_drive_replaced_since_confirmation()
    {
        var device = new SparseBlockDevice(KeySize);
        var writer = new RawDiskWriter(new FakeDiskService([Key() with { SerialNumber = "SWAPPED" }]), new FakeAccess(device));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => writer.WriteAsync(new UsbWriteRequest(_iso, Key())));

        Assert.Equal(UsbWriteFailure.TargetChanged, ex.Failure);
        Assert.False(device.Opened);
    }

    [Fact]
    public async Task Refuses_an_unplugged_drive()
    {
        var device = new SparseBlockDevice(KeySize);
        var writer = new RawDiskWriter(new FakeDiskService([]), new FakeAccess(device));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => writer.WriteAsync(new UsbWriteRequest(_iso, Key())));

        Assert.Equal(UsbWriteFailure.TargetChanged, ex.Failure);
    }

    [Fact]
    public async Task Refuses_a_drive_that_became_protected()
    {
        var device = new SparseBlockDevice(KeySize);
        var disks = new FakeDiskService([Key()]) { Protected = new HashSet<int> { Key().Number } };
        var writer = new RawDiskWriter(disks, new FakeAccess(device));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => writer.WriteAsync(new UsbWriteRequest(_iso, Key())));

        Assert.Equal(UsbWriteFailure.TargetRejected, ex.Failure);
        Assert.False(device.Opened);
    }

    [Fact]
    public async Task Refuses_a_drive_too_small_to_be_a_usb_flash_drive()
    {
        var small = Key(size: 8 * 1024 * 1024);
        var device = new SparseBlockDevice(small.Size);
        var writer = new RawDiskWriter(new FakeDiskService([small]), new FakeAccess(device));

        var ex = await Assert.ThrowsAsync<UsbWriteException>(() => writer.WriteAsync(new UsbWriteRequest(_iso, small)));

        Assert.Equal(UsbWriteFailure.TargetRejected, ex.Failure);
        Assert.False(device.Opened);
    }

    private sealed class FakeDiskService(IReadOnlyList<DiskInfo> disks) : IDiskService
    {
        public IReadOnlySet<int> Protected { get; init; } = new HashSet<int>();

        public List<string> LastExtraPaths { get; } = [];

        public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default) => Task.FromResult(disks);

        public Task<IReadOnlySet<int>> GetProtectedDisksAsync(IEnumerable<string> extraPaths, CancellationToken cancellationToken = default)
        {
            LastExtraPaths.AddRange(extraPaths);
            return Task.FromResult(Protected);
        }
    }

    private sealed class FakeAccess(SparseBlockDevice device) : IRawDiskAccess
    {
        public IBlockDevice Open(DiskInfo disk)
        {
            device.Opened = true;
            return device;
        }
    }
}
