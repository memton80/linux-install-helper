using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;

namespace LinuxInstallHelper.App.Services;

/// <summary>Opening folders, web pages and Windows settings with the shell, restarting the computer, copying text.</summary>
public static class SystemActions
{
    /// <summary>Restarts now into the Windows startup options, where "Use a device" starts the computer from the USB drive.</summary>
    public const string StartupOptions = "/o";

    /// <summary>Restarts now into the UEFI settings of the computer.</summary>
    public const string FirmwareSettings = "/fw";

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

    /// <summary>Opens a page of the Windows Settings app, like <c>storagesense</c>.</summary>
    public static void OpenWindowsSettings(string page)
    {
        if (page.All(c => char.IsAsciiLetterLower(c) || c == '-'))
        {
            Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// Restarts the computer right away with <c>shutdown /r</c> and <paramref name="destination"/> (<see cref="StartupOptions"/>
    /// or <see cref="FirmwareSettings"/>). False when Windows refused, for instance on a computer without UEFI.
    /// </summary>
    public static async Task<bool> RestartAsync(string destination)
    {
        var shutdown = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "/r", destination, "/t", "0" })
        {
            shutdown.ArgumentList.Add(argument);
        }

        using var process = Process.Start(shutdown);
        if (process is null)
        {
            return false;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return process.ExitCode == 0;
    }

    public static void CopyText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
