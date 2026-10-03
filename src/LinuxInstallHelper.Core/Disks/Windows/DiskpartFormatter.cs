using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>
/// Restores a USB drive to normal use (one exFAT partition) with <c>diskpart</c>, after checking again
/// that the target is still the confirmed, eligible USB drive.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DiskpartFormatter : IDiskFormatter
{
    private readonly IDiskService _disks;
    private readonly IRawDiskAccess _access;
    private readonly ILogger _logger;

    public DiskpartFormatter(IDiskService disks, IRawDiskAccess access, ILogger<DiskpartFormatter>? logger = null)
    {
        _disks = disks;
        _access = access;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Keeps only characters accepted in a volume label (11 characters, FAT compatible).</summary>
    public static string SanitizeLabel(string label)
    {
        var clean = new string((label ?? string.Empty).Where(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-').ToArray()).Trim();
        if (clean.Length > 11)
        {
            clean = clean[..11].Trim();
        }

        return clean.Length == 0 ? "USB" : clean.ToUpperInvariant();
    }

    public async Task FormatAsync(DiskInfo disk, string label, CancellationToken cancellationToken = default)
    {
        var current = (await _disks.GetDisksAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(d => d.Number == disk.Number);
        if (TargetGuard.Compare(disk, current) != TargetCheck.Same)
        {
            throw new UsbWriteException(UsbWriteFailure.TargetChanged, "The USB drive was unplugged or replaced. Nothing was erased.");
        }

        var protectedDisks = await _disks.GetProtectedDisksAsync([], cancellationToken).ConfigureAwait(false);
        var rejection = DiskFilter.Evaluate(current!, protectedDisks);
        if (rejection != DiskRejection.None)
        {
            throw new UsbWriteException(UsbWriteFailure.TargetRejected, $"This drive cannot be erased ({rejection}).");
        }

        // On a drive that holds a Linux image written as is, Windows mounts some of the image's partitions and diskpart's
        // "clean" fails with "access denied". Lock the drive and erase its partition tables first: diskpart then finds an
        // empty drive.
        await Task.Run(
            () =>
            {
                using var device = _access.Open(current!);
                ImageWriteEngine.WipeHead(device);
                ImageWriteEngine.WipeTail(device);
            },
            cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Partition tables of disk {Number} erased", current!.Number);

        var script = Path.Combine(Path.GetTempPath(), $"lih-diskpart-{Guid.NewGuid():N}.txt");
        var commands = new StringBuilder()
            .AppendLine("rescan")
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"select disk {current.Number}")
            .AppendLine("clean")
            .AppendLine("create partition primary")
            .AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"format fs=exfat quick label=\"{SanitizeLabel(label)}\"")
            .AppendLine("assign")
            .AppendLine("exit")
            .ToString();
        await File.WriteAllTextAsync(script, commands, Encoding.ASCII, cancellationToken).ConfigureAwait(false);

        try
        {
            _logger.LogWarning("Formatting disk {Number} \"{Name}\" with diskpart", current.Number, current.FriendlyName);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "diskpart.exe"), $"/s \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // diskpart writes in the OEM code page (850 on a French Windows), not in UTF-8.
                StandardOutputEncoding = OemEncoding(),
                StandardErrorEncoding = OemEncoding(),
            };

            using var process = Process.Start(start) ?? throw new UsbWriteException(UsbWriteFailure.DeviceError, "diskpart could not be started.");
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("diskpart exited with {Code}: {Output}", process.ExitCode, output);

            if (process.ExitCode != 0)
            {
                throw new UsbWriteException(UsbWriteFailure.DeviceError, $"diskpart failed (code {process.ExitCode}).\n{output.Trim()}");
            }
        }
        finally
        {
            File.Delete(script);
        }
    }

    private static Encoding OemEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding((int)NativeMethods.GetOEMCP());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.Default;
        }
    }
}
