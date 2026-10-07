using LinuxInstallHelper.Core.Download;

namespace LinuxInstallHelper.Core.Images;

/// <summary>An ISO image kept in the download folder, or a download that was interrupted.</summary>
/// <param name="Path">The file.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="LastWrite">When it was last written.</param>
/// <param name="IsPartial">An interrupted download (<c>.part</c>), resumed by the next creation with the same image.</param>
public sealed record StoredImage(string Path, long Size, DateTime LastWrite, bool IsPartial)
{
    /// <summary>The name of the image, without the extension of a partial download.</summary>
    public string Name
    {
        get
        {
            var name = System.IO.Path.GetFileName(Path);
            return IsPartial ? name[..^ResumableDownloader.PartialExtension.Length] : name;
        }
    }
}

/// <summary>Lists and deletes the images of the download folder, to free disk space.</summary>
public static class StoredImages
{
    private const string IsoPattern = "*.iso";

    /// <summary>The images directly in <paramref name="folder"/>, newest first. Nothing when the folder does not exist.</summary>
    public static IReadOnlyList<StoredImage> List(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
        var complete = Directory.EnumerateFiles(folder, IsoPattern, options).Select(path => Describe(path, isPartial: false));
        var partial = Directory.EnumerateFiles(folder, IsoPattern + ResumableDownloader.PartialExtension, options).Select(path => Describe(path, isPartial: true));
        return complete.Concat(partial)
            .OfType<StoredImage>()
            .OrderByDescending(image => image.LastWrite)
            .ToList();
    }

    /// <summary>Deletes the image, and the state file of a partial download.</summary>
    public static void Delete(StoredImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        File.Delete(image.Path);
        if (image.IsPartial)
        {
            File.Delete(image.Path[..^ResumableDownloader.PartialExtension.Length] + ResumableDownloader.StateExtension);
        }
    }

    private static StoredImage? Describe(string path, bool isPartial)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new StoredImage(info.FullName, info.Length, info.LastWriteTimeUtc, isPartial) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
