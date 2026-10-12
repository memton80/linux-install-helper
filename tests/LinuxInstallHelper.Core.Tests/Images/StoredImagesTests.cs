using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Images;

public sealed class StoredImagesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Lists_images_and_interrupted_downloads_newest_first()
    {
        var old = Add("debian-13.6.0-amd64-netinst.iso", 1000, DateTime.UtcNow.AddDays(-3));
        var recent = Add("ubuntu-26.04.1-desktop-amd64.iso", 3000, DateTime.UtcNow.AddHours(-1));
        var partial = Add("Fedora-Workstation-Live-44.iso.part", 500, DateTime.UtcNow.AddDays(-1));
        Add("Fedora-Workstation-Live-44.iso.part.json", 10, DateTime.UtcNow);
        Add("notes.txt", 10, DateTime.UtcNow);

        var images = StoredImages.List(_temp.Path);

        Assert.Equal([recent, partial, old], images.Select(i => i.Path));
        Assert.Equal([false, true, false], images.Select(i => i.IsPartial));
        Assert.Equal("Fedora-Workstation-Live-44.iso", images[1].Name);
        Assert.Equal(3000, images[0].Size);
    }

    [Fact]
    public void Deleting_an_interrupted_download_removes_its_state()
    {
        Add("Fedora-Workstation-Live-44.iso.part", 500, DateTime.UtcNow);
        var state = Add("Fedora-Workstation-Live-44.iso.part.json", 10, DateTime.UtcNow);

        StoredImages.Delete(Assert.Single(StoredImages.List(_temp.Path)));

        Assert.Empty(StoredImages.List(_temp.Path));
        Assert.False(File.Exists(state));
    }

    [Fact]
    public void A_missing_folder_has_no_images()
    {
        Assert.Empty(StoredImages.List(_temp.File("missing")));
        Assert.Empty(StoredImages.List(string.Empty));
    }

    private string Add(string name, int size, DateTime lastWrite)
    {
        var path = _temp.File(name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, lastWrite);
        return Path.GetFullPath(path);
    }
}
