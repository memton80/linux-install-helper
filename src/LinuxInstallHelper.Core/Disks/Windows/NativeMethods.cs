using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LinuxInstallHelper.Core.Disks.Windows;

[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x00000001;
    public const uint FileShareWrite = 0x00000002;
    public const uint OpenExisting = 3;
    public const uint FileFlagNoBuffering = 0x20000000;
    public const uint FileFlagWriteThrough = 0x80000000;

    public const uint FsctlLockVolume = 0x00090018;
    public const uint FsctlUnlockVolume = 0x0009001C;
    public const uint FsctlDismountVolume = 0x00090020;
    public const uint FsctlAllowExtendedDasdIo = 0x00090083;
    public const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    public const uint IoctlDiskIsWritable = 0x00070024;
    public const uint IoctlDiskUpdateProperties = 0x00070140;
    public const uint IoctlStorageQueryProperty = 0x002D1400;
    public const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;

    public const int ErrorAccessDenied = 5;
    public const int ErrorWriteProtect = 19;

    public const int CrSuccess = 0;

    public static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool WriteFile(SafeFileHandle file, byte* buffer, uint numberOfBytesToWrite, out uint numberOfBytesWritten, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool ReadFile(SafeFileHandle file, byte* buffer, uint numberOfBytesToRead, out uint numberOfBytesRead, NativeOverlapped* overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FlushFileBuffers(SafeFileHandle file);

    [DllImport("ntdll.dll")]
    public static extern int RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNameW(string fileName, [Out] char[] volumePathName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, [Out] char[] volumeName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstVolumeW([Out] char[] volumeName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextVolumeW(IntPtr findVolume, [Out] char[] volumeName, uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindVolumeClose(IntPtr findVolume);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Request_Device_EjectW(uint devInst, out int vetoType, StringBuilder? vetoName, int nameLength, uint flags);

    /// <summary>Opens a device for queries only (no read/write rights needed).</summary>
    public static SafeFileHandle OpenForQuery(string path) =>
        CreateFileW(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    /// <summary>Runs an IOCTL with a buffer of <paramref name="outSize"/> bytes and returns it.</summary>
    public static byte[]? Ioctl(SafeFileHandle device, uint code, byte[]? input = null, int outSize = 0)
    {
        var inHandle = input is null ? default : GCHandle.Alloc(input, GCHandleType.Pinned);
        var output = new byte[outSize];
        var outHandle = GCHandle.Alloc(output, GCHandleType.Pinned);
        try
        {
            var ok = DeviceIoControl(
                device,
                code,
                input is null ? IntPtr.Zero : inHandle.AddrOfPinnedObject(),
                (uint)(input?.Length ?? 0),
                outSize == 0 ? IntPtr.Zero : outHandle.AddrOfPinnedObject(),
                (uint)outSize,
                out _,
                IntPtr.Zero);
            return ok ? output : null;
        }
        finally
        {
            if (input is not null)
            {
                inHandle.Free();
            }

            outHandle.Free();
        }
    }
}
