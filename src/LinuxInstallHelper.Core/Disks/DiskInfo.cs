namespace LinuxInstallHelper.Core.Disks;

/// <summary>Bus types reported by Windows (<c>STORAGE_BUS_TYPE</c> / <c>MSFT_Disk.BusType</c>).</summary>
public enum DiskBusType
{
    Unknown = 0,
    Scsi = 1,
    Atapi = 2,
    Ata = 3,
    Ieee1394 = 4,
    Ssa = 5,
    FibreChannel = 6,
    Usb = 7,
    Raid = 8,
    IScsi = 9,
    Sas = 10,
    Sata = 11,
    Sd = 12,
    Mmc = 13,
    Virtual = 14,
    FileBackedVirtual = 15,
    StorageSpaces = 16,
    Nvme = 17,
    Scm = 18,
    Ufs = 19,
}

/// <summary>A physical disk as seen by Windows.</summary>
public sealed record DiskInfo
{
    /// <summary>Disk number: <c>\\.\PhysicalDrive{Number}</c>.</summary>
    public required int Number { get; init; }

    public required string FriendlyName { get; init; }

    public string? SerialNumber { get; init; }

    /// <summary>Stable identifier of the disk (<c>MSFT_Disk.UniqueId</c>).</summary>
    public string? UniqueId { get; init; }

    public required long Size { get; init; }

    public DiskBusType BusType { get; init; }

    /// <summary>The device reports removable media (typical of USB flash drives).</summary>
    public bool IsRemovableMedia { get; init; }

    public bool IsSystem { get; init; }

    public bool IsBoot { get; init; }

    public bool IsOffline { get; init; }

    public bool IsReadOnly { get; init; }

    /// <summary>Drive letters of the volumes on the disk, like <c>E:</c>.</summary>
    public IReadOnlyList<string> DriveLetters { get; init; } = [];

    /// <summary>Volume labels, when known.</summary>
    public IReadOnlyList<string> VolumeLabels { get; init; } = [];

    /// <summary>Volume paths (<c>\\?\Volume{GUID}\</c>) used to lock and dismount the volumes before writing.</summary>
    public IReadOnlyList<string> VolumePaths { get; init; } = [];

    /// <summary>Plug and Play instance id, used to eject the device.</summary>
    public string? PnpDeviceId { get; init; }

    public string DevicePath => $@"\\.\PhysicalDrive{Number}";

    public string DisplayLetters => DriveLetters.Count == 0 ? "—" : string.Join(", ", DriveLetters);
}
