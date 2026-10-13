using System.Runtime.InteropServices;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Catalog;

public class DistroArchitecturesTests
{
    [Theory]
    [InlineData("ubuntu-26.04.1-desktop-amd64.iso", DistroArchitectures.X64)]
    [InlineData("Fedora-Workstation-Live-44-1.7.x86_64.iso", DistroArchitectures.X64)]
    [InlineData("openSUSE-Tumbleweed-DVD-x86_64-Current.iso", DistroArchitectures.X64)]
    [InlineData(@"C:\Users\me\Downloads\archlinux-2026.10.01-x86_64.iso", DistroArchitectures.X64)]
    [InlineData("ubuntu-26.04.1-desktop-arm64.iso", DistroArchitectures.Arm64)]
    [InlineData("debian-13.7.0-arm64-netinst.iso", DistroArchitectures.Arm64)]
    [InlineData("Fedora-Workstation-Live-44-1.7.aarch64.iso", DistroArchitectures.Arm64)]
    [InlineData("KALI-LINUX-2026.2-INSTALLER-ARM64.ISO", DistroArchitectures.Arm64)]
    public void The_file_name_tells_the_architecture(string fileName, string expected)
    {
        Assert.Equal(expected, DistroArchitectures.FromFileName(fileName));
    }

    [Theory]
    [InlineData("Zorin-OS-18.1-Core-64-bit.iso")]
    [InlineData("linuxmint-22.3-cinnamon-64bit.iso")]
    [InlineData("farmhouse64.iso")]
    [InlineData("multi-arch-amd64-arm64.iso")]
    [InlineData("")]
    [InlineData(null)]
    public void A_file_name_that_does_not_say_gives_no_architecture(string? fileName)
    {
        Assert.Null(DistroArchitectures.FromFileName(fileName));
    }

    [Fact]
    public void Catalog_file_names_agree_with_their_architecture()
    {
        var wrong = TestDistros.Embedded().Distros
            .Where(d => DistroArchitectures.FromFileName(d.Image.FileName) is { } named && named != d.Architecture)
            .Select(d => d.Id)
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void The_catalog_has_images_for_both_processors()
    {
        var architectures = TestDistros.Embedded().Distros.Select(d => d.Architecture).ToHashSet();

        Assert.Equal(DistroArchitectures.All.ToHashSet(), architectures);
    }

    [Theory]
    [InlineData((ushort)0x8664, DistroArchitectures.X64)]
    [InlineData((ushort)0xAA64, DistroArchitectures.Arm64)]
    [InlineData((ushort)0x014C, null)]
    public void Windows_machine_types_are_named_like_the_catalog(ushort machine, string? expected)
    {
        Assert.Equal(expected, MachineArchitecture.FromMachine(machine));
    }

    [Fact]
    public void This_computer_has_a_processor_of_the_catalog()
    {
        Assert.Equal(MachineArchitecture.FromArchitecture(RuntimeInformation.OSArchitecture), MachineArchitecture.Current);
        Assert.True(MachineArchitecture.Boots(MachineArchitecture.Current));
        Assert.True(MachineArchitecture.Boots(null));
    }
}
