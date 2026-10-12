using CommunityToolkit.Mvvm.ComponentModel;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// Whether the technical details are shown (the creation log, the details of an error, hashes and key fingerprints). Off at
/// each start, so that nobody finds it on without having accepted its warning; turned on from the title bar after that
/// warning, and shown by an orange frame around the window. It only shows or hides details: no check, warning, error
/// message or confirmation depends on it. <c>--advanced</c> turns it on for the session (used by the CI).
/// </summary>
public sealed partial class AdvancedModeState : ObservableObject
{
    /// <summary>The technical details are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff))]
    private bool _isOn;

    /// <summary>The technical details are hidden (the default).</summary>
    public bool IsOff => !IsOn;
}
