using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LinuxInstallHelper.Core.Backup;

/// <summary>Folders of the user's profile that usually hold personal files.</summary>
public enum PersonalFolderKind
{
    Documents,
    Pictures,
    Desktop,
    Music,
    Videos,
    Downloads,
}

public sealed record PersonalFolder(PersonalFolderKind Kind, string Path);

/// <summary>What a folder holds: the files to back up and their total size.</summary>
public sealed record FolderSize(long Bytes, int Files);

/// <summary>
/// Finds the personal folders (Documents, Pictures, Desktop…) that hold files, to remind the user to back them up
/// before installing Linux.
/// </summary>
public static class PersonalFolders
{
    // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS: a placeholder of a file kept online by OneDrive and similar services.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private const FileAttributes SkippedAttributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | RecallOnDataAccess;

    // FOLDERID_Downloads: the Downloads folder has no Environment.SpecialFolder value.
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    // Put there by Windows or by installers rather than by the user.
    private static readonly string[] IgnoredExtensions = [".lnk", ".url"];
    private static readonly string[] IgnoredNames = ["desktop.ini", "thumbs.db"];

    /// <summary>The personal folders of the current user that exist on this computer.</summary>
    public static IReadOnlyList<PersonalFolder> ForCurrentUser()
    {
        PersonalFolder[] folders =
        [
            new(PersonalFolderKind.Documents, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            new(PersonalFolderKind.Pictures, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            new(PersonalFolderKind.Desktop, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            new(PersonalFolderKind.Music, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            new(PersonalFolderKind.Videos, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            new(PersonalFolderKind.Downloads, DownloadsFolder()),
        ];

        return folders.Where(folder => !string.IsNullOrEmpty(folder.Path) && Directory.Exists(folder.Path)).ToList();
    }

    /// <summary>
    /// Returns the folders that hold at least one file of the user, in the given order. Hidden and system files,
    /// shortcuts and <paramref name="ignoredFiles"/> (the image that was just written) do not count.
    /// </summary>
    public static IReadOnlyList<PersonalFolder> WithFiles(
        IEnumerable<PersonalFolder> folders,
        IEnumerable<string>? ignoredFiles = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var ignored = new HashSet<string>((ignoredFiles ?? []).Select(Path.GetFullPath), PathComparer);
        var seen = new HashSet<string>(PathComparer);
        var result = new List<PersonalFolder>();
        foreach (var folder in folders)
        {
            if (string.IsNullOrEmpty(folder.Path) || !seen.Add(Path.GetFullPath(folder.Path)))
            {
                continue;
            }

            if (HasFiles(folder.Path, ignored, cancellationToken))
            {
                result.Add(folder);
            }
        }

        return result;
    }

    /// <summary>
    /// The files of <paramref name="folder"/> and its subfolders that need a backup, with their size. Like
    /// <see cref="WithFiles"/>, hidden and system files, shortcuts and <paramref name="ignoredFiles"/> do not count, nor do
    /// files kept only online (OneDrive): they are not on this computer.
    /// </summary>
    public static FolderSize Measure(string folder, IEnumerable<string>? ignoredFiles = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        var ignored = new HashSet<string>((ignoredFiles ?? []).Select(Path.GetFullPath), PathComparer);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = SkippedAttributes };
        long bytes = 0;
        var files = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsIgnored(file.FullName, ignored))
                {
                    bytes += file.Length;
                    files++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // What could be read is still a useful estimate.
        }

        return new FolderSize(bytes, files);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool HasFiles(string folder, HashSet<string> ignored, CancellationToken cancellationToken)
    {
        // The default options skip hidden and system entries, such as desktop.ini or the old "My Music" links.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsIgnored(file, ignored))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read cannot be shown to the user either.
        }

        return false;
    }

    private static bool IsIgnored(string file, HashSet<string> ignored)
    {
        var name = Path.GetFileName(file);
        return IgnoredNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || IgnoredExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            || ignored.Contains(Path.GetFullPath(file));
    }

    private static string DownloadsFolder()
    {
        if (OperatingSystem.IsWindows() && KnownFolderPath(DownloadsFolderId) is { } path)
        {
            return path;
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? string.Empty : Path.Combine(profile, "Downloads");
    }

    [SupportedOSPlatform("windows")]
    private static string? KnownFolderPath(Guid folderId)
    {
        var result = SHGetKnownFolderPath(folderId, 0, IntPtr.Zero, out var buffer);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            // Allocated by the shell even when the call fails.
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
