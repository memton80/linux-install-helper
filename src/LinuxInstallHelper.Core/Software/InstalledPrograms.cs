using System.Text.RegularExpressions;

namespace LinuxInstallHelper.Core.Software;

/// <summary>A program installed on Windows, as listed in "Installed apps".</summary>
public sealed record InstalledProgram(string Name, string? Publisher, string? Version);

/// <summary>An entry of the <c>Uninstall</c> registry keys, before the components and updates are left out.</summary>
public sealed record UninstallEntry
{
    public string? DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? DisplayVersion { get; init; }

    /// <summary><c>SystemComponent = 1</c>: hidden from "Installed apps".</summary>
    public bool SystemComponent { get; init; }

    /// <summary>Set for the updates of another program.</summary>
    public string? ParentKeyName { get; init; }

    /// <summary>"Update", "Hotfix", "Security Update"…</summary>
    public string? ReleaseType { get; init; }
}

public interface IInstalledProgramSource
{
    Task<IReadOnlyList<InstalledProgram>> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Keeps the programs the user installed and cares about: no runtimes, drivers, updates or components.</summary>
public static partial class InstalledPrograms
{
    /// <summary>The programs of <paramref name="entries"/>, once each, sorted by name.</summary>
    public static IReadOnlyList<InstalledProgram> Clean(IEnumerable<UninstallEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.DisplayName) && !e.SystemComponent && string.IsNullOrEmpty(e.ParentKeyName))
            .Where(e => e.ReleaseType is null || !ReleaseTypes().IsMatch(e.ReleaseType))
            .Select(e => new InstalledProgram(e.DisplayName!.Trim(), Trim(e.Publisher), Trim(e.DisplayVersion)))
            .Where(p => !IsNoise(p.Name))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Runtimes, drivers, SDKs and helpers installed along with other programs: nothing to look for on Linux.</summary>
    public static bool IsNoise(string name) => Noise().IsMatch(name);

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("update|hotfix", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTypes();

    [GeneratedRegex(
        @"\b(redistributable|runtime|driver|drivers|pilote|sdk|webview2|asp\.net core|targeting pack|vc_redist|directx"
            + @"|update for|hotfix|security update|service pack|language pack|module linguistique|bootstrapper|prerequisites|chipset|bonjour"
            + @"|java auto updater|edge update|update health tools|gameinput|pc health check|management engine|realtek audio|amd software|physx|vulkan"
            + @"|openal|app certification kit|kb\d{6,})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Noise();
}
