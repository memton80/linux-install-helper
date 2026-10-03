using System.Diagnostics;

namespace LinuxInstallHelper.App.Services;

/// <summary>Opening folders and web pages with the shell.</summary>
public static class SystemActions
{
    public static void OpenFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    public static void ShowInExplorer(string file)
    {
        if (File.Exists(file))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        else
        {
            OpenFolder(Path.GetDirectoryName(file) ?? string.Empty);
        }
    }

    public static void OpenUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
    }
}
