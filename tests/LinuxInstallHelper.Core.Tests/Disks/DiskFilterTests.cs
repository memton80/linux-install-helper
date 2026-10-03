using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Tests.Disks;

public class DiskFilterTests
{
    private static readonly IReadOnlySet<int> NoneProtected = new HashSet<int>();

    public static DiskInfo UsbKey(int number = 2, long size = 32_015_679_488) => new()
    {
        Number = number,
        FriendlyName = "SanDisk Ultra",
        SerialNumber = "4C530001230719117283",
        UniqueId = "USBSTOR\\DISK&VEN_SANDISK&PROD_ULTRA\\4C530001230719117283&0",
        Size = size,
        BusType = DiskBusType.Usb,
        IsRemovableMedia = true,
        DriveLetters = ["E:"],
    };

    [Fact]
    public void A_usb_flash_drive_is_eligible()
    {
        Assert.Equal(DiskRejection.None, DiskFilter.Evaluate(UsbKey(), NoneProtected));
    }

    [Theory]
    [InlineData(DiskBusType.Nvme)]
    [InlineData(DiskBusType.Sata)]
    [InlineData(DiskBusType.Sas)]
    [InlineData(DiskBusType.Raid)]
    [InlineData(DiskBusType.Sd)]
    [InlineData(DiskBusType.Virtual)]
    [InlineData(DiskBusType.FileBackedVirtual)]
    [InlineData(DiskBusType.StorageSpaces)]
    [InlineData(DiskBusType.Unknown)]
    public void Internal_and_virtual_disks_are_never_eligible(DiskBusType bus)
    {
        Assert.Equal(DiskRejection.NotUsb, DiskFilter.Evaluate(UsbKey() with { BusType = bus }, NoneProtected));
    }

    [Fact]
    public void The_system_disk_is_rejected_even_on_usb()
    {
        Assert.Equal(DiskRejection.SystemDisk, DiskFilter.Evaluate(UsbKey() with { IsSystem = true }, NoneProtected));
        Assert.Equal(DiskRejection.BootDisk, DiskFilter.Evaluate(UsbKey() with { IsBoot = true }, NoneProtected));
    }

    [Fact]
    public void A_disk_holding_protected_data_is_rejected()
    {
        Assert.Equal(DiskRejection.HostsProtectedData, DiskFilter.Evaluate(UsbKey(number: 3), new HashSet<int> { 0, 3 }));
    }

    [Fact]
    public void Offline_and_read_only_disks_are_rejected()
    {
        Assert.Equal(DiskRejection.Offline, DiskFilter.Evaluate(UsbKey() with { IsOffline = true }, NoneProtected));
        Assert.Equal(DiskRejection.ReadOnly, DiskFilter.Evaluate(UsbKey() with { IsReadOnly = true }, NoneProtected));
    }

    [Theory]
    [InlineData(0L, DiskRejection.TooSmall)]
    [InlineData(512L * 1024 * 1024, DiskRejection.TooSmall)]
    [InlineData(1_000_000_000L, DiskRejection.None)]
    [InlineData(256_060_514_304L, DiskRejection.None)]
    [InlineData(500_107_862_016L, DiskRejection.TooLarge)]
    [InlineData(2_000_398_934_016L, DiskRejection.TooLarge)]
    public void Size_must_be_plausible_for_a_usb_flash_drive(long size, DiskRejection expected)
    {
        Assert.Equal(expected, DiskFilter.Evaluate(UsbKey(size: size), NoneProtected));
    }

    [Fact]
    public void Image_must_fit_on_the_drive()
    {
        var key = UsbKey(size: 4_000_000_000);

        Assert.Equal(DiskRejection.TooSmallForImage, DiskFilter.EvaluateForImage(key, 6_482_409_472, NoneProtected));
        Assert.Equal(DiskRejection.None, DiskFilter.EvaluateForImage(key, 2_927_861_760, NoneProtected));
    }

    [Fact]
    public void Eligible_keeps_only_usb_drives_sorted_by_number()
    {
        var disks = new[]
        {
            UsbKey(number: 4),
            UsbKey(number: 0) with { BusType = DiskBusType.Nvme, IsSystem = true, IsBoot = true, Size = 1_000_204_886_016 },
            UsbKey(number: 1) with { BusType = DiskBusType.Sata, Size = 2_000_398_934_016 },
            UsbKey(number: 2),
            UsbKey(number: 3) with { Size = 1_000_204_886_016 },
        };

        var eligible = DiskFilter.Eligible(disks, NoneProtected);

        Assert.Equal([2, 4], eligible.Select(d => d.Number));
    }
}
