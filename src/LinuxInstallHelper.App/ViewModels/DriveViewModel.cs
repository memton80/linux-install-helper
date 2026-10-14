using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class DriveViewModel : ObservableObject, INavigationAware
{
    private readonly DriveScanner _scanner;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private readonly ISettingsStore _settings;
    private readonly AppPaths _paths;
    private readonly ILogger<DriveViewModel> _logger;
    private readonly DispatcherQueueTimer _timer;
    private bool _refreshing;

    public DriveViewModel(
        DriveScanner scanner,
        ILocalizer localizer,
        INavigationService navigation,
        IDialogService dialogs,
        WizardState wizard,
        DisplayFormatter formatter,
        ISettingsStore settings,
        AppPaths paths,
        GuidanceState guidance,
        ILogger<DriveViewModel> logger)
    {
        _scanner = scanner;
        _localizer = localizer;
        _navigation = navigation;
        _dialogs = dialogs;
        _wizard = wizard;
        _formatter = formatter;
        _settings = settings;
        _paths = paths;
        _logger = logger;
        IsAlone = guidance.IsAlone;

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    /// <summary>The user is on their own: the page says what to check before plugging the drive in.</summary>
    public bool IsAlone { get; }

    /// <summary>"The image takes 5.8 GB: …", shown to a user on their own.</summary>
    [ObservableProperty]
    private string _capacityTip = string.Empty;

    /// <summary>No drive found and the user is on their own: what to try, and a way to the troubleshooting page.</summary>
    public bool ShowNoDrivesHelp => IsAlone && HasNoDrives;

    [ObservableProperty]
    private string _imageSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private DriveItemViewModel? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isConfirmed;

    [ObservableProperty]
    private string _confirmText = string.Empty;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoDrivesHelp))]
    private bool _hasNoDrives;

    [ObservableProperty]
    private bool _isFirstScan = true;

    [ObservableProperty]
    private bool _hasIgnoredDrives;

    [ObservableProperty]
    private string _ignoredDrives = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Why the drive will not start on this computer: the image is for another kind of processor. Empty when it will start.
    /// It never blocks: the drive may be for another computer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProcessorWarning))]
    private string _processorWarning = string.Empty;

    public bool ShowProcessorWarning => ProcessorWarning.Length > 0;

    public void OnNavigatedTo(object? parameter)
    {
        if (_wizard.Source is null)
        {
            _navigation.NavigateTo(PageKeys.Distros, clearHistory: true);
            return;
        }

        ImageSummary = _localizer.Format("Drive_ImageSummary", _wizard.SourceName, _formatter.Size(_wizard.ImageSize));
        CapacityTip = _localizer.Format("Alone_DriveCapacity", _formatter.Size(_wizard.ImageSize));
        ProcessorWarning = ProcessorText.Mismatch(_localizer, _wizard.ImageArchitecture);
        IsConfirmed = false;
        _ = RefreshAsync();
        _timer.Start();
    }

    public void OnNavigatedFrom() => _timer.Stop();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var protectedPaths = new List<string> { _settings.Current.DownloadFolder ?? _paths.DefaultDownloads, _paths.Root };
            if (_wizard.LocalIsoPath is not null)
            {
                protectedPaths.Add(_wizard.LocalIsoPath);
            }

            var scan = await _scanner.ScanAsync(_wizard.ImageSize, protectedPaths);
            Update(scan);
            HasError = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not list the drives");
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _refreshing = false;
            IsFirstScan = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var drive = SelectedDrive!;

        // Second, explicit confirmation naming the drive (a user on their own also reads what happens next).
        var message = _localizer.Format("Drive_ConfirmMessage", drive.Name, drive.Size, drive.Disk.DisplayLetters, _wizard.SourceName);
        if (IsAlone)
        {
            message += "\n\n" + _localizer.Get("Drive_ConfirmMessage_Alone");
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Drive_ConfirmTitle"),
            message,
            _localizer.Get("Drive_ConfirmPrimary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);

        if (!confirmed || SelectedDrive is null || !SelectedDrive.IsSameDrive(drive.Disk))
        {
            return;
        }

        _wizard.ConfirmCreation(drive.Disk);
        _navigation.NavigateTo(PageKeys.Progress);
    }

    [RelayCommand]
    private void OpenTroubleshoot() => _navigation.NavigateTo(PageKeys.Troubleshoot);

    private bool CanStart() => SelectedDrive is not null && IsConfirmed;

    partial void OnSelectedDriveChanged(DriveItemViewModel? value)
    {
        IsConfirmed = false;
        HasSelection = value is not null;
        ConfirmText = value is null ? string.Empty : _localizer.Format("Drive_ConfirmCheckbox", value.Name, value.Size);
    }

    private void Update(DriveScan scan)
    {
        var selected = SelectedDrive?.Disk;
        var changed = scan.Eligible.Count != Drives.Count
            || scan.Eligible.Where((disk, i) => !Drives[i].IsSameDrive(disk) || Drives[i].Disk.DisplayLetters != disk.DisplayLetters).Any();

        if (changed)
        {
            Drives.Clear();
            foreach (var disk in scan.Eligible)
            {
                Drives.Add(new DriveItemViewModel(disk, _formatter, _localizer));
            }

            // Keep the selection only if it is still exactly the same drive.
            SelectedDrive = selected is null ? null : Drives.FirstOrDefault(d => d.IsSameDrive(selected));
        }

        HasNoDrives = Drives.Count == 0;
        HasIgnoredDrives = scan.Ignored.Count > 0;
        IgnoredDrives = string.Join(Environment.NewLine, scan.Ignored);
    }
}
