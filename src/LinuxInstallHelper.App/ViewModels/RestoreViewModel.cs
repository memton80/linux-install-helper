using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>Turns a drive written with a Linux image back into a normal exFAT USB drive.</summary>
public sealed partial class RestoreViewModel : ObservableObject, INavigationAware
{
    private readonly DriveScanner _scanner;
    private readonly IDiskFormatter _formatter;
    private readonly ILocalizer _localizer;
    private readonly IDialogService _dialogs;
    private readonly DisplayFormatter _display;
    private readonly ErrorDescriber _errors;
    private readonly ISettingsStore _settings;
    private readonly AppPaths _paths;
    private readonly AppBusyState _busy;
    private readonly INavigationService _navigation;
    private readonly ILogger<RestoreViewModel> _logger;

    public RestoreViewModel(
        DriveScanner scanner,
        IDiskFormatter formatter,
        ILocalizer localizer,
        IDialogService dialogs,
        DisplayFormatter display,
        ErrorDescriber errors,
        ISettingsStore settings,
        AppPaths paths,
        AppBusyState busy,
        INavigationService navigation,
        ILogger<RestoreViewModel> logger)
    {
        _scanner = scanner;
        _formatter = formatter;
        _localizer = localizer;
        _dialogs = dialogs;
        _display = display;
        _errors = errors;
        _settings = settings;
        _paths = paths;
        _busy = busy;
        _navigation = navigation;
        _logger = logger;
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private DriveItemViewModel? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isConfirmed;

    [ObservableProperty]
    private string _confirmText = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _label = "USB";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isWorking;

    [ObservableProperty]
    private bool _hasNoDrives;

    [ObservableProperty]
    private bool _succeeded;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public void OnNavigatedTo(object? parameter) => _ = RefreshAsync();

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var scan = await _scanner.ScanAsync(0, [_settings.Current.DownloadFolder ?? _paths.DefaultDownloads, _paths.Root]);
            var selected = SelectedDrive?.Disk;
            Drives.Clear();
            foreach (var disk in scan.Eligible)
            {
                Drives.Add(new DriveItemViewModel(disk, _display, _localizer));
            }

            SelectedDrive = selected is null ? null : Drives.FirstOrDefault(d => d.IsSameDrive(selected));
            HasNoDrives = Drives.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not list the drives");
            ShowError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        var drive = SelectedDrive!;
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Restore_ConfirmTitle"),
            _localizer.Format("Restore_ConfirmMessage", drive.Name, drive.Size, drive.Disk.DisplayLetters),
            _localizer.Get("Restore_ConfirmPrimary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        IsWorking = true;
        Succeeded = false;
        HasError = false;
        _busy.IsBusy = true;
        _navigation.IsLocked = true;
        try
        {
            await _formatter.FormatAsync(drive.Disk, Label);
            Succeeded = true;
            IsConfirmed = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed");
            ShowError(ex);
        }
        finally
        {
            IsWorking = false;
            _busy.IsBusy = false;
            _navigation.IsLocked = false;
        }

        await RefreshAsync();
    }

    private bool CanRestore() => SelectedDrive is not null && IsConfirmed && !IsWorking;

    partial void OnSelectedDriveChanged(DriveItemViewModel? value)
    {
        IsConfirmed = false;
        HasSelection = value is not null;
        ConfirmText = value is null ? string.Empty : _localizer.Format("Drive_ConfirmCheckbox", value.Name, value.Size);
    }

    private void ShowError(Exception ex)
    {
        var description = _errors.Describe(ex);
        ErrorTitle = description.Title;
        ErrorMessage = $"{description.Message}\n{description.Details}";
        HasError = true;
    }
}
