using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// Whether the user is on their own (the pages then explain each step in more detail) or helped by someone. Follows the
/// settings, unless <c>--guidance</c> forces it for this session.
/// </summary>
public sealed partial class GuidanceState : ObservableObject
{
    private GuidanceMode? _forced;

    public GuidanceState(ISettingsStore settings)
    {
        Apply(settings.Current.Guidance);
        settings.Changed += (_, current) => Apply(current.Guidance);
    }

    /// <summary>The pages add their detailed explanations. False until the welcome screen has asked.</summary>
    [ObservableProperty]
    private bool _isAlone;

    /// <summary>The user has answered the welcome screen (or the mode is forced).</summary>
    [ObservableProperty]
    private bool _hasChosen;

    /// <summary>Forces the mode for this session, without saving it (developer option <c>--guidance</c>).</summary>
    public void Force(GuidanceMode mode)
    {
        _forced = mode;
        Apply(mode);
    }

    private void Apply(GuidanceMode? saved)
    {
        var mode = _forced ?? saved;
        IsAlone = mode == GuidanceMode.Alone;
        HasChosen = mode is not null;
    }
}
