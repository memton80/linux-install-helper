using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>
/// Shown at the first start: is someone who knows computers helping, or is the user on their own? One click saves the
/// answer and opens the questionnaire. The answer can be changed at any time in the settings.
/// </summary>
public sealed partial class WelcomeViewModel
{
    private readonly ISettingsStore _settings;
    private readonly INavigationService _navigation;

    public WelcomeViewModel(ISettingsStore settings, INavigationService navigation)
    {
        _settings = settings;
        _navigation = navigation;
    }

    [RelayCommand]
    private void ChooseWithHelper() => Choose(GuidanceMode.WithHelper);

    [RelayCommand]
    private void ChooseAlone() => Choose(GuidanceMode.Alone);

    private void Choose(GuidanceMode mode)
    {
        _settings.Save(_settings.Current with { Guidance = mode });
        _navigation.NavigateTo(PageKeys.Advisor, clearHistory: true);
    }
}
