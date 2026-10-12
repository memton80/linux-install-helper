using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// Whether the technical details are shown (the creation log, hashes, ignored disks…). Off by default; turned on from the
/// title bar after a warning, and shown by an orange frame around the window. It only shows or hides details: no check or
/// confirmation depends on it. Follows the settings, unless <c>--advanced</c> turns it on for this session.
/// </summary>
public sealed partial class AdvancedModeState : ObservableObject
{
    private readonly ISettingsStore _settings;
    private bool _forced;

    public AdvancedModeState(ISettingsStore settings)
    {
        _settings = settings;
        IsOn = settings.Current.AdvancedMode;
        settings.Changed += (_, current) => IsOn = _forced || current.AdvancedMode;
    }

    /// <summary>The technical details are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    private bool _isOn;

    /// <summary>The technical details are hidden (the default).</summary>
    public bool IsOff => !IsOn;

    /// <summary>Turns the mode on for this session, without saving it (developer option <c>--advanced</c>).</summary>
    public void Force()
    {
        _forced = true;
        IsOn = true;
    }

    /// <summary>Turns the mode on or off and saves it. Asking for a confirmation is up to the caller.</summary>
    public void Set(bool on)
    {
        _forced = false;
        if (_settings.Current.AdvancedMode != on)
        {
            _settings.Save(_settings.Current with { AdvancedMode = on });
        }

        IsOn = on;
    }
}
