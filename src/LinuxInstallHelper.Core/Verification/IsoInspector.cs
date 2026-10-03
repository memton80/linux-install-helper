using System.Text;

namespace LinuxInstallHelper.Core.Verification;

/// <param name="IsIso9660">The file contains an ISO 9660 primary volume descriptor.</param>
/// <param name="IsHybrid">The first sector holds a boot record (MBR signature): the image can be written as-is to a USB drive.</param>
/// <param name="VolumeLabel">Volume identifier of the ISO, when present.</param>
/// <param name="Size">File size in bytes.</param>
public sealed record IsoInfo(bool IsIso9660, bool IsHybrid, string? VolumeLabel, long Size);

/// <summary>Quick structural checks of an ISO file (used for local images).</summary>
public static class IsoInspector
{
    private const int SectorSize = 2048;
    private const long PrimaryVolumeDescriptorOffset = 16 * SectorSize;

    public static IsoInfo Inspect(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = file.Length;

        var mbr = new byte[512];
        var hasMbr = file.Read(mbr, 0, mbr.Length) == mbr.Length && mbr[510] == 0x55 && mbr[511] == 0xAA;

        var descriptor = new byte[SectorSize];
        var isIso = false;
        string? label = null;
        if (size >= PrimaryVolumeDescriptorOffset + SectorSize)
        {
            file.Position = PrimaryVolumeDescriptorOffset;
            file.ReadExactly(descriptor);
            isIso = descriptor[0] == 1 && Encoding.ASCII.GetString(descriptor, 1, 5) == "CD001";
            if (isIso)
            {
                label = Encoding.ASCII.GetString(descriptor, 40, 32).Trim(' ', '\0');
                if (label.Length == 0)
                {
                    label = null;
                }
            }
        }

        return new IsoInfo(isIso, isIso && hasMbr, label, size);
    }
}
