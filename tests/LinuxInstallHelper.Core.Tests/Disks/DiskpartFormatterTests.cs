using LinuxInstallHelper.Core.Disks.Windows;

namespace LinuxInstallHelper.Core.Tests.Disks;

public class DiskpartFormatterTests
{
    [Theory]
    [InlineData("My USB", "MY USB")]
    [InlineData("clé\"; clean all", "CL CLEAN AL")]
    [InlineData("", "USB")]
    [InlineData("   ", "USB")]
    [InlineData("a-very-long-label-name", "A-VERY-LONG")]
    public void Labels_are_sanitized_before_reaching_diskpart(string input, string expected)
    {
#pragma warning disable CA1416 // Pure string logic, safe on every platform.
        Assert.Equal(expected, DiskpartFormatter.SanitizeLabel(input));
#pragma warning restore CA1416
    }
}
