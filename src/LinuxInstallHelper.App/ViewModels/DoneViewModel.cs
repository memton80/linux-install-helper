using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Images;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class DoneViewModel : ObservableObject, INavigationAware
{
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;

    public DoneViewModel(ILocalizer localizer, INavigationService navigation, WizardState wizard)
    {
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
    }

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _verification = string.Empty;

    [ObservableProperty]
    private string _signature = string.Empty;

    [ObservableProperty]
    private string _ejectStatus = string.Empty;

    [ObservableProperty]
    private bool _ejected;

    [ObservableProperty]
    private bool _showSecureBootWarning;

    [ObservableProperty]
    private bool _hasIso;

    private string? _isoPath;

    public void OnNavigatedTo(object? parameter)
    {
        var result = _wizard.LastResult;
        if (result is null || _wizard.Target is null)
        {
            _navigation.NavigateTo(PageKeys.Distros, clearHistory: true);
            return;
        }

        Summary = _localizer.Format("Done_Summary", _wizard.SourceName, _wizard.Target.FriendlyName);
        var algorithm = result.HashAlgorithm.ToString().ToUpperInvariant().Replace("SHA", "SHA-", StringComparison.Ordinal);
        Verification = result.WriteVerified
            ? _localizer.Format("Done_VerifiedWrite", algorithm, result.ImageHash)
            : _localizer.Format("Done_VerifiedImage", algorithm, result.ImageHash);

        var signed = result.ChecksumSignature == SignatureStatus.Verified || result.ImageSignature == SignatureStatus.Verified;
        Signature = signed
            ? _localizer.Format("Done_Signed", DisplayFormatter.Fingerprint(result.Signer))
            : _wizard.IsLocalIso ? _localizer.Get("Done_LocalImage") : _localizer.Get("Done_NotSigned");

        Ejected = result.Ejected;
        EjectStatus = result.Ejected
            ? _localizer.Get("Done_Ejected")
            : result.EjectError is null ? _localizer.Get("Done_NotEjected") : _localizer.Format("Done_EjectFailed", result.EjectError);

        ShowSecureBootWarning = _wizard.Distro is { SecureBoot: false };
        _isoPath = result.ImagePath;
        HasIso = File.Exists(result.ImagePath);
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void CreateAnother()
    {
        var local = _wizard.IsLocalIso;
        _wizard.Reset();
        _navigation.NavigateTo(local ? PageKeys.LocalIso : PageKeys.Distros, clearHistory: true);
    }

    [RelayCommand]
    private void ShowIso()
    {
        if (_isoPath is not null)
        {
            SystemActions.ShowInExplorer(_isoPath);
        }
    }

    [RelayCommand]
    private void Close() => App.MainWindow.Close();
}
