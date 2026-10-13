using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.Core;

/// <summary>The processor of this computer, named like the images of the catalog (<see cref="DistroArchitectures"/>).</summary>
public static class MachineArchitecture
{
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

    /// <summary>
    /// <see cref="DistroArchitectures.X64"/> or <see cref="DistroArchitectures.Arm64"/>, null for another processor.
    /// </summary>
    public static string? Current { get; } = Detect();

    /// <summary>True when an image for <paramref name="architecture"/> starts on this computer, or when it is unknown.</summary>
    public static bool Boots(string? architecture) =>
        architecture is null || Current is null || string.Equals(architecture, Current, StringComparison.Ordinal);

    /// <summary>The catalog name of a .NET architecture, or null when the catalog has no image for it.</summary>
    public static string? FromArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => DistroArchitectures.X64,
        Architecture.Arm64 => DistroArchitectures.Arm64,
        _ => null,
    };

    /// <summary>The catalog name of a Windows <c>IMAGE_FILE_MACHINE_*</c> value, or null.</summary>
    public static string? FromMachine(ushort machine) => machine switch
    {
        ImageFileMachineAmd64 => DistroArchitectures.X64,
        ImageFileMachineArm64 => DistroArchitectures.Arm64,
        _ => null,
    };

    private static string? Detect()
    {
        // The x64 build also runs on ARM PCs, emulated: Windows gives the real processor, not the one of the process.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299) && NativeMachine() is { } machine)
        {
            return FromMachine(machine);
        }

        return FromArchitecture(RuntimeInformation.OSArchitecture);
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
