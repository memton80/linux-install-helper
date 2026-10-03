using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>Lists physical disks through the Windows Storage Management API (WMI) and IOCTLs.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDiskService : IDiskService
{
    private const string StorageNamespace = @"\\.\root\Microsoft\Windows\Storage";
    private readonly ILogger _logger;

    public WindowsDiskService(ILogger<WindowsDiskService>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DiskInfo>>(() => GetDisks(), cancellationToken);

    public Task<IReadOnlySet<int>> GetProtectedDisksAsync(IEnumerable<string> extraPaths, CancellationToken cancellationToken = default)
    {
        var paths = extraPaths.ToList();
        return Task.Run<IReadOnlySet<int>>(() => GetProtectedDisks(paths), cancellationToken);
    }

    private List<DiskInfo> GetDisks()
    {
        var scope = new ManagementScope(StorageNamespace);
        scope.Connect();

        var letters = new Dictionary<int, List<string>>();
        var volumePaths = new Dictionary<int, List<string>>();
        using var partitions = new DisposableList(Query(scope, "SELECT DiskNumber, DriveLetter, AccessPaths FROM MSFT_Partition"));
        foreach (var partition in partitions.Items)
        {
            var disk = ToInt(partition["DiskNumber"]);
            var letter = ToLetter(partition["DriveLetter"]);
            if (letter is not null)
            {
                Add(letters, disk, letter + ":");
            }

            if (partition["AccessPaths"] is string[] accessPaths)
            {
                foreach (var path in accessPaths.Where(p => p.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)))
                {
                    Add(volumePaths, disk, path);
                }
            }
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var volumes = new DisposableList(Query(scope, "SELECT DriveLetter, FileSystemLabel FROM MSFT_Volume"));
        foreach (var volume in volumes.Items)
        {
            var letter = ToLetter(volume["DriveLetter"]);
            if (letter is not null && volume["FileSystemLabel"] is string label && label.Length > 0)
            {
                labels[letter + ":"] = label;
            }
        }

        var pnpIds = GetPnpDeviceIds();
        var disks = new List<DiskInfo>();
        using var physicalDisks = new DisposableList(Query(scope, "SELECT Number, FriendlyName, SerialNumber, UniqueId, Size, BusType, IsSystem, IsBoot, IsOffline, IsReadOnly FROM MSFT_Disk"));
        foreach (var disk in physicalDisks.Items)
        {
            if (disk["Number"] is null)
            {
                continue;
            }

            var number = ToInt(disk["Number"]);
            var (removable, ioctlBus) = QueryStorageDescriptor(number);
            var wmiBus = (DiskBusType)ToInt(disk["BusType"]);
            var diskLetters = letters.GetValueOrDefault(number) ?? [];

            disks.Add(new DiskInfo
            {
                Number = number,
                FriendlyName = (disk["FriendlyName"] as string)?.Trim() ?? $"Disk {number}",
                SerialNumber = (disk["SerialNumber"] as string)?.Trim(),
                UniqueId = disk["UniqueId"] as string,
                Size = Convert.ToInt64(disk["Size"] ?? 0L, System.Globalization.CultureInfo.InvariantCulture),
                // Both sources must agree that the disk is on the USB bus.
                BusType = ioctlBus is null || ioctlBus == wmiBus ? wmiBus : DiskBusType.Unknown,
                IsRemovableMedia = removable,
                IsSystem = disk["IsSystem"] as bool? ?? true,
                IsBoot = disk["IsBoot"] as bool? ?? true,
                IsOffline = disk["IsOffline"] as bool? ?? false,
                IsReadOnly = disk["IsReadOnly"] as bool? ?? false,
                DriveLetters = diskLetters,
                VolumeLabels = diskLetters.Select(l => labels.GetValueOrDefault(l)).OfType<string>().ToList(),
                VolumePaths = volumePaths.GetValueOrDefault(number) ?? [],
                PnpDeviceId = pnpIds.GetValueOrDefault(number),
            });
        }

        _logger.LogDebug("Found {Count} disks: {Disks}", disks.Count, string.Join("; ", disks.Select(d => $"#{d.Number} {d.FriendlyName} {d.BusType} {d.Size}")));
        return disks;
    }

    private static Dictionary<int, string> GetPnpDeviceIds()
    {
        var result = new Dictionary<int, string>();
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Index, PNPDeviceID FROM Win32_DiskDrive");
        foreach (var drive in searcher.Get().Cast<ManagementObject>())
        {
            using (drive)
            {
                if (drive["Index"] is not null && drive["PNPDeviceID"] is string id)
                {
                    result[ToInt(drive["Index"])] = id;
                }
            }
        }

        return result;
    }

    /// <summary>RemovableMedia flag and bus type straight from the device descriptor.</summary>
    private (bool Removable, DiskBusType? Bus) QueryStorageDescriptor(int number)
    {
        using var handle = NativeMethods.OpenForQuery($@"\\.\PhysicalDrive{number}");
        if (handle.IsInvalid)
        {
            return (false, null);
        }

        // STORAGE_PROPERTY_QUERY { StorageDeviceProperty, PropertyStandardQuery }
        var output = NativeMethods.Ioctl(handle, NativeMethods.IoctlStorageQueryProperty, new byte[12], 1024);
        if (output is null)
        {
            _logger.LogDebug("IOCTL_STORAGE_QUERY_PROPERTY failed for disk {Number}: {Error}", number, Marshal.GetLastWin32Error());
            return (false, null);
        }

        return (output[10] != 0, (DiskBusType)BitConverter.ToInt32(output, 28));
    }

    private HashSet<int> GetProtectedDisks(IReadOnlyList<string> extraPaths)
    {
        var paths = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppContext.BaseDirectory,
        };
        paths.AddRange(extraPaths);

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name FROM Win32_PageFileUsage");
            foreach (var pageFile in searcher.Get().Cast<ManagementObject>())
            {
                using (pageFile)
                {
                    if (pageFile["Name"] is string name)
                    {
                        paths.Add(name);
                    }
                }
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogWarning(ex, "Could not list the page files");
        }

        var result = new HashSet<int>();
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var disk in DisksOfPath(path))
            {
                result.Add(disk);
            }
        }

        _logger.LogDebug("Protected disks: {Disks}", string.Join(", ", result));
        return result;
    }

    /// <summary>Disk numbers backing the volume that contains <paramref name="path"/>.</summary>
    private IEnumerable<int> DisksOfPath(string path)
    {
        var volumePath = new char[1024];
        if (!NativeMethods.GetVolumePathNameW(Path.GetFullPath(path), volumePath, (uint)volumePath.Length))
        {
            return [];
        }

        var mountPoint = new string(volumePath).TrimEnd('\0');
        var device = mountPoint.Length == 3 && mountPoint[1] == ':' ? $@"\\.\{mountPoint[0]}:" : Volumes.NameOf(mountPoint);
        if (device is null)
        {
            return [];
        }

        var disks = Volumes.DiskNumbers(device);
        if (disks.Count == 0)
        {
            _logger.LogDebug("Cannot find the disk of {Device}", device);
        }

        return disks;
    }

    private static List<ManagementObject> Query(ManagementScope scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    /// <summary>Disposes WMI objects (the drive list is refreshed every few seconds).</summary>
    private sealed class DisposableList(List<ManagementObject> items) : IDisposable
    {
        public List<ManagementObject> Items { get; } = items;

        public void Dispose()
        {
            foreach (var item in Items)
            {
                item.Dispose();
            }
        }
    }

    private static int ToInt(object? value) => Convert.ToInt32(value ?? 0, System.Globalization.CultureInfo.InvariantCulture);

    private static string? ToLetter(object? value)
    {
        var c = value switch
        {
            char ch => ch,
            ushort u => (char)u,
            _ => '\0',
        };
        return char.IsAsciiLetter(c) ? char.ToUpperInvariant(c).ToString() : null;
    }

    private static void Add(Dictionary<int, List<string>> map, int key, string value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }
}
