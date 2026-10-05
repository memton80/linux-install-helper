using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Tests.Helpers;

namespace LinuxInstallHelper.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Defaults_are_safe()
    {
        var settings = new SettingsStore(new AppPaths(_temp.Path)).Current;

        Assert.True(settings.VerifyAfterWrite);
        Assert.True(settings.EjectWhenDone);
        Assert.True(settings.KeepIsoAfterWrite);
        Assert.Null(settings.Language);
        Assert.Equal(AppTheme.System, settings.Theme);
    }

    [Fact]
    public void Settings_are_saved_and_reloaded()
    {
        var paths = new AppPaths(_temp.Path);
        var store = new SettingsStore(paths);
        UserSettings? changed = null;
        store.Changed += (_, s) => changed = s;

        store.Save(new UserSettings { Theme = AppTheme.Dark, Language = "fr-FR", EjectWhenDone = false, DownloadFolder = @"D:\ISO", ChecklistDone = "backup,passwords" });

        var reloaded = new SettingsStore(paths).Current;
        Assert.Equal(AppTheme.Dark, reloaded.Theme);
        Assert.Equal("fr-FR", reloaded.Language);
        Assert.False(reloaded.EjectWhenDone);
        Assert.Equal(@"D:\ISO", reloaded.DownloadFolder);
        Assert.Equal("backup,passwords", reloaded.ChecklistDone);
        Assert.Equal(reloaded, changed);
    }

    [Fact]
    public void A_checklist_item_is_marked_done_once()
    {
        Assert.Equal("backup", new UserSettings().WithChecklistItemDone("backup").ChecklistDone);
        Assert.Equal("passwords,backup", new UserSettings { ChecklistDone = "passwords" }.WithChecklistItemDone("backup").ChecklistDone);

        var done = new UserSettings { ChecklistDone = "backup,passwords" };
        Assert.Same(done, done.WithChecklistItemDone("backup"));
    }

    [Fact]
    public void Unsupported_values_are_dropped()
    {
        var paths = new AppPaths(_temp.Path);
        var store = new SettingsStore(paths);

        store.Save(new UserSettings { Language = "de-DE", DownloadFolder = "  " });

        Assert.Null(store.Current.Language);
        Assert.Null(store.Current.DownloadFolder);
    }

    [Fact]
    public void Corrupted_file_falls_back_to_defaults()
    {
        var paths = new AppPaths(_temp.Path);
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, "{ not json");

        Assert.Equal(new UserSettings(), new SettingsStore(paths).Current);
    }
}
