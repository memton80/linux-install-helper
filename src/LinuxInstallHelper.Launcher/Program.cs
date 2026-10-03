using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace LinuxInstallHelper.Launcher;

/// <summary>Extracts the application carried by this .exe (once per version), then starts it and waits for it.</summary>
internal static partial class Program
{
    private const string Payload = "app.zip";
    private const string AppExe = "LinuxInstallHelper.exe";
    private const string CompleteMarker = ".complete";
    private const int ErrorCancelled = 1223;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var folder = Extract();
            var start = new ProcessStartInfo(Path.Combine(folder, AppExe))
            {
                // The application's manifest asks for administrator rights: only the shell shows the UAC prompt.
                UseShellExecute = true,
                WorkingDirectory = folder,
                Arguments = string.Join(' ', args.Select(Quote)),
            };

            using var process = Process.Start(start);
            if (process is null)
            {
                return 1;
            }

            process.WaitForExit();
            try
            {
                return process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            // The user declined the UAC prompt.
            return ErrorCancelled;
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero, $"Linux Install Helper could not start.\n\n{ex.Message}", "Linux Install Helper", 0x10);
            return 1;
        }
    }

    /// <summary>Folder holding this version of the application, extracted on first use.</summary>
    private static string Extract()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var id = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "PayloadId")?.Value;
        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException("This build does not contain the application.");
        }

        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinuxInstallHelper", "app");
        var target = Path.Combine(root, id);
        if (!File.Exists(Path.Combine(target, CompleteMarker)))
        {
            using var payload = assembly.GetManifestResourceStream(Payload)
                ?? throw new InvalidOperationException("This build does not contain the application.");
            Directory.CreateDirectory(root);

            // Extract next to the target, then move it in place: an interrupted extraction is never used.
            var temp = $"{target}.tmp-{Guid.NewGuid():N}";
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                zip.ExtractToDirectory(temp);
            }

            File.WriteAllText(Path.Combine(temp, CompleteMarker), id);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(temp, target);
        }

        RemoveOtherVersions(root, target);
        return target;
    }

    private static void RemoveOtherVersions(string root, string current)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(folder, current, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still running, or locked: it goes next time.
            }
        }
    }

    /// <summary>Quotes an argument the way CommandLineToArgvW reads it back.</summary>
    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return argument;
        }

        var quoted = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
