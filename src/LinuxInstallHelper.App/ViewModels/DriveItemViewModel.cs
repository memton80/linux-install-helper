using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A USB drive in a list.</summary>
public sealed class DriveItemViewModel
{
    public DriveItemViewModel(DiskInfo disk, DisplayFormatter formatter, ILocalizer localizer)
    {
        Disk = disk;
        Name = disk.FriendlyName;
        var parts = new List<string> { formatter.Size(disk.Size) };
        if (disk.DriveLetters.Count > 0)
        {
            parts.Add(disk.DisplayLetters);
        }

        if (disk.VolumeLabels.Count > 0)
        {
            parts.Add(string.Join(", ", disk.VolumeLabels));
        }

        parts.Add(localizer.Format("Drive_DiskNumber", disk.Number));
        Details = string.Join(" · ", parts);
        Size = formatter.Size(disk.Size);
        AutomationName = $"{Name}, {Details}";
    }

    public DiskInfo Disk { get; }

    public string Name { get; }

    public string Details { get; }

    public string Size { get; }

    public string AutomationName { get; }

    /// <summary>Same physical drive (number and identity).</summary>
    public bool IsSameDrive(DiskInfo other) => TargetGuard.Compare(Disk, other) == TargetCheck.Same;
}

/// <summary>Eligible drives and the USB drives that were left out, with the reason.</summary>
public sealed record DriveScan(IReadOnlyList<DiskInfo> Eligible, IReadOnlyList<string> Ignored);

/// <summary>Lists drives the same way for the creation and restore screens.</summary>
public sealed class DriveScanner
{
    private readonly IDiskService _disks;
    private readonly ILocalizer _localizer;
    private readonly DisplayFormatter _formatter;

    public DriveScanner(IDiskService disks, ILocalizer localizer, DisplayFormatter formatter)
    {
        _disks = disks;
        _localizer = localizer;
        _formatter = formatter;
    }

    public async Task<DriveScan> ScanAsync(long imageSize, IEnumerable<string> protectedPaths)
    {
        var disks = await _disks.GetDisksAsync();
        var protectedDisks = await _disks.GetProtectedDisksAsync(protectedPaths);
        var eligible = new List<DiskInfo>();
        var ignored = new List<string>();

        foreach (var disk in disks.OrderBy(d => d.Number))
        {
            var rejection = DiskFilter.EvaluateForImage(disk, imageSize, protectedDisks);
            if (rejection == DiskRejection.None)
            {
                eligible.Add(disk);
            }
            else if (rejection != DiskRejection.NotUsb)
            {
                // Internal disks are never mentioned; USB disks that are left out are explained.
                ignored.Add($"{disk.FriendlyName} ({_formatter.Size(disk.Size)}) : {_localizer.Get("Rejection_" + rejection)}");
            }
        }

        return new DriveScan(eligible, ignored);
    }
}
