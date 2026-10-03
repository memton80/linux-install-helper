namespace LinuxInstallHelper.Core.Disks;

public enum TargetCheck
{
    /// <summary>The disk is still the one the user confirmed.</summary>
    Same,

    /// <summary>The disk was unplugged.</summary>
    Disappeared,

    /// <summary>Another disk now has this number (unplugged and replaced).</summary>
    Changed,
}

/// <summary>
/// Makes sure the disk about to be erased is exactly the one the user confirmed: disk numbers can be
/// reused when drives are unplugged and plugged again.
/// </summary>
public static class TargetGuard
{
    public static TargetCheck Compare(DiskInfo confirmed, DiskInfo? current)
    {
        ArgumentNullException.ThrowIfNull(confirmed);

        if (current is null || current.Number != confirmed.Number)
        {
            return TargetCheck.Disappeared;
        }

        if (current.Size != confirmed.Size
            || current.BusType != confirmed.BusType
            || Differs(confirmed.UniqueId, current.UniqueId)
            || Differs(confirmed.SerialNumber, current.SerialNumber)
            || !string.Equals(confirmed.FriendlyName, current.FriendlyName, StringComparison.Ordinal))
        {
            return TargetCheck.Changed;
        }

        return TargetCheck.Same;
    }

    private static bool Differs(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
}
