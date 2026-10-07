using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Readiness;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Readiness;

public class PcReadinessTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static readonly PcFacts ReadyPc = new()
    {
        Processor = ProcessorKind.X64,
        Firmware = FirmwareKind.Uefi,
        SecureBootEnabled = true,
        SystemDiskBus = DiskBusType.Nvme,
        StorageControllers = ["Standard NVM Express Controller"],
        SystemDriveEncrypted = false,
        FastStartupEnabled = false,
        SystemDriveFreeBytes = 200 * Gib,
        MemoryBytes = 16 * Gib,
        GraphicsAdapters = ["Intel(R) Iris(R) Xe Graphics"],
        WirelessAdapters = ["Intel(R) Wi-Fi 6 AX201 160MHz"],
    };

    [Fact]
    public void A_ready_computer_has_nothing_to_fix()
    {
        var items = PcReadiness.Evaluate(ReadyPc, TestDistros.Get("ubuntu-desktop"));

        Assert.All(items, item => Assert.Equal(ReadinessLevel.Ok, item.Level));
        Assert.Equal(Enum.GetValues<ReadinessCheck>(), items.Select(i => i.Check));
        Assert.Equal(ReadinessLevel.Ok, PcReadiness.Worst(items));
    }

    [Fact]
    public void Unknown_facts_are_left_out()
    {
        var items = PcReadiness.Evaluate(new PcFacts(), null);

        Assert.Empty(items);
    }

    [Fact]
    public void An_arm_computer_cannot_start_an_x64_drive()
    {
        var arm = ReadyPc with { Processor = ProcessorKind.Arm64 };

        var item = Single(PcReadiness.Evaluate(arm, TestDistros.Get("linuxmint-cinnamon")), ReadinessCheck.Processor);

        Assert.Equal(ReadinessLevel.Blocker, item.Level);
        Assert.Equal("Processor_Arm64", item.Key);
        Assert.Equal("Linux Mint Cinnamon", item.Name);
        Assert.True(PcReadiness.CannotStart(ProcessorKind.Arm64, TestDistros.Get("ubuntu-desktop")));
        Assert.False(PcReadiness.CannotStart(ProcessorKind.X64, TestDistros.Get("ubuntu-desktop")));
        Assert.False(PcReadiness.CannotStart(ProcessorKind.Unknown, TestDistros.Get("ubuntu-desktop")));
    }

    [Fact]
    public void An_arm_computer_is_a_blocker_without_a_chosen_distribution()
    {
        var item = Single(PcReadiness.Evaluate(ReadyPc with { Processor = ProcessorKind.Arm64 }, null), ReadinessCheck.Processor);

        Assert.Equal(ReadinessLevel.Blocker, item.Level);
        Assert.Equal("Arm64", item.Variant);
    }

    [Fact]
    public void Secure_boot_must_be_turned_off_for_a_distribution_that_does_not_support_it()
    {
        var arch = TestDistros.Get("archlinux");

        var item = Single(PcReadiness.Evaluate(ReadyPc, arch), ReadinessCheck.SecureBoot);

        Assert.Equal(ReadinessLevel.Warning, item.Level);
        Assert.Equal("MustDisable", item.Variant);
        Assert.Equal(ReadinessAction.OpenFirmwareSettings, item.Action);

        var off = Single(PcReadiness.Evaluate(ReadyPc with { SecureBootEnabled = false }, arch), ReadinessCheck.SecureBoot);
        Assert.Equal(ReadinessLevel.Ok, off.Level);
        Assert.Equal("Off", off.Variant);
    }

    [Fact]
    public void Secure_boot_is_not_checked_in_the_old_bios_mode()
    {
        var items = PcReadiness.Evaluate(ReadyPc with { Firmware = FirmwareKind.Bios }, TestDistros.Get("archlinux"));

        Assert.DoesNotContain(items, i => i.Check == ReadinessCheck.SecureBoot);
        Assert.Equal(ReadinessLevel.Info, Single(items, ReadinessCheck.Firmware).Level);
    }

    [Theory]
    [InlineData("Intel RST VMD Controller 9A0B")]
    [InlineData("Intel(R) Chipset SATA/PCIe RST Premium Controller")]
    [InlineData("Intel(R) Desktop/Workstation/Server Express Chipset SATA RAID Controller")]
    public void Intel_rst_controllers_are_reported(string controller)
    {
        var item = Single(PcReadiness.Evaluate(ReadyPc with { StorageControllers = [controller] }, null), ReadinessCheck.StorageMode);

        Assert.Equal(ReadinessLevel.Warning, item.Level);
        Assert.Equal(controller, item.Name);
    }

    [Theory]
    [InlineData("Standard NVM Express Controller")]
    [InlineData("Microsoft Storage Spaces Controller")]
    [InlineData("Intel(R) 600 Series Chipset Family SATA AHCI Controller")]
    [InlineData("First Strike Controller")]
    public void Standard_controllers_are_not_reported(string controller)
    {
        Assert.False(PcReadiness.IsRaidController(controller));
    }

    [Fact]
    public void A_system_disk_on_a_raid_bus_is_reported()
    {
        var item = Single(PcReadiness.Evaluate(ReadyPc with { SystemDiskBus = DiskBusType.Raid, StorageControllers = [] }, null), ReadinessCheck.StorageMode);

        Assert.Equal(ReadinessLevel.Warning, item.Level);
        Assert.Null(item.Name);
    }

    [Fact]
    public void Encryption_and_fast_startup_offer_an_action()
    {
        var items = PcReadiness.Evaluate(ReadyPc with { SystemDriveEncrypted = true, FastStartupEnabled = true }, null);

        Assert.Equal(ReadinessAction.OpenRecoveryKeyPage, Single(items, ReadinessCheck.Encryption).Action);
        Assert.Equal(ReadinessAction.DisableFastStartup, Single(items, ReadinessCheck.FastStartup).Action);
        Assert.Equal(ReadinessLevel.Warning, PcReadiness.Worst(items));
    }

    [Fact]
    public void Disk_space_counts_the_distribution_and_a_margin_for_windows()
    {
        var ubuntu = TestDistros.Get("ubuntu-desktop");
        var needed = (ubuntu.Requirements!.DiskGb!.Value * Gib) + PcReadiness.WindowsMarginBytes;

        var low = Single(PcReadiness.Evaluate(ReadyPc with { SystemDriveFreeBytes = needed - 1 }, ubuntu), ReadinessCheck.DiskSpace);
        var enough = Single(PcReadiness.Evaluate(ReadyPc with { SystemDriveFreeBytes = needed }, ubuntu), ReadinessCheck.DiskSpace);

        Assert.Equal(ReadinessLevel.Warning, low.Level);
        Assert.Equal(needed, low.Needed);
        Assert.Equal(ReadinessAction.OpenStorageSettings, low.Action);
        Assert.Equal(ReadinessLevel.Ok, enough.Level);
    }

    [Fact]
    public void Memory_reported_by_windows_may_be_a_little_lower_than_installed()
    {
        var mint = TestDistros.Get("linuxmint-xfce");
        var needed = mint.Requirements!.RamMb!.Value * 1024L * 1024;

        Assert.Equal(ReadinessLevel.Ok, Single(PcReadiness.Evaluate(ReadyPc with { MemoryBytes = needed - (needed / 10) }, mint), ReadinessCheck.Memory).Level);
        Assert.Equal(ReadinessLevel.Warning, Single(PcReadiness.Evaluate(ReadyPc with { MemoryBytes = needed / 2 }, mint), ReadinessCheck.Memory).Level);
        Assert.Equal(ReadinessLevel.Info, Single(PcReadiness.Evaluate(ReadyPc with { MemoryBytes = 2 * Gib }, null), ReadinessCheck.Memory).Level);
    }

    [Fact]
    public void Nvidia_graphics_and_broadcom_wifi_are_pointed_out()
    {
        var items = PcReadiness.Evaluate(
            ReadyPc with
            {
                GraphicsAdapters = ["Intel(R) UHD Graphics", "NVIDIA GeForce RTX 3050 Laptop GPU"],
                WirelessAdapters = ["Broadcom 802.11ac Network Adapter"],
            },
            null);

        var graphics = Single(items, ReadinessCheck.Graphics);
        Assert.Equal("Nvidia", graphics.Variant);
        Assert.Equal("NVIDIA GeForce RTX 3050 Laptop GPU", graphics.Name);
        Assert.Equal(ReadinessLevel.Warning, Single(items, ReadinessCheck.Wifi).Level);
    }

    private static ReadinessItem Single(IReadOnlyList<ReadinessItem> items, ReadinessCheck check) =>
        Assert.Single(items, i => i.Check == check);
}
