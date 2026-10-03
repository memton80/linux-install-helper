using System.Text;
using LinuxInstallHelper.Core.Tests.Helpers;
using LinuxInstallHelper.Core.Verification;

namespace LinuxInstallHelper.Core.Tests.Verification;

public sealed class IsoInspectorTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string CreateIso(bool withMbr, bool withDescriptor, string label = "Ubuntu 26.04 LTS amd64")
    {
        var data = new byte[40 * 2048];
        if (withMbr)
        {
            data[510] = 0x55;
            data[511] = 0xAA;
        }

        if (withDescriptor)
        {
            var pvd = 16 * 2048;
            data[pvd] = 1;
            Encoding.ASCII.GetBytes("CD001").CopyTo(data, pvd + 1);
            Encoding.ASCII.GetBytes(label.PadRight(32)).CopyTo(data, pvd + 40);
        }

        var path = _temp.File(Guid.NewGuid().ToString("N") + ".iso");
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public void Detects_a_hybrid_iso_and_its_label()
    {
        var info = IsoInspector.Inspect(CreateIso(withMbr: true, withDescriptor: true));

        Assert.True(info.IsIso9660);
        Assert.True(info.IsHybrid);
        Assert.Equal("Ubuntu 26.04 LTS amd64", info.VolumeLabel);
    }

    [Fact]
    public void Iso_without_boot_record_is_not_hybrid()
    {
        var info = IsoInspector.Inspect(CreateIso(withMbr: false, withDescriptor: true));

        Assert.True(info.IsIso9660);
        Assert.False(info.IsHybrid);
    }

    [Fact]
    public void Random_file_is_not_an_iso()
    {
        var info = IsoInspector.Inspect(Fixtures.PathOf("payload.bin"));

        Assert.False(info.IsIso9660);
        Assert.False(info.IsHybrid);
    }

    [Fact]
    public void Tiny_file_is_not_an_iso()
    {
        var path = _temp.File("tiny.iso");
        File.WriteAllBytes(path, new byte[100]);

        Assert.False(IsoInspector.Inspect(path).IsIso9660);
    }
}
