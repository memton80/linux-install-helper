using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using LinuxInstallHelper.Core.Disks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace LinuxInstallHelper.Core.Readiness.Windows;

/// <summary>Reads the facts of <see cref="PcFacts"/> from WMI, the registry and the firmware API.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPcProbe : IPcProbe
{
    private const string SecureBootKey = @"SYSTEM\CurrentControlSet\Control\SecureBoot\State";
    private const string SessionPowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
    private const string EncryptionNamespace = @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption";

    // Win32_ComputerSystem.PCSystemType
    private const int MobileComputer = 2;

    private static readonly string[] WirelessWords = ["Wi-Fi", "WiFi", "Wireless", "WLAN", "802.11"];

    private readonly IDiskService _disks;
    private readonly ILogger _logger;

    public WindowsPcProbe(IDiskService disks, ILogger<WindowsPcProbe>? logger = null)
    {
        _disks = disks;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public async Task<PcFacts> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var systemDiskBus = await ReadAsync("system disk", async () =>
        {
            var disks = await _disks.GetDisksAsync(cancellationToken).ConfigureAwait(false);
            return disks.Where(d => d.IsBoot && d.BusType != DiskBusType.Usb).Select(d => (DiskBusType?)d.BusType).FirstOrDefault();
        }).ConfigureAwait(false);

        return await Task.Run(() => Probe(systemDiskBus), cancellationToken).ConfigureAwait(false);
    }

    public void DisableFastStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(SessionPowerKey, writable: true)
            ?? throw new IOException($@"HKLM\{SessionPowerKey} is missing.");
        key.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
        _logger.LogInformation("Fast startup turned off");
    }

    private PcFacts Probe(DiskBusType? systemDiskBus)
    {
        var basic = PcFacts.Basic();
        var system = Read("computer", () => QueryFirst(@"root\cimv2", "SELECT Manufacturer, Model, SystemFamily, PCSystemType, TotalPhysicalMemory FROM Win32_ComputerSystem"));
        var facts = basic with
        {
            Firmware = Read("firmware", FirmwareType),
            SecureBootEnabled = Read("Secure Boot", () => RegistryDword(SecureBootKey, "UEFISecureBootEnabled") is { } value ? value != 0 : (bool?)null),
            SystemDiskBus = systemDiskBus,
            StorageControllers = Read("storage controllers", () => Names("SELECT Name FROM Win32_SCSIController")) ?? [],
            SystemDriveEncrypted = Read("BitLocker", SystemDriveEncrypted),
            FastStartupEnabled = Read("fast startup", FastStartup),
            SystemDriveFreeBytes = Read("free space", () => (long?)new DriveInfo(SystemDrive()).AvailableFreeSpace),
            MemoryBytes = Read("memory", () => system?["TotalPhysicalMemory"] is { } total ? Convert.ToInt64(total, System.Globalization.CultureInfo.InvariantCulture) : (long?)null)
                ?? basic.MemoryBytes,
            GraphicsAdapters = Read("graphics", () => Names("SELECT Name FROM Win32_VideoController")) ?? [],
            WirelessAdapters = Read("Wi-Fi", () => Names("SELECT Name FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")?
                .Where(name => WirelessWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .ToList()) ?? [],
            Manufacturer = (system?["Manufacturer"] as string)?.Trim(),
            Model = (system?["Model"] as string)?.Trim(),
            Family = Read("family", () => (system?["SystemFamily"] as string)?.Trim()),
            BoardManufacturer = Read("motherboard", () => (QueryFirst(@"root\cimv2", "SELECT Manufacturer FROM Win32_BaseBoard")?["Manufacturer"] as string)?.Trim()),
            IsLaptop = Read("battery", () => (bool?)(system?["PCSystemType"] is { } type && Convert.ToInt32(type, System.Globalization.CultureInfo.InvariantCulture) == MobileComputer
                || Names("SELECT Name FROM Win32_Battery")?.Count > 0)),
        };

        system?.Dispose();
        _logger.LogInformation(
            "Computer: {Manufacturer} {Model} ({Family}), board {Board}, {Processor}, {Firmware}, Secure Boot {SecureBoot}, system disk {Bus}, controllers [{Controllers}], "
                + "encrypted {Encrypted}, fast startup {FastStartup}, free {Free} bytes, memory {Memory} bytes, graphics [{Graphics}], Wi-Fi [{Wifi}], laptop {Laptop}",
            facts.Manufacturer, facts.Model, facts.Family, facts.BoardManufacturer, facts.Processor, facts.Firmware, facts.SecureBootEnabled, facts.SystemDiskBus,
            string.Join("; ", facts.StorageControllers), facts.SystemDriveEncrypted, facts.FastStartupEnabled, facts.SystemDriveFreeBytes, facts.MemoryBytes,
            string.Join("; ", facts.GraphicsAdapters), string.Join("; ", facts.WirelessAdapters), facts.IsLaptop);
        return facts;
    }

    private static string SystemDrive() => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    private static FirmwareKind FirmwareType() =>
        GetFirmwareType(out var type) ? type switch
        {
            1 => FirmwareKind.Bios,
            2 => FirmwareKind.Uefi,
            _ => FirmwareKind.Unknown,
        } : FirmwareKind.Unknown;

    /// <summary>BitLocker (or device encryption) has encrypted the Windows drive, even partly or with its protection suspended.</summary>
    private static bool? SystemDriveEncrypted()
    {
        var drive = SystemDrive().TrimEnd('\\');
        var scope = new ManagementScope(EncryptionNamespace);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT DriveLetter, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume"));
        foreach (var volume in searcher.Get().Cast<ManagementObject>())
        {
            using (volume)
            {
                if (!string.Equals(volume["DriveLetter"] as string, drive, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // ProtectionStatus 1: protection on. ConversionStatus 0: fully decrypted.
                var protection = volume["ProtectionStatus"] is { } p ? Convert.ToInt32(p, System.Globalization.CultureInfo.InvariantCulture) : 0;
                var conversion = volume["ConversionStatus"] is { } c ? Convert.ToInt32(c, System.Globalization.CultureInfo.InvariantCulture) : 0;
                return protection == 1 || conversion != 0;
            }
        }

        return false;
    }

    /// <summary>Fast startup needs hibernation: it is only on when both are.</summary>
    private static bool? FastStartup()
    {
        if (RegistryDword(PowerKey, "HibernateEnabled") == 0)
        {
            return false;
        }

        return RegistryDword(SessionPowerKey, "HiberbootEnabled") is { } value ? value != 0 : null;
    }

    private static int? RegistryDword(string path, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : null;
    }

    private static ManagementObject? QueryFirst(string scope, string query)
    {
        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();
        ManagementObject? first = null;
        foreach (var item in results.Cast<ManagementObject>())
        {
            if (first is null)
            {
                first = item;
            }
            else
            {
                item.Dispose();
            }
        }

        return first;
    }

    private static List<string>? Names(string query)
    {
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", query);
        var names = new List<string>();
        foreach (var item in searcher.Get().Cast<ManagementObject>())
        {
            using (item)
            {
                if (item["Name"] is string name && !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A fact that cannot be read (missing WMI class, older Windows, refused access…) is left out: never an error for the user.
    private T? Read<T>(string what, Func<T?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the {What} of this computer", what);
            return default;
        }
    }

    private async Task<T?> ReadAsync<T>(string what, Func<Task<T?>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the {What} of this computer", what);
            return default;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFirmwareType(out int firmwareType);
}
