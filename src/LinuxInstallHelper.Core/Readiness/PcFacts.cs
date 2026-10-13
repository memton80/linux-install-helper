using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Readiness;

public enum ProcessorKind
{
    Unknown,

    /// <summary>Intel or AMD, 64 bits: what most distributions of the catalog are made for.</summary>
    X64,

    /// <summary>ARM (Snapdragon…): only the aarch64 images of the catalog start on it.</summary>
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

    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

    /// <summary>
    /// The processor of this computer. The x64 build also runs on ARM PCs, emulated: Windows gives the real processor,
    /// not the one of the process.
    /// </summary>
    public static ProcessorKind ThisProcessor { get; } = DetectProcessor();

    /// <summary>The architecture of the running Windows.</summary>
    public static ProcessorKind ProcessorOf(Architecture architecture) => architecture switch
    {
        Architecture.X64 => ProcessorKind.X64,
        Architecture.Arm64 => ProcessorKind.Arm64,
        _ => ProcessorKind.Unknown,
    };

    /// <summary>The processor of a Windows <c>IMAGE_FILE_MACHINE_*</c> value.</summary>
    public static ProcessorKind ProcessorOfMachine(ushort machine) => machine switch
    {
        ImageFileMachineAmd64 => ProcessorKind.X64,
        ImageFileMachineArm64 => ProcessorKind.Arm64,
        _ => ProcessorKind.Unknown,
    };

    /// <summary>The architecture of the catalog images that start on <paramref name="processor"/>, null when unknown.</summary>
    public static string? ArchitectureOf(ProcessorKind processor) => processor switch
    {
        ProcessorKind.X64 => DistroArchitectures.X64,
        ProcessorKind.Arm64 => DistroArchitectures.Arm64,
        _ => null,
    };

    /// <summary>The facts that need no system query: the processor and the memory seen by .NET.</summary>
    public static PcFacts Basic()
    {
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return new PcFacts
        {
            Processor = ThisProcessor,
            MemoryBytes = memory > 0 ? memory : null,
        };
    }

    private static ProcessorKind DetectProcessor()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299) && NativeMachine() is { } machine)
        {
            return ProcessorOfMachine(machine);
        }

        return ProcessorOf(RuntimeInformation.OSArchitecture);
    }

    [SupportedOSPlatform("windows10.0.16299")]
    private static ushort? NativeMachine()
    {
        try
        {
            return IsWow64Process2(GetCurrentProcess(), out _, out var native) ? native : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
}

public interface IPcProbe
{
    /// <summary>Reads the facts. Never throws for a fact that cannot be read: it stays null.</summary>
    Task<PcFacts> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns Windows fast startup off (the "Turn on fast startup" box of the power options).</summary>
    void DisableFastStartup();
}
