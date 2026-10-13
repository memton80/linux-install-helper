using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace LinuxInstallHelper.Core.Software.Windows;

/// <summary>Reads the programs listed in "Installed apps" from the <c>Uninstall</c> registry keys (64-bit, 32-bit, per user).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstalledPrograms : IInstalledProgramSource
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Uninstall32Key = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly ILogger _logger;

    public WindowsInstalledPrograms(ILogger<WindowsInstalledPrograms>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public Task<IReadOnlyList<InstalledProgram>> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var entries = new List<UninstallEntry>();
        Read(Registry.LocalMachine, UninstallKey, entries, cancellationToken);
        Read(Registry.LocalMachine, Uninstall32Key, entries, cancellationToken);
        Read(Registry.CurrentUser, UninstallKey, entries, cancellationToken);

        var programs = InstalledPrograms.Clean(entries);
        _logger.LogInformation("Found {Count} installed programs ({Entries} registry entries)", programs.Count, entries.Count);
        return programs;
    }, cancellationToken);

    private void Read(RegistryKey hive, string path, List<UninstallEntry> entries, CancellationToken cancellationToken)
    {
        try
        {
            using var uninstall = hive.OpenSubKey(path);
            if (uninstall is null)
            {
                return;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var key = uninstall.OpenSubKey(name);
                if (key is null)
                {
                    continue;
                }

                entries.Add(new UninstallEntry
                {
                    DisplayName = key.GetValue("DisplayName") as string,
                    Publisher = key.GetValue("Publisher") as string,
                    DisplayVersion = key.GetValue("DisplayVersion") as string,
                    SystemComponent = key.GetValue("SystemComponent") is int component && component != 0,
                    ParentKeyName = key.GetValue("ParentKeyName") as string,
                    ReleaseType = key.GetValue("ReleaseType") as string,
                });
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Could not read {Hive}\\{Path}", hive.Name, path);
        }
    }
}
