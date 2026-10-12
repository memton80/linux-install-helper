namespace LinuxInstallHelper.App.Services;

/// <summary>Keys of the pages the <see cref="INavigationService"/> can show.</summary>
public static class PageKeys
{
    public const string Advisor = "Advisor";
    public const string Distros = "Distros";
    public const string DistroDetails = "DistroDetails";
    public const string LocalIso = "LocalIso";
    public const string Restore = "Restore";
    public const string Guide = "Guide";
    public const string Readiness = "Readiness";
    public const string Software = "Software";
    public const string Troubleshoot = "Troubleshoot";
    public const string Drive = "Drive";
    public const string Progress = "Progress";
    public const string Backup = "Backup";
    public const string Done = "Done";
    public const string Settings = "Settings";
    public const string About = "About";

    /// <summary>Returns the navigation menu entry that should stay highlighted for a page.</summary>
    public static string MenuKeyFor(string pageKey, bool fromLocalIso) => pageKey switch
    {
        DistroDetails => Distros,
        Software => Readiness,
        Drive or Progress or Backup or Done => fromLocalIso ? LocalIso : Distros,
        _ => pageKey,
    };
}
