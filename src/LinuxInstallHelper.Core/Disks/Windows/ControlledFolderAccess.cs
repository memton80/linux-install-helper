using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace LinuxInstallHelper.Core.Disks.Windows;

/// <summary>
/// Microsoft Defender's controlled folder access: besides protected folders, it can block the raw disk writes ("sector writes")
/// of applications that it does not trust.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ControlledFolderAccess
{
    private const string Subkey = @"Windows Defender Exploit Guard\Controlled Folder Access";

    /// <summary>The setting, as written in the logs and error messages.</summary>
    public static string Describe() => Read() switch
    {
        null => "unknown",
        0 => "off",
        1 => "on",
        2 => "audit mode",
        3 => "on for disk writes only",
        4 => "audit mode for disk writes only",
        var other => $"unknown setting {other}",
    };

    // EnableControlledFolderAccess: 0 off, 1 on, 2 audit, 3 block disk modification only, 4 audit disk modification only.
    // A group policy (or Intune) setting takes precedence over the one chosen in Windows Security.
    private static int? Read() =>
        ReadValue($@"SOFTWARE\Policies\Microsoft\Windows Defender\{Subkey}") ?? ReadValue($@"SOFTWARE\Microsoft\Windows Defender\{Subkey}");

    private static int? ReadValue(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("EnableControlledFolderAccess") as int?;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
