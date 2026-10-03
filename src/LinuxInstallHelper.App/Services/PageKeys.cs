namespace LinuxInstallHelper.App.Services;

/// <summary>Keys of the pages the <see cref="INavigationService"/> can show.</summary>
public static class PageKeys
{
    public const string Distros = "Distros";
    public const string LocalIso = "LocalIso";
    public const string Settings = "Settings";
    public const string About = "About";

    /// <summary>Returns the navigation menu entry that should stay highlighted for a page.</summary>
    public static string MenuKeyFor(string pageKey) => pageKey;
}
