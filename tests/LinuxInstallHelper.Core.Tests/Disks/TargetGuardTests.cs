using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Tests.Disks;

public class TargetGuardTests
{
    private static readonly DiskInfo Confirmed = DiskFilterTests.UsbKey();

    [Fact]
    public void Same_disk_passes()
    {
        Assert.Equal(TargetCheck.Same, TargetGuard.Compare(Confirmed, Confirmed with { DriveLetters = ["F:"] }));
    }

    [Fact]
    public void Unplugged_disk_is_detected()
    {
        Assert.Equal(TargetCheck.Disappeared, TargetGuard.Compare(Confirmed, null));
    }

    [Fact]
    public void Another_drive_with_the_same_number_is_detected()
    {
        Assert.Equal(TargetCheck.Changed, TargetGuard.Compare(Confirmed, Confirmed with { SerialNumber = "OTHER" }));
        Assert.Equal(TargetCheck.Changed, TargetGuard.Compare(Confirmed, Confirmed with { Size = Confirmed.Size + 512 }));
        Assert.Equal(TargetCheck.Changed, TargetGuard.Compare(Confirmed, Confirmed with { UniqueId = "OTHER" }));
        Assert.Equal(TargetCheck.Changed, TargetGuard.Compare(Confirmed, Confirmed with { FriendlyName = "Kingston DataTraveler" }));
        Assert.Equal(TargetCheck.Changed, TargetGuard.Compare(Confirmed, Confirmed with { BusType = DiskBusType.Sata }));
    }

    [Fact]
    public void Missing_serial_number_alone_is_not_a_change()
    {
        Assert.Equal(TargetCheck.Same, TargetGuard.Compare(Confirmed, Confirmed with { SerialNumber = null }));
    }
}
