using System.Runtime.InteropServices;
using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Readiness;

public enum ProcessorKind
{
    Unknown,

    /// <summary>Intel or AMD, 64 bits: what the distributions of the catalog are made for.</summary>
    X64,

    /// <summary>ARM (Snapdragon…): the x86_64 images do not start on it.</summary>
    Arm64,
}

public enum FirmwareKind
{
    Unknown,
    Uefi,

    /// <summary>The old BIOS mode (Legacy, CSM): no Secure Boot.</summary>
    Bios,
}

/// <summary>
/// What Windows says about this computer, as far as starting and installing Linux is concerned. A null value could not be
/// read: the matching check is left out rather than guessed.
/// </summary>
public sealed record PcFacts
{
    public ProcessorKind Processor { get; init; }

    public FirmwareKind Firmware { get; init; }

    public bool? SecureBootEnabled { get; init; }

    /// <summary>Bus of the disk that holds Windows (<see cref="DiskBusType.Raid"/> when Intel RST/RAID drives it).</summary>
    public DiskBusType? SystemDiskBus { get; init; }

    /// <summary>Names of the storage controllers ("Intel RST VMD Controller"…).</summary>
    public IReadOnlyList<string> StorageControllers { get; init; } = [];

    /// <summary>BitLocker or device encryption protects the Windows drive (fully or partly encrypted).</summary>
    public bool? SystemDriveEncrypted { get; init; }

    /// <summary>Windows fast startup is on: Windows is not fully shut down, which matters when Linux is installed next to it.</summary>
    public bool? FastStartupEnabled { get; init; }

    /// <summary>Free space on the Windows drive.</summary>
    public long? SystemDriveFreeBytes { get; init; }

    public long? MemoryBytes { get; init; }

    public IReadOnlyList<string> GraphicsAdapters { get; init; } = [];

    public IReadOnlyList<string> WirelessAdapters { get; init; } = [];

    /// <summary>Maker of the computer (<c>Win32_ComputerSystem</c>), like "Dell Inc." or "LENOVO".</summary>
    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    /// <summary>Product line, like "ThinkPad X1 Carbon Gen 9" (Lenovo puts a type number in <see cref="Model"/>).</summary>
    public string? Family { get; init; }

    /// <summary>Maker of the motherboard, for computers assembled from parts.</summary>
    public string? BoardManufacturer { get; init; }

    /// <summary>A portable computer (with a battery).</summary>
    public bool? IsLaptop { get; init; }

    /// <summary>The architecture of the running Windows.</summary>
    public static ProcessorKind ProcessorOf(Architecture architecture) => architecture switch
    {
        Architecture.X64 => ProcessorKind.X64,
        Architecture.Arm64 => ProcessorKind.Arm64,
        _ => ProcessorKind.Unknown,
    };

    /// <summary>The facts that need no system query: the processor and the memory seen by .NET.</summary>
    public static PcFacts Basic()
    {
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return new PcFacts
        {
            Processor = ProcessorOf(RuntimeInformation.OSArchitecture),
            MemoryBytes = memory > 0 ? memory : null,
        };
    }
}

public interface IPcProbe
{
    /// <summary>Reads the facts. Never throws for a fact that cannot be read: it stays null.</summary>
    Task<PcFacts> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns Windows fast startup off (the "Turn on fast startup" box of the power options).</summary>
    void DisableFastStartup();
}
