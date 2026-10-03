namespace LinuxInstallHelper.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void All_paths_live_under_the_application_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "lih-tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(Path.Combine(root, AppPaths.AppFolderName), paths.Root);
        Assert.StartsWith(paths.Root, paths.Logs);
        Assert.StartsWith(paths.Root, paths.CatalogCache);
        Assert.StartsWith(paths.Root, paths.DefaultDownloads);
        Assert.StartsWith(paths.Root, paths.SettingsFile);
    }

    [Fact]
    public void EnsureCreated_creates_the_folders()
    {
        var root = Path.Combine(Path.GetTempPath(), "lih-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);
            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.Logs));
            Assert.True(Directory.Exists(paths.CatalogCache));
            Assert.True(Directory.Exists(paths.DefaultDownloads));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
