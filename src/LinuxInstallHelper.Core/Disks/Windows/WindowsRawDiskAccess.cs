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
    private const int WriteAttempts = 4;
    private static readonly TimeSpan LockDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly DiskInfo _info;
    private readonly SafeFileHandle _disk;
    private readonly bool _diskLocked;
    private readonly Dictionary<string, SafeFileHandle> _volumes;
    private readonly ILogger _logger;

    private PhysicalDriveDevice(DiskInfo info, SafeFileHandle disk, bool diskLocked, Dictionary<string, SafeFileHandle> volumes, int sectorSize, long size, ILogger logger)
    {
        _info = info;
        _disk = disk;
        _diskLocked = diskLocked;
        _volumes = volumes;
        SectorSize = sectorSize;
        Size = size;
        _logger = logger;
    }

    public int SectorSize { get; }

    public long Size { get; }

    public static PhysicalDriveDevice Open(DiskInfo disk, ILogger logger)
    {
        var volumes = new Dictionary<string, SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
        SafeFileHandle? handle = null;
        try
        {
            // Windows silently drops raw writes that land on a mounted volume, so every volume of the drive is locked first.
            LockVolumes(disk, volumes, logger);

            handle = NativeMethods.CreateFileW(
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

            // Like Rufus: lift the I/O boundary checks and lock the drive itself; neither is supported everywhere, so failures are ignored.
            if (NativeMethods.Ioctl(handle, NativeMethods.FsctlAllowExtendedDasdIo) is null)
            {
                logger.LogDebug("FSCTL_ALLOW_EXTENDED_DASD_IO failed on {Device}: {Error}", disk.DevicePath, LastError());
            }

            var diskLocked = NativeMethods.Ioctl(handle, NativeMethods.FsctlLockVolume) is not null;
            if (!diskLocked)
            {
                logger.LogDebug("FSCTL_LOCK_VOLUME failed on {Device}: {Error}", disk.DevicePath, LastError());
            }

            if (NativeMethods.Ioctl(handle, NativeMethods.IoctlDiskIsWritable) is null && Marshal.GetLastWin32Error() == NativeMethods.ErrorWriteProtect)
            {
                throw WriteProtected();
            }

            var geometry = NativeMethods.Ioctl(handle, NativeMethods.IoctlDiskGetDriveGeometryEx, null, 256)
                ?? throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Cannot read the geometry of {disk.DevicePath}: {LastError()}");

            // DISK_GEOMETRY_EX: DISK_GEOMETRY (BytesPerSector at offset 20), then LARGE_INTEGER DiskSize at offset 24.
            var sectorSize = BitConverter.ToInt32(geometry, 20);
            var size = BitConverter.ToInt64(geometry, 24);
            if (sectorSize <= 0 || (sectorSize & (sectorSize - 1)) != 0)
            {
                sectorSize = 512;
            }

            logger.LogInformation("Opened {Device}: {Size} bytes, {Sector}-byte sectors, drive locked: {DiskLocked}", disk.DevicePath, size, sectorSize, diskLocked);
            return new PhysicalDriveDevice(disk, handle, diskLocked, volumes, sectorSize, size, logger);
        }
        catch
        {
            handle?.Dispose();
            foreach (var volume in volumes.Values)
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
            var attempt = 1;
            while (done < data.Length)
            {
                var position = offset + done;
                var overlapped = new NativeOverlapped { OffsetLow = (int)(position & 0xFFFFFFFF), OffsetHigh = (int)(position >> 32) };
                var ok = NativeMethods.WriteFile(_disk, pointer + done, (uint)(data.Length - done), out var written, &overlapped);
                var error = Marshal.GetLastWin32Error();
                if (ok && written > 0)
                {
                    done += (int)written;
                    attempt = 1;
                    continue;
                }

                if (!ok && error == NativeMethods.ErrorWriteProtect)
                {
                    throw WriteProtected();
                }

                // A write that "succeeds" with 0 bytes, or is denied, hits a volume that Windows mounted in the meantime
                // (for instance the RAW volume it creates over a drive without a partition table).
                var reason = ok ? "0 bytes written" : Describe(error);
                if (ok || error == NativeMethods.ErrorAccessDenied)
                {
                    if (attempt >= WriteAttempts)
                    {
                        throw new UsbWriteException(
                            UsbWriteFailure.DeviceError,
                            $"Windows blocked the write at byte {position} ({reason}): a volume of the USB drive is still mounted. Unplug the drive, plug it back in and try again.");
                    }

                    _logger.LogWarning("Write at byte {Position} blocked ({Reason}), locking the volumes of the drive again (attempt {Attempt}/{Max})", position, reason, attempt, WriteAttempts);
                    attempt++;
                    Thread.Sleep(RetryDelay);
                    RelockVolumes();
                    continue;
                }

                throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Write error at byte {position}: {reason}. Was the drive unplugged?");
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
                var ok = NativeMethods.ReadFile(_disk, pointer + done, (uint)(buffer.Length - done), out var read, &overlapped);
                var error = Marshal.GetLastWin32Error();
                if (!ok || read == 0)
                {
                    var reason = ok ? "0 bytes read" : Describe(error);
                    throw new UsbWriteException(UsbWriteFailure.DeviceError, $"Read error at byte {position}: {reason}. Was the drive unplugged?");
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
        if (_diskLocked)
        {
            NativeMethods.Ioctl(_disk, NativeMethods.FsctlUnlockVolume);
        }

        _disk.Dispose();
        ReleaseVolumes();
        _logger.LogDebug("Raw access released");
    }

    private void ReleaseVolumes()
    {
        foreach (var volume in _volumes.Values)
        {
            NativeMethods.Ioctl(volume, NativeMethods.FsctlUnlockVolume);
            volume.Dispose();
        }

        _volumes.Clear();
    }

    /// <summary>
    /// Locks again every volume currently on the drive. Windows may have replaced a volume since it was locked, even under the
    /// same GUID path, leaving a handle on a device that no longer exists: all the handles are released first.
    /// </summary>
    private void RelockVolumes()
    {
        ReleaseVolumes();
        try
        {
            LockVolumes(_info, _volumes, _logger);
        }
        catch (UsbWriteException ex) when (ex.Failure == UsbWriteFailure.VolumeBusy)
        {
            // The drive is already partly written: this is no longer a "nothing was written" failure.
            throw new UsbWriteException(UsbWriteFailure.DeviceError, ex.Message, ex);
        }
    }

    private static void LockVolumes(DiskInfo disk, Dictionary<string, SafeFileHandle> locked, ILogger logger)
    {
        foreach (var path in VolumeDevicePaths(disk))
        {
            locked[path] = LockAndDismount(path, logger);
        }

        logger.LogInformation("{Count} volume(s) of disk {Number} locked and dismounted {Volumes}", locked.Count, disk.Number, locked.Keys);
    }

    /// <summary>
    /// Every volume stored on the drive, by GUID path so that a volume is never opened (and locked) twice under two names.
    /// A volume is only ever locked after Windows confirmed that it lies on this drive.
    /// </summary>
    private static List<string> VolumeDevicePaths(DiskInfo disk)
    {
        var known = disk.VolumePaths.Select(p => p.TrimEnd('\\'))
            .Concat(disk.DriveLetters.Select(Volumes.NameOf).OfType<string>());
        return Volumes.OnDisk(disk.Number)
            .Concat(known.Where(path => Volumes.DiskNumbers(path).Contains(disk.Number)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

                logger.LogDebug("Locked and dismounted {Path}", path);
                return handle;
            }

            Thread.Sleep(LockDelay);
        }

        handle.Dispose();
        throw new UsbWriteException(
            UsbWriteFailure.VolumeBusy,
            $"The volume {path} is in use. Close the windows and programs that use the USB drive, then try again.");
    }

    private static UsbWriteException WriteProtected() =>
        new(UsbWriteFailure.DeviceError, "The USB drive is write-protected. Turn off its protection switch (on the drive or the card adapter) and try again.");

    private static string Describe(int error) => $"{new Win32Exception(error).Message} (error {error})";

    private static string LastError() => Describe(Marshal.GetLastWin32Error());
}
