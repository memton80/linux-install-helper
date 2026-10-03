using System.Runtime.Versioning;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>Finds the volumes that Windows mounted on a disk, including the ones without a drive letter or a partition.</summary>
[SupportedOSPlatform("windows")]
internal static class Volumes
{
    /// <summary>
    /// Device paths (<c>\\?\Volume{GUID}</c>, no trailing backslash) of every volume stored on disk <paramref name="diskNumber"/>.
    /// This also returns the "superfloppy" volume that Windows mounts over a whole removable drive without a partition table.
    /// </summary>
    public static IReadOnlyList<string> OnDisk(int diskNumber) =>
        All().Where(volume => DiskNumbers(volume).Contains(diskNumber)).ToList();

    /// <summary>Disk numbers backing the volume <paramref name="device"/> (<c>\\.\E:</c> or <c>\\?\Volume{GUID}</c>).</summary>
    public static IReadOnlyList<int> DiskNumbers(string device)
    {
        using var handle = NativeMethods.OpenForQuery(device);
        if (handle.IsInvalid)
        {
            return [];
        }

        var extents = NativeMethods.Ioctl(handle, NativeMethods.IoctlVolumeGetVolumeDiskExtents, null, 4096);
        if (extents is null)
        {
            return [];
        }

        // VOLUME_DISK_EXTENTS: DWORD count, padding, then DISK_EXTENT { DWORD DiskNumber; LARGE_INTEGER Start; LARGE_INTEGER Length } (24 bytes each).
        var count = BitConverter.ToInt32(extents, 0);
        return Enumerable.Range(0, Math.Min(count, (extents.Length - 8) / 24))
            .Select(i => BitConverter.ToInt32(extents, 8 + (i * 24)))
            .ToList();
    }

    /// <summary>GUID device path of the volume mounted at <paramref name="mountPoint"/> (<c>E:\</c>), or <c>null</c>.</summary>
    public static string? NameOf(string mountPoint)
    {
        if (!mountPoint.EndsWith('\\'))
        {
            mountPoint += '\\';
        }

        var name = new char[1024];
        return NativeMethods.GetVolumeNameForVolumeMountPointW(mountPoint, name, (uint)name.Length)
            ? Trim(new string(name))
            : null;
    }

    private static List<string> All()
    {
        var result = new List<string>();
        var name = new char[1024];
        var find = NativeMethods.FindFirstVolumeW(name, (uint)name.Length);
        if (find == NativeMethods.InvalidHandleValue)
        {
            return result;
        }

        try
        {
            do
            {
                result.Add(Trim(new string(name)));
                Array.Clear(name);
            }
            while (NativeMethods.FindNextVolumeW(find, name, (uint)name.Length));
        }
        finally
        {
            NativeMethods.FindVolumeClose(find);
        }

        return result;
    }

    // CreateFile opens the volume itself only without the trailing backslash (with it, the root directory).
    private static string Trim(string name) => name.TrimEnd('\0').TrimEnd('\\');
}
