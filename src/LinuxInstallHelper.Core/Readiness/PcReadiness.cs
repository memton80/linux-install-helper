using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Readiness;

/// <summary>The points checked before starting Linux from the drive and installing it, in the order they are shown.</summary>
public enum ReadinessCheck
{
    Processor,
    Firmware,
    SecureBoot,
    StorageMode,
    Encryption,
    FastStartup,
    DiskSpace,
    Memory,
    Graphics,
    Wifi,
}

public enum ReadinessLevel
{
    /// <summary>Nothing to do.</summary>
    Ok,

    /// <summary>Good to know.</summary>
    Info,

    /// <summary>Something to do or to keep in mind before installing.</summary>
    Warning,

    /// <summary>The drive will not start on this computer.</summary>
    Blocker,
}

/// <summary>What the application can do for the user about a point.</summary>
public enum ReadinessAction
{
    None,

    /// <summary>Restart into the UEFI settings (to turn Secure Boot off).</summary>
    OpenFirmwareSettings,

    DisableFastStartup,

    /// <summary>Open the Microsoft page that lists the BitLocker recovery keys of the account.</summary>
    OpenRecoveryKeyPage,

    /// <summary>Open the storage settings of Windows, to free space.</summary>
    OpenStorageSettings,
}

/// <summary>
/// The result of a check. Its texts are <c>Ready_{Check}_{Variant}_Title</c> and <c>_Message</c>, where <c>{0}</c> is
/// <see cref="Name"/>, <c>{1}</c> is <see cref="Amount"/> and <c>{2}</c> is <see cref="Needed"/>, as sizes.
/// </summary>
public sealed record ReadinessItem(ReadinessCheck Check, ReadinessLevel Level, string Variant)
{
    public string? Name { get; init; }

    public long? Amount { get; init; }

    public long? Needed { get; init; }

    public ReadinessAction Action { get; init; }

    /// <summary><c>{Check}_{Variant}</c>, the stem of the text keys.</summary>
    public string Key => $"{Check}_{Variant}";
}

/// <summary>
/// Compares what is known about this computer with what a distribution needs: whether the drive will start, what to change
/// in the firmware and what to prepare before installing Linux next to Windows.
/// </summary>
public static class PcReadiness
{
    private const long Gib = 1024L * 1024 * 1024;
    private const long Mib = 1024L * 1024;

    /// <summary>Space left to Windows when Linux is installed next to it.</summary>
    public const long WindowsMarginBytes = 10 * Gib;

    /// <summary>Disk space a distribution needs when the catalog does not say.</summary>
    public const int DefaultDiskGb = 25;

    /// <summary>Memory recommended when no distribution is chosen.</summary>
    public const int RecommendedRamMb = 4096;

    private static readonly char[] WordSeparators = [' ', '(', ')', '-', '/', ',', '®'];

    // Controllers that put the disk behind Intel RST (RAID, VMD): some Linux installers do not see the disk then.
    private static readonly string[] RaidControllerWords = ["RAID", "VMD", "RST"];

    /// <summary>The checks for this computer, for <paramref name="distro"/> or for Linux in general when it is null.</summary>
    public static IReadOnlyList<ReadinessItem> Evaluate(PcFacts facts, Distro? distro)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var items = new List<ReadinessItem?>
        {
            Processor(facts, distro),
            Firmware(facts),
            SecureBoot(facts, distro),
            StorageMode(facts),
            Encryption(facts),
            FastStartup(facts),
            DiskSpace(facts, distro),
            Memory(facts, distro),
            Graphics(facts),
            Wifi(facts),
        };

