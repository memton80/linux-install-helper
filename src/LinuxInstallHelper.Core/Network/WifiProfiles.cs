using System.Xml;
using System.Xml.Linq;

namespace LinuxInstallHelper.Core.Network;

/// <summary>A Wi-Fi network saved by Windows.</summary>
/// <param name="Name">Name of the network (its SSID).</param>
/// <param name="Password">The key, or null for an open network, a company network or a key Windows keeps encrypted.</param>
/// <param name="Security">How the network is protected.</param>
public sealed record WifiNetwork(string Name, string? Password, WifiSecurity Security);

public enum WifiSecurity
{
    /// <summary>Protected by a password (WPA2, WPA3, WEP…).</summary>
    Password,

    /// <summary>No password.</summary>
    Open,

    /// <summary>A company network with a user name (802.1X): no shared password.</summary>
    Enterprise,
}

public enum WifiReadStatus
{
    Ok,

    /// <summary>This computer has no Wi-Fi (or the Wi-Fi service of Windows is stopped).</summary>
    NoWifi,

    Failed,
}

public sealed record WifiNetworks(WifiReadStatus Status, IReadOnlyList<WifiNetwork> Networks)
{
    public static WifiNetworks Empty(WifiReadStatus status) => new(status, []);
}

public interface IWifiNetworkReader
{
    /// <summary>The saved networks, with their passwords. Only called when the user asks to see them.</summary>
    Task<WifiNetworks> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads the profile XML that Windows stores for each Wi-Fi network (<c>WLANProfile</c>, schema v1).</summary>
public static class WifiProfileXml
{
    /// <summary>The network described by <paramref name="xml"/>, or null when it is not a Wi-Fi profile.</summary>
    public static WifiNetwork? Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return null;
        }

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "WLANProfile")
        {
            return null;
        }

        var ssid = First(root, "SSIDConfig", "SSID", "name") ?? First(root, "name");
        if (string.IsNullOrEmpty(ssid))
        {
            return null;
        }

        var authentication = First(root, "MSM", "security", "authEncryption", "authentication");
        var oneX = string.Equals(First(root, "MSM", "security", "authEncryption", "useOneX"), "true", StringComparison.OrdinalIgnoreCase);
        // WPA, WPA2, WPA3 and WPA3ENT… without "PSK" or "SAE" are the company (802.1X) variants.
        if (oneX || authentication is "WPA" or "WPA2" or "WPA3" || (authentication?.StartsWith("WPA3ENT", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return new WifiNetwork(ssid, null, WifiSecurity.Enterprise);
        }

        var key = Element(root, "MSM", "security", "sharedKey");
        if (key is null)
        {
            var open = authentication is null || authentication.Equals("open", StringComparison.OrdinalIgnoreCase) || authentication.Equals("OWE", StringComparison.OrdinalIgnoreCase);
            return new WifiNetwork(ssid, null, open ? WifiSecurity.Open : WifiSecurity.Password);
        }

        // Without administrator rights, Windows returns the key encrypted (protected = true): it is useless to the user.
        var isProtected = !string.Equals(Child(key, "protected")?.Value, "false", StringComparison.OrdinalIgnoreCase);
        var material = Child(key, "keyMaterial")?.Value;
        return new WifiNetwork(ssid, isProtected || string.IsNullOrEmpty(material) ? null : material, WifiSecurity.Password);
    }

    private static string? First(XElement root, params string[] path) => Element(root, path)?.Value.Trim();

    private static XElement? Element(XElement root, params string[] path)
    {
        var current = root;
        foreach (var name in path)
        {
            current = Child(current, name);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static XElement? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
}
