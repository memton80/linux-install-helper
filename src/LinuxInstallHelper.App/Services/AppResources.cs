using Microsoft.Windows.ApplicationModel.Resources;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// Locates the application's PRI file (localized strings used by x:Uid and <see cref="ResourceLocalizer"/>).
/// Windows looks for it next to the .exe, but a single-file build extracts it to a temporary folder.
/// </summary>
public static class AppResources
{
    private static readonly string[] PriNames = ["resources.pri", "LinuxInstallHelper.pri"];

    /// <summary>The PRI extracted from a single-file build, or <c>null</c> when Windows finds it on its own.</summary>
    public static string? ExtractedPriPath { get; } = FindExtractedPri();

    public static ResourceLoader CreateLoader() =>
        ExtractedPriPath is { } path ? new ResourceLoader(path) : new ResourceLoader();

    public static ResourceManager? CreateManager() =>
        ExtractedPriPath is { } path ? new ResourceManager(path) : null;

    private static string? FindExtractedPri()
    {
        var exeFolder = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        var contentFolder = AppContext.BaseDirectory;
        if (string.Equals(Path.TrimEndingDirectorySeparator(exeFolder), Path.TrimEndingDirectorySeparator(contentFolder), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return PriNames.Select(name => Path.Combine(contentFolder, name)).FirstOrDefault(File.Exists);
    }
}
