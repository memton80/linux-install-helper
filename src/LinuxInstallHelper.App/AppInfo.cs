using System.Reflection;

namespace LinuxInstallHelper.App;

/// <summary>Static information about the running build.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public const string RepositoryUrl = "https://github.com/memton80/linux-install-helper";
}