        return items.OfType<ReadinessItem>().ToList();
    }

    /// <summary>The worst level among <paramref name="items"/>.</summary>
    public static ReadinessLevel Worst(IEnumerable<ReadinessItem> items) =>
        items.Select(i => i.Level).DefaultIfEmpty(ReadinessLevel.Ok).Max();

    /// <summary>True when the drive made for <paramref name="distro"/> cannot start on a computer with this processor.</summary>
    public static bool CannotStart(ProcessorKind processor, Distro distro)
    {
        ArgumentNullException.ThrowIfNull(distro);
        return (processor, distro.Architecture) switch
        {
            (ProcessorKind.Arm64, DistroArchitectures.X64) => true,
            (ProcessorKind.X64, DistroArchitectures.Arm64) => true,
            _ => false,
        };
    }

    private static ReadinessItem? Processor(PcFacts facts, Distro? distro)
    {
        if (facts.Processor == ProcessorKind.Unknown)
        {
            return null;
        }

        if (distro is not null && CannotStart(facts.Processor, distro))
        {
            return new(ReadinessCheck.Processor, ReadinessLevel.Blocker, facts.Processor == ProcessorKind.Arm64 ? "Arm64" : "NeedsArm") { Name = distro.DisplayName };
        }

        if (facts.Processor == ProcessorKind.Arm64)
        {
            // Without a chosen distribution: the catalog only has images for Intel and AMD processors.
            return distro is null
                ? new(ReadinessCheck.Processor, ReadinessLevel.Blocker, "Arm64")
                : new(ReadinessCheck.Processor, ReadinessLevel.Ok, "Arm64Ok") { Name = distro.DisplayName };
        }

        return new(ReadinessCheck.Processor, ReadinessLevel.Ok, "X64");
    }

    private static ReadinessItem? Firmware(PcFacts facts) => facts.Firmware switch
    {
        FirmwareKind.Uefi => new(ReadinessCheck.Firmware, ReadinessLevel.Ok, "Uefi"),
        FirmwareKind.Bios => new(ReadinessCheck.Firmware, ReadinessLevel.Info, "Bios"),
        _ => null,
    };

    private static ReadinessItem? SecureBoot(PcFacts facts, Distro? distro)
    {
        // The old BIOS mode has no Secure Boot.
        if (facts.Firmware == FirmwareKind.Bios || facts.SecureBootEnabled is not { } enabled)
        {
            return null;
        }

        if (!enabled)
        {
            return new(ReadinessCheck.SecureBoot, ReadinessLevel.Ok, "Off");
        }

        return distro switch
        {
            null => new(ReadinessCheck.SecureBoot, ReadinessLevel.Info, "On"),
            { SecureBoot: true } => new(ReadinessCheck.SecureBoot, ReadinessLevel.Ok, "Supported") { Name = distro.DisplayName },
            _ => new(ReadinessCheck.SecureBoot, ReadinessLevel.Warning, "MustDisable")
            {
                Name = distro.DisplayName,
                Action = facts.Firmware == FirmwareKind.Uefi ? ReadinessAction.OpenFirmwareSettings : ReadinessAction.None,
            },
        };
    }

    private static ReadinessItem? StorageMode(PcFacts facts)
    {
        var controller = facts.StorageControllers.FirstOrDefault(IsRaidController);
        if (facts.SystemDiskBus == DiskBusType.Raid || controller is not null)
        {
            return new(ReadinessCheck.StorageMode, ReadinessLevel.Warning, "Raid") { Name = controller };
        }

        return facts.SystemDiskBus is null ? null : new(ReadinessCheck.StorageMode, ReadinessLevel.Ok, "Standard");
    }

    /// <summary>True for an Intel RST controller in RAID or VMD mode.</summary>
    public static bool IsRaidController(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && RaidControllerWords.Any(word => Words(name).Contains(word, StringComparer.OrdinalIgnoreCase));

    private static ReadinessItem? Encryption(PcFacts facts) => facts.SystemDriveEncrypted switch
    {
        true => new(ReadinessCheck.Encryption, ReadinessLevel.Warning, "On") { Action = ReadinessAction.OpenRecoveryKeyPage },
        false => new(ReadinessCheck.Encryption, ReadinessLevel.Ok, "Off"),
        null => null,
    };

    private static ReadinessItem? FastStartup(PcFacts facts) => facts.FastStartupEnabled switch
    {
        true => new(ReadinessCheck.FastStartup, ReadinessLevel.Warning, "On") { Action = ReadinessAction.DisableFastStartup },
        false => new(ReadinessCheck.FastStartup, ReadinessLevel.Ok, "Off"),
        null => null,
    };

    private static ReadinessItem? DiskSpace(PcFacts facts, Distro? distro)
    {
        if (facts.SystemDriveFreeBytes is not { } free)
        {
            return null;
        }

        var needed = ((distro?.Requirements?.DiskGb ?? DefaultDiskGb) * Gib) + WindowsMarginBytes;
        return free >= needed
            ? new(ReadinessCheck.DiskSpace, ReadinessLevel.Ok, "Enough") { Amount = free, Needed = needed }
            : new(ReadinessCheck.DiskSpace, ReadinessLevel.Warning, "Low") { Amount = free, Needed = needed, Action = ReadinessAction.OpenStorageSettings };
    }

    private static ReadinessItem? Memory(PcFacts facts, Distro? distro)
    {
        if (facts.MemoryBytes is not { } memory)
        {
            return null;
        }

        // Windows reports a little less than the memory installed (part of it is reserved by the hardware).
        var needed = (distro?.Requirements?.RamMb ?? RecommendedRamMb) * Mib;
        if (memory >= needed * 7 / 8)
        {
            return new(ReadinessCheck.Memory, ReadinessLevel.Ok, "Enough") { Amount = memory, Needed = needed };
        }

        return distro is null
            ? new(ReadinessCheck.Memory, ReadinessLevel.Info, "Modest") { Amount = memory, Needed = needed }
            : new(ReadinessCheck.Memory, ReadinessLevel.Warning, "Low") { Amount = memory, Needed = needed, Name = distro.DisplayName };
    }

    private static ReadinessItem? Graphics(PcFacts facts)
    {
        var nvidia = facts.GraphicsAdapters.FirstOrDefault(a => a.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        if (nvidia is not null)
        {
            return new(ReadinessCheck.Graphics, ReadinessLevel.Info, "Nvidia") { Name = nvidia };
        }

        var first = facts.GraphicsAdapters.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        return first is null ? null : new(ReadinessCheck.Graphics, ReadinessLevel.Ok, "Standard") { Name = first };
    }

    private static ReadinessItem? Wifi(PcFacts facts)
    {
        // Broadcom chips often need a driver that is not on the drive: no Wi-Fi until it is installed.
        var broadcom = facts.WirelessAdapters.FirstOrDefault(a => a.Contains("Broadcom", StringComparison.OrdinalIgnoreCase));
        if (broadcom is not null)
        {
            return new(ReadinessCheck.Wifi, ReadinessLevel.Warning, "Broadcom") { Name = broadcom };
        }

        var first = facts.WirelessAdapters.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        return first is null ? null : new(ReadinessCheck.Wifi, ReadinessLevel.Ok, "Standard") { Name = first };
    }

    // "Intel(R) Chipset SATA/PCIe RST Premium Controller" gives Intel, R, Chipset, SATA, PCIe, RST…
    private static string[] Words(string text) => text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
}
