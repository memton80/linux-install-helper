namespace LinuxInstallHelper.Core.Writing;

/// <summary>Raw sector access to a disk.</summary>
public interface IBlockDevice : IDisposable
{
    /// <summary>Logical sector size in bytes (offsets and lengths must be multiples of it).</summary>
    int SectorSize { get; }

    /// <summary>Capacity in bytes.</summary>
    long Size { get; }

    void Write(long offset, ReadOnlySpan<byte> data);

    void Read(long offset, Span<byte> buffer);

    void Flush();
}

/// <summary>Opens a disk for exclusive raw access (volumes locked and dismounted first).</summary>
public interface IRawDiskAccess
{
    IBlockDevice Open(Disks.DiskInfo disk);
}
