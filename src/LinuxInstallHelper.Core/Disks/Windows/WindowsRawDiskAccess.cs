using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>Opens <c>\\.\PhysicalDriveN</c> for unbuffered raw I/O after locking and dismounting its volumes.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRawDiskAccess : IRawDiskAccess
{
    private readonly ILogger _logger;

    public WindowsRawDiskAccess(ILogger<WindowsRawDiskAccess>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public IBlockDevice Open(DiskInfo disk) => PhysicalDriveDevice.Open(disk, _logger);
}

[SupportedOSPlatform("windows")]
internal sealed unsafe class PhysicalDriveDevice : IBlockDevice
{
    private const int LockAttempts = 20;
    private static readonly TimeSpan LockDelay = TimeSpan.FromMilliseconds(500);

    private readonly SafeFileHandle _disk;
    private readonly List<SafeFileHandle> _volumes;
    private readonly ILogger _logger;

    private PhysicalDriveDevice(SafeFileHandle disk, List<SafeFileHandle> volumes, int sectorSize, long size, ILogger logger)
    {
        _disk = disk;
        _volumes = volumes;
        SectorSize = sectorSize;
        Size = size;
        _logger = logger;
    }

    public int SectorSize { get; }

    public long Size { get; }

    public static PhysicalDriveDevice Open(DiskInfo disk, ILogger logger)
    {
        var volumes = new List<SafeFileHandle>();
        try
        {
            foreach (var path in VolumeDevicePaths(disk))
            {
                volumes.Add(LockAndDismount(path, logger));
            }

            var handle = NativeMethods.CreateFileW(
                disk.DevicePath,
                NativeMethods.GenericRead | NativeMethods.GenericWrite,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                IntPtr.Zero,
                NativeMethods.OpenExisting,
                NativeMethods.FileFlagNoBuffering | NativeMethods.FileFlagWriteThrough,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Windows refused to open {disk.DevicePath}: {LastError()}");
            }

            var geometry = NativeMethods.Ioctl(handle, NativeMethods.IoctlDiskGetDriveGeometryEx, null, 256);
            if (geometry is null)
            {
                handle.Dispose();
                throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Cannot read the geometry of {disk.DevicePath}: {LastError()}");
            }

            // DISK_GEOMETRY_EX: DISK_GEOMETRY (BytesPerSector at offset 20), then LARGE_INTEGER DiskSize at offset 24.
            var sectorSize = BitConverter.ToInt32(geometry, 20);
            var size = BitConverter.ToInt64(geometry, 24);
            if (sectorSize <= 0 || (sectorSize & (sectorSize - 1)) != 0)
            {
                sectorSize = 512;
            }

            // Forget the old partition table so that Windows does not remount anything while writing.
            if (NativeMethods.Ioctl(handle, NativeMethods.IoctlDiskDeleteDriveLayout) is null)
            {
                logger.LogDebug("IOCTL_DISK_DELETE_DRIVE_LAYOUT failed on {Device}: {Error}", disk.DevicePath, LastError());
            }

            logger.LogInformation("Opened {Device}: {Size} bytes, {Sector}-byte sectors, {Volumes} volume(s) locked", disk.DevicePath, size, sectorSize, volumes.Count);
            return new PhysicalDriveDevice(handle, volumes, sectorSize, size, logger);
        }
        catch
        {
            foreach (var volume in volumes)
            {
                volume.Dispose();
            }

            throw;
        }
    }

    public void Write(long offset, ReadOnlySpan<byte> data)
    {
        fixed (byte* pointer = data)
        {
            var done = 0;
            while (done < data.Length)
            {
                var position = offset + done;
                var overlapped = new NativeOverlapped { OffsetLow = (int)(position & 0xFFFFFFFF), OffsetHigh = (int)(position >> 32) };
                if (!NativeMethods.WriteFile(_disk, pointer + done, (uint)(data.Length - done), out var written, &overlapped) || written == 0)
                {
                    throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Write error at byte {position}: {LastError()}. Was the drive unplugged?");
                }

                done += (int)written;
            }
        }
    }

    public void Read(long offset, Span<byte> buffer)
    {
        fixed (byte* pointer = buffer)
        {
            var done = 0;
            while (done < buffer.Length)
            {
                var position = offset + done;
                var overlapped = new NativeOverlapped { OffsetLow = (int)(position & 0xFFFFFFFF), OffsetHigh = (int)(position >> 32) };
                if (!NativeMethods.ReadFile(_disk, pointer + done, (uint)(buffer.Length - done), out var read, &overlapped) || read == 0)
                {
                    throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Read error at byte {position}: {LastError()}. Was the drive unplugged?");
                }

                done += (int)read;
            }
        }
    }

    public void Flush()
    {
        if (!NativeMethods.FlushFileBuffers(_disk))
        {
            throw new UsbWriteException(UsbWriteFailure.DeviceError, $"The drive could not flush its cache: {LastError()}");
        }
    }

    public void Dispose()
    {
        // Ask Windows to read the new partition table written by the image.
        NativeMethods.Ioctl(_disk, NativeMethods.IoctlDiskUpdateProperties);
        _disk.Dispose();

        foreach (var volume in _volumes)
        {
            NativeMethods.Ioctl(volume, NativeMethods.FsctlUnlockVolume);
            volume.Dispose();
        }

        _logger.LogDebug("Raw access released");
    }

    private static IEnumerable<string> VolumeDevicePaths(DiskInfo disk)
    {
        var paths = disk.VolumePaths.Select(p => p.TrimEnd('\\')).ToList();
        if (paths.Count == 0)
        {
            paths.AddRange(disk.DriveLetters.Select(l => $@"\\.\{l.TrimEnd('\\')}"));
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static SafeFileHandle LockAndDismount(string path, ILogger logger)
    {
        var handle = NativeMethods.CreateFileW(
            path,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new UsbWriteException(UsbWriteFailure.VolumeBusy, $"Cannot open the volume {path}: {LastError()}");
        }

        for (var attempt = 1; attempt <= LockAttempts; attempt++)
        {
            if (NativeMethods.Ioctl(handle, NativeMethods.FsctlLockVolume) is not null)
            {
                if (NativeMethods.Ioctl(handle, NativeMethods.FsctlDismountVolume) is null)
                {
                    logger.LogDebug("FSCTL_DISMOUNT_VOLUME failed on {Path}: {Error}", path, LastError());
                }

                return handle;
            }

            Thread.Sleep(LockDelay);
        }

        handle.Dispose();
        throw new UsbWriteException(
            UsbWriteFailure.VolumeBusy,
            $"The volume {path} is in use. Close the windows and programs that use the USB drive, then try again.");
    }

    private static string LastError() => new Win32Exception(Marshal.GetLastWin32Error()).Message;
}
