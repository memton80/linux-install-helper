using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>Safe removal through the Configuration Manager (same as "Eject" in Explorer).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskEjector : IDiskEjector
{
    private readonly ILogger _logger;

    public WindowsDiskEjector(ILogger<WindowsDiskEjector>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public Task<string?> EjectAsync(DiskInfo disk, CancellationToken cancellationToken = default) =>
        Task.Run(() => Eject(disk, cancellationToken), cancellationToken);

    private string? Eject(DiskInfo disk, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(disk.PnpDeviceId))
        {
            return "The device instance is unknown.";
        }

        if (NativeMethods.CM_Locate_DevNodeW(out var devInst, disk.PnpDeviceId, 0) != NativeMethods.CrSuccess)
        {
            return "The device is no longer present.";
        }

        var nodes = new List<uint> { devInst };
        if (NativeMethods.CM_Get_Parent(out var parent, devInst, 0) == NativeMethods.CrSuccess)
        {
            nodes.Add(parent);
        }

        var lastError = "Unknown error.";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var node in nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var vetoName = new StringBuilder(260);
                var result = NativeMethods.CM_Request_Device_EjectW(node, out var vetoType, vetoName, vetoName.Capacity, 0);
                if (result == NativeMethods.CrSuccess && vetoType == 0)
                {
                    _logger.LogInformation("Disk {Number} ejected", disk.Number);
                    return null;
                }

                lastError = vetoType != 0
                    ? $"Windows refused to eject the drive ({(VetoType)vetoType}: {vetoName})."
                    : $"Windows could not eject the drive (error {result}).";
            }

            Thread.Sleep(700);
        }

        _logger.LogWarning("Could not eject disk {Number}: {Error}", disk.Number, lastError);
        return lastError;
    }

    private enum VetoType
    {
        TypeUnknown = 0,
        LegacyDevice = 1,
        PendingClose = 2,
        WindowsApp = 3,
        WindowsService = 4,
        OutstandingOpen = 5,
        Device = 6,
        Driver = 7,
        IllegalDeviceRequest = 8,
        InsufficientPower = 9,
        NonDisableable = 10,
        LegacyDriver = 11,
        InsufficientRights = 12,
        AlreadyRemoved = 13,
    }
}
