namespace LinuxInstallHelper.Core.Disks;

public interface IDiskService
{
    /// <summary>Every physical disk, eligible or not (the caller filters with <see cref="DiskFilter"/>).</summary>
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disks that must never be written: the ones holding Windows, the page file, the application and the
    /// given extra paths (download folder, ISO file).
    /// </summary>
    Task<IReadOnlySet<int>> GetProtectedDisksAsync(IEnumerable<string> extraPaths, CancellationToken cancellationToken = default);
}

public interface IDiskEjector
{
    /// <summary>Asks Windows to safely remove the device. Returns null on success, or the reason of the refusal.</summary>
    Task<string?> EjectAsync(DiskInfo disk, CancellationToken cancellationToken = default);
}

public interface IDiskFormatter
{
    /// <summary>Erases the disk and creates a single exFAT partition (to use the drive normally again).</summary>
    Task FormatAsync(DiskInfo disk, string label, CancellationToken cancellationToken = default);
}
