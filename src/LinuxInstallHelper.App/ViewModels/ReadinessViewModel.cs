using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Network;
using LinuxInstallHelper.Core.Readiness;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A check of the readiness page, shown as an info bar. Its texts are Ready_{Check}_{Variant}_Title and _Message.</summary>
public sealed class ReadinessItemViewModel
{
    public ReadinessItemViewModel(ReadinessItem item, ILocalizer localizer, DisplayFormatter formatter, Func<ReadinessAction, Task> act)
    {
        Item = item;
        object?[] args =
        [
            item.Name ?? string.Empty,
            item.Amount is { } amount ? formatter.Size(amount) : string.Empty,
            item.Needed is { } needed ? formatter.Size(needed) : string.Empty,
        ];
        Title = localizer.Format($"Ready_{item.Key}_Title", args);
        Message = localizer.Format($"Ready_{item.Key}_Message", args);
        Severity = SeverityOf(item.Level);
        HasAction = item.Action != ReadinessAction.None;
        ActionLabel = HasAction ? localizer.Get($"Ready_Action_{item.Action}") : string.Empty;
        ActionCommand = new AsyncRelayCommand(() => act(item.Action));
    }

    public ReadinessItem Item { get; }

    public string Title { get; }

    public string Message { get; }

    public InfoBarSeverity Severity { get; }

    public bool HasAction { get; }

    public string ActionLabel { get; }

    public IAsyncRelayCommand ActionCommand { get; }

    public static InfoBarSeverity SeverityOf(ReadinessLevel level) => level switch
    {
        ReadinessLevel.Ok => InfoBarSeverity.Success,
        ReadinessLevel.Info => InfoBarSeverity.Informational,
        ReadinessLevel.Warning => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Error,
    };
}

/// <summary>A saved Wi-Fi network, its password hidden until the user shows it.</summary>
public sealed partial class WifiNetworkViewModel : ObservableObject
{
    private const string Mask = "••••••••";

    public WifiNetworkViewModel(WifiNetwork network, ILocalizer localizer)
    {
        Name = network.Name;
        Password = network.Password;
        HasPassword = network.Password is not null;
        Detail = network switch
        {
            { Password: not null } => string.Empty,
            { Security: WifiSecurity.Open } => localizer.Get("Wifi_Open"),
            { Security: WifiSecurity.Enterprise } => localizer.Get("Wifi_Enterprise"),
            _ => localizer.Get("Wifi_Unavailable"),
        };
        ShowLabel = localizer.Get("Wifi_Show");
        CopyLabel = localizer.Get("Wifi_Copy");
        CopyName = localizer.Format("Wifi_CopyName", network.Name);
    }

    public string Name { get; }

    public string? Password { get; }

    public bool HasPassword { get; }

    /// <summary>Why there is no password to show, empty when there is one.</summary>
    public string Detail { get; }

    public string ShowLabel { get; }

    public string CopyLabel { get; }

    /// <summary>Accessible name of the copy button.</summary>
    public string CopyName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedPassword))]
    private bool _isRevealed;

    public string DisplayedPassword => IsRevealed ? Password ?? string.Empty : Mask;

    [RelayCommand]
    private void Copy()
    {
        if (Password is not null)
        {
            SystemActions.CopyText(Password);
        }
    }
}

/// <summary>
/// "Prepare my PC": whether the drive will start on this computer and what to prepare before installing Linux, for the
/// chosen distribution; how to start from the drive; the saved Wi-Fi passwords; a link to the list of programs.
/// </summary>
public sealed partial class ReadinessViewModel : ObservableObject, INavigationAware
{
    private readonly PcInfo _pc;
    private readonly ReadinessActions _actions;
    private readonly ICatalogService _catalog;
    private readonly WizardState _wizard;
    private readonly ILocalizer _localizer;
    private readonly DisplayFormatter _formatter;
    private readonly INavigationService _navigation;
    private readonly IWifiNetworkReader _wifi;
    private readonly ILogger<ReadinessViewModel> _logger;
    private readonly Dictionary<string, Distro> _distros = new(StringComparer.Ordinal);
    private PcFacts? _facts;
    private bool _hasDistros;

    public ReadinessViewModel(
        PcInfo pc,
        ReadinessActions actions,
        ICatalogService catalog,
        WizardState wizard,
        ILocalizer localizer,
        DisplayFormatter formatter,
        INavigationService navigation,
        IWifiNetworkReader wifi,
        ILogger<ReadinessViewModel> logger)
    {
        _pc = pc;
        _actions = actions;
        _catalog = catalog;
        _wizard = wizard;
        _localizer = localizer;
        _formatter = formatter;
        _navigation = navigation;
        _wifi = wifi;
        _logger = logger;
        DistroOptions.Add(new FilterOption(null, localizer.Get("Readiness_AnyDistro")));
        BootKeys = localizer.Get("Boot_KeysUnknown");
    }

