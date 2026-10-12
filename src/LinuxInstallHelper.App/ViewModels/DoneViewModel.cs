using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Readiness;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class DoneViewModel : ObservableObject, INavigationAware
{
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;
    private readonly PcInfo _pc;
    private readonly ReadinessActions _actions;
    private readonly DisplayFormatter _formatter;

    public DoneViewModel(ILocalizer localizer, INavigationService navigation, WizardState wizard, PcInfo pc, ReadinessActions actions, DisplayFormatter formatter)
    {
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _pc = pc;
        _actions = actions;
        _formatter = formatter;
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
    private bool _showBackupReminder;

    [ObservableProperty]
    private bool _hasIso;

    /// <summary>Which keys open the boot menu of this computer.</summary>
    [ObservableProperty]
    private string _bootKeys = string.Empty;

    /// <summary>The computer starts in UEFI mode: Windows can restart it into its boot options or its settings.</summary>
    [ObservableProperty]
    private bool _canRestart;

    /// <summary>This computer has points to fix or to keep in mind before installing (other than Secure Boot).</summary>
    [ObservableProperty]
    private bool _showReadiness;

    [ObservableProperty]
    private InfoBarSeverity _readinessSeverity = InfoBarSeverity.Warning;

    [ObservableProperty]
    private string _readinessTitle = string.Empty;

    [ObservableProperty]
    private string _readinessMessage = string.Empty;

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
        ShowBackupReminder = _wizard.BackupPending;
        _isoPath = result.ImagePath;
        HasIso = File.Exists(result.ImagePath);
        BootKeys = _actions.DescribeKeys(null);
        _ = LoadPcAsync();
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void OpenGuide() => _navigation.NavigateTo(PageKeys.Guide);

    [RelayCommand]
    private void OpenReadiness() => _navigation.NavigateTo(PageKeys.Readiness);

    [RelayCommand]
    private Task RestartToDriveAsync() => _actions.RestartToDriveAsync();

    [RelayCommand]
    private Task RestartToFirmwareAsync() => _actions.RestartToFirmwareAsync();

    /// <summary>What this computer needs before Linux is started and installed on it (the drive is usually made for it).</summary>
    private async Task LoadPcAsync()
    {
        var facts = await _pc.GetAsync();
        BootKeys = _actions.DescribeKeys(PcInfo.KeysOf(facts));
        CanRestart = facts.Firmware == FirmwareKind.Uefi;

        // Nothing to turn off when Secure Boot is already off, or in the old BIOS mode that has none.
        ShowSecureBootWarning = _wizard.Distro is { SecureBoot: false } && facts.SecureBootEnabled != false && facts.Firmware != FirmwareKind.Bios;

        var issues = PcReadiness.Evaluate(facts, _wizard.Distro)
            .Where(item => item.Level >= ReadinessLevel.Warning && item.Check != ReadinessCheck.SecureBoot)
            .ToList();
        ShowReadiness = issues.Count > 0;
        if (issues.Count > 0)
        {
            ReadinessSeverity = ReadinessItemViewModel.SeverityOf(PcReadiness.Worst(issues));
            ReadinessTitle = _localizer.Format(issues.Exists(i => i.Level == ReadinessLevel.Blocker) ? "Done_ReadinessBlocked" : "Done_Readiness", issues.Count);
            ReadinessMessage = string.Join("\n", issues.Select(item => "• " + new ReadinessItemViewModel(item, _localizer, _formatter, _ => Task.CompletedTask).Title));
        }
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
