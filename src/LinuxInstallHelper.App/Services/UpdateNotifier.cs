using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Updates;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// Looks once, at startup, for a newer release of the application (the executable is not installed, so nothing else would
/// tell the user). Shown by the main window and the About page.
/// </summary>
public sealed partial class UpdateNotifier : ObservableObject
{
    private readonly IAppUpdateChecker _checker;
    private readonly ISettingsStore _settings;
    private bool _checked;

    public UpdateNotifier(IAppUpdateChecker checker, ISettingsStore settings)
    {
        _checker = checker;
        _settings = settings;
    }

    /// <summary>The newer release, null when the running version is the latest (or when it is not known).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    private AppRelease? _available;

    public bool IsAvailable => Available is not null;

    /// <summary>Checks once per run, when the setting allows it.</summary>
    public async Task CheckAsync()
    {
        if (_checked || !_settings.Current.CheckForUpdates)
        {
            return;
        }

        _checked = true;
        Available = await _checker.FindNewerAsync(AppInfo.Version);
    }

    public void OpenReleasePage()
    {
        if (Available is { } release)
        {
            SystemActions.OpenUrl(release.Page.ToString());
        }
    }
}
