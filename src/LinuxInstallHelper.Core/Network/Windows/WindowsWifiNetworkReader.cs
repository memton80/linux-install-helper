using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Network.Windows;

/// <summary>
/// Reads the Wi-Fi networks saved by Windows through the Native Wifi API. An administrator gets the keys in clear text
/// (<c>WLAN_PROFILE_GET_PLAINTEXT_KEY</c>), like <c>netsh wlan show profile key=clear</c>, without depending on its
/// translated output.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWifiNetworkReader : IWifiNetworkReader
{
    private const uint ClientVersion = 2;
    private const uint ProfileGetPlaintextKey = 0x00000004;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorNotSupported = 50;

    // WLAN_INTERFACE_INFO: GUID (16 bytes), WCHAR[256] description, WLAN_INTERFACE_STATE (4 bytes).
    private const int InterfaceInfoSize = 16 + 512 + 4;

    // WLAN_PROFILE_INFO: WCHAR[256] name, DWORD flags.
    private const int ProfileInfoSize = 512 + 4;

    // Both lists start with two DWORDs: the number of items and an index.
    private const int ListHeaderSize = 8;

    private readonly ILogger _logger;

    public WindowsWifiNetworkReader(ILogger<WindowsWifiNetworkReader>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public Task<WifiNetworks> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(Read, cancellationToken);

    private WifiNetworks Read()
    {
        IntPtr client;
        try
        {
            var opened = WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out client);
            if (opened is ErrorServiceNotActive or ErrorNotSupported)
            {
                return WifiNetworks.Empty(WifiReadStatus.NoWifi);
            }

            if (opened != 0)
            {
                _logger.LogWarning("WlanOpenHandle failed with error {Error}", opened);
                return WifiNetworks.Empty(WifiReadStatus.Failed);
            }
        }
        catch (DllNotFoundException)
        {
            // Windows editions without the wireless components.
            return WifiNetworks.Empty(WifiReadStatus.NoWifi);
        }

        try
        {
            var interfaces = Interfaces(client);
            if (interfaces.Count == 0)
            {
                return WifiNetworks.Empty(WifiReadStatus.NoWifi);
            }

            // The same network is listed by every Wi-Fi adapter that knows it.
            var networks = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
            foreach (var id in interfaces)
            {
                foreach (var profile in ProfileNames(client, id))
                {
                    if (!networks.ContainsKey(profile) && Profile(client, id, profile) is { } network)
                    {
                        networks[profile] = network;
                    }
                }
            }

            // Never log the passwords: only how many networks were found.
            _logger.LogInformation("Read {Count} saved Wi-Fi network(s)", networks.Count);
            return new WifiNetworks(WifiReadStatus.Ok, networks.Values.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
        }
        finally
        {
            WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    private List<Guid> Interfaces(IntPtr client)
    {
        var result = WlanEnumInterfaces(client, IntPtr.Zero, out var list);
        if (result != 0)
        {
            _logger.LogWarning("WlanEnumInterfaces failed with error {Error}", result);
            return [];
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            var ids = new List<Guid>(count);
            for (var i = 0; i < count; i++)
            {
                ids.Add(Marshal.PtrToStructure<Guid>(list + ListHeaderSize + (i * InterfaceInfoSize)));
            }

            return ids;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private List<string> ProfileNames(IntPtr client, Guid id)
    {
        var result = WlanGetProfileList(client, ref id, IntPtr.Zero, out var list);
        if (result != 0)
        {
            _logger.LogWarning("WlanGetProfileList failed with error {Error}", result);
            return [];
        }

        try
        {
            var count = Marshal.ReadInt32(list);
            var names = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                if (Marshal.PtrToStringUni(list + ListHeaderSize + (i * ProfileInfoSize)) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }

            return names;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private WifiNetwork? Profile(IntPtr client, Guid id, string name)
    {
        var flags = ProfileGetPlaintextKey;
        var result = WlanGetProfile(client, ref id, name, IntPtr.Zero, out var xml, ref flags, out _);
        if (result != 0)
        {
            _logger.LogWarning("WlanGetProfile failed with error {Error}", result);
            return null;
        }

        try
        {
            return WifiProfileXml.Parse(Marshal.PtrToStringUni(xml) ?? string.Empty);
        }
        finally
        {
            WlanFreeMemory(xml);
        }
    }

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    private static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetProfileList(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved, out IntPtr profileList);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanGetProfile(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        string profileName,
        IntPtr reserved,
        out IntPtr profileXml,
        ref uint flags,
        out uint grantedAccess);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr memory);
}