    /// <summary>"Linux in general", then the distributions of the catalog.</summary>
    public ObservableCollection<FilterOption> DistroOptions { get; } = [];

    public ObservableCollection<ReadinessItemViewModel> Items { get; } = [];

    public ObservableCollection<WifiNetworkViewModel> Networks { get; } = [];

    [ObservableProperty]
    private int _distroIndex;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private InfoBarSeverity _summarySeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _summaryTitle = string.Empty;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private string _bootKeys = string.Empty;

    /// <summary>The computer starts in UEFI mode: Windows can restart it into its boot options or its settings.</summary>
    [ObservableProperty]
    private bool _canRestart;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowWifiCommand))]
    private bool _isReadingWifi;

    [ObservableProperty]
    private string _wifiStatus = string.Empty;

    public void OnNavigatedTo(object? parameter)
    {
        _ = LoadAsync(refresh: false);
        if (!_hasDistros)
        {
            _ = LoadDistrosAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnDistroIndexChanged(int value) => Evaluate();

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(refresh: true);

    [RelayCommand]
    private Task RestartToDriveAsync() => _actions.RestartToDriveAsync();

    [RelayCommand]
    private Task RestartToFirmwareAsync() => _actions.RestartToFirmwareAsync();

    [RelayCommand]
    private void OpenSoftware() => _navigation.NavigateTo(PageKeys.Software);

    [RelayCommand(CanExecute = nameof(CanShowWifi))]
    private async Task ShowWifiAsync()
    {
        IsReadingWifi = true;
        Networks.Clear();
        WifiStatus = string.Empty;
        try
        {
            var result = await _wifi.ReadAsync();
            foreach (var network in result.Networks)
            {
                Networks.Add(new WifiNetworkViewModel(network, _localizer));
            }

            WifiStatus = result.Status switch
            {
                WifiReadStatus.NoWifi => _localizer.Get("Wifi_NoWifi"),
                WifiReadStatus.Failed => _localizer.Get("Wifi_Failed"),
                _ when Networks.Count == 0 => _localizer.Get("Wifi_None"),
                _ => string.Empty,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the saved Wi-Fi networks");
            WifiStatus = _localizer.Get("Wifi_Failed");
        }
        finally
        {
            IsReadingWifi = false;
        }
    }

    private bool CanShowWifi() => !IsReadingWifi;

    private async Task LoadAsync(bool refresh)
    {
        IsLoading = true;
        _facts = await (refresh ? _pc.RefreshAsync() : _pc.GetAsync());
        IsLoading = false;
        BootKeys = _actions.DescribeKeys(PcInfo.KeysOf(_facts));
        CanRestart = _facts.Firmware == FirmwareKind.Uefi;
        Evaluate();
    }

    private async Task LoadDistrosAsync()
    {
        _hasDistros = true;
        var distros = (_catalog.Current ?? await _catalog.LoadAsync()).Catalog.Distros;
        foreach (var distro in distros)
        {
            _distros[distro.Id] = distro;
            DistroOptions.Add(new FilterOption(distro.Id, distro.DisplayName));
        }

        // The distribution chosen for the drive, when there is one.
        var chosen = DistroOptions.ToList().FindIndex(option => option.Key is not null && option.Key == _wizard.Distro?.Id);
        DistroIndex = Math.Max(chosen, 0);
    }

    private void Evaluate()
    {
        if (_facts is null)
        {
            return;
        }

        var key = DistroIndex >= 0 && DistroIndex < DistroOptions.Count ? DistroOptions[DistroIndex].Key : null;
        var distro = key is not null && _distros.TryGetValue(key, out var found) ? found : null;
        var items = PcReadiness.Evaluate(_facts, distro);

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(new ReadinessItemViewModel(item, _localizer, _formatter, ActAsync));
        }

        var blockers = items.Count(i => i.Level == ReadinessLevel.Blocker);
        var warnings = items.Count(i => i.Level == ReadinessLevel.Warning);
        SummarySeverity = ReadinessItemViewModel.SeverityOf(PcReadiness.Worst(items));
        (SummaryTitle, SummaryMessage) = (blockers, warnings) switch
        {
            ( > 0, _) => (_localizer.Get("Readiness_BlockedTitle"), _localizer.Format("Readiness_Counts", blockers, warnings)),
            (0, > 0) => (_localizer.Get("Readiness_WarningsTitle"), _localizer.Format("Readiness_Warnings", warnings)),
            _ => (_localizer.Get("Readiness_ReadyTitle"), _localizer.Get("Readiness_Ready")),
        };
    }

    private async Task ActAsync(ReadinessAction action)
    {
        if (await _actions.RunAsync(action))
        {
            await LoadAsync(refresh: true);
        }
    }
}
