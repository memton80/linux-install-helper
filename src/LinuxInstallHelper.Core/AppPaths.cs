namespace LinuxInstallHelper.Core;

/// <summary>
/// Well-known locations used by the application. Everything lives under
/// <c>%LOCALAPPDATA%\LinuxInstallHelper</c> so that the app never writes next to its executable.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "LinuxInstallHelper";

    public AppPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create))
    {
    }

    public AppPaths(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);
        Root = Path.Combine(localAppDataRoot, AppFolderName);
    }

    /// <summary>Root data folder (<c>%LOCALAPPDATA%\LinuxInstallHelper</c>).</summary>
    public string Root { get; }

    /// <summary>Rolling log files.</summary>
    public string Logs => Path.Combine(Root, "logs");

    /// <summary>Last successfully downloaded and validated catalog.</summary>
    public string CatalogCache => Path.Combine(Root, "catalog");

    /// <summary>Default folder for downloaded ISO images.</summary>
    public string DefaultDownloads => Path.Combine(Root, "downloads");

    /// <summary>User settings file.</summary>
    public string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>Creates every folder the application needs.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(CatalogCache);
        Directory.CreateDirectory(DefaultDownloads);
    }
}
