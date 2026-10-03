namespace LinuxInstallHelper.Core.Disks;

public enum DiskRejection
{
    /// <summary>The disk can be used.</summary>
    None,

    /// <summary>Only USB disks are offered: internal disks are never shown.</summary>
    NotUsb,

    /// <summary>Windows reports it as the system disk.</summary>
    SystemDisk,

    /// <summary>Windows reports it as the boot disk.</summary>
    BootDisk,

    /// <summary>It holds Windows, the page file, this application, the download folder or the ISO itself.</summary>
    HostsProtectedData,

    Offline,

    ReadOnly,

    /// <summary>Smaller than any Linux image.</summary>
    TooSmall,

    /// <summary>Larger than a USB flash drive: probably an external hard disk.</summary>
    TooLarge,

    /// <summary>Not large enough for the selected image.</summary>
    TooSmallForImage,
}

/// <summary>
/// Decides which disks may be erased. Defensive by design: a disk must positively prove that it is a
/// non-system USB disk of a plausible size; anything doubtful is rejected.
/// </summary>
public static class DiskFilter
{
    /// <summary>Smallest drive accepted (1 GB nominal).</summary>
    public const long MinimumSize = 900_000_000;

    /// <summary>Largest drive accepted: 256 GB nominal USB flash drives, with margin.</summary>
    public const long MaximumSize = 300_000_000_000;

    public static DiskRejection Evaluate(DiskInfo disk, IReadOnlySet<int> protectedDisks)
    {
        ArgumentNullException.ThrowIfNull(disk);
        ArgumentNullException.ThrowIfNull(protectedDisks);

        if (disk.BusType != DiskBusType.Usb)
        {
            return DiskRejection.NotUsb;
        }

        if (disk.IsSystem)
        {
            return DiskRejection.SystemDisk;
        }

        if (disk.IsBoot)
        {
            return DiskRejection.BootDisk;
        }

        if (protectedDisks.Contains(disk.Number))
        {
            return DiskRejection.HostsProtectedData;
        }

        if (disk.IsOffline)
        {
            return DiskRejection.Offline;
        }

        if (disk.IsReadOnly)
        {
            return DiskRejection.ReadOnly;
        }

        if (disk.Size < MinimumSize)
        {
            return DiskRejection.TooSmall;
        }

        if (disk.Size > MaximumSize)
        {
            return DiskRejection.TooLarge;
        }

        return DiskRejection.None;
    }

    public static DiskRejection EvaluateForImage(DiskInfo disk, long imageSize, IReadOnlySet<int> protectedDisks)
    {
        var rejection = Evaluate(disk, protectedDisks);
        if (rejection != DiskRejection.None)
        {
            return rejection;
        }

        return imageSize > disk.Size ? DiskRejection.TooSmallForImage : DiskRejection.None;
    }

    /// <summary>Disks that may be offered to the user. Internal and system disks never appear.</summary>
    public static IReadOnlyList<DiskInfo> Eligible(IEnumerable<DiskInfo> disks, IReadOnlySet<int> protectedDisks) =>
        disks.Where(d => Evaluate(d, protectedDisks) == DiskRejection.None)
            .OrderBy(d => d.Number)
            .ToList();
}
