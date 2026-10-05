using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Backup;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Tour;
using LinuxInstallHelper.Core.Workflow;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class ProgressViewModel : ObservableObject, INavigationAware
{
    private readonly ICreationPipeline _pipeline;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private readonly ErrorDescriber _errors;
    private readonly ISettingsStore _settings;
    private readonly AppPaths _paths;
    private readonly AppBusyState _busy;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ProgressViewModel> _logger;

    public ProgressViewModel(
        ICreationPipeline pipeline,
        ILocalizer localizer,
        INavigationService navigation,
        IDialogService dialogs,
        WizardState wizard,
        DisplayFormatter formatter,
        ErrorDescriber errors,
        ISettingsStore settings,
        AppPaths paths,
        AppBusyState busy,
        IUiDispatcher dispatcher,
        TourBook tours,
        ILogger<ProgressViewModel> logger)
    {
        _pipeline = pipeline;
        _localizer = localizer;
        _navigation = navigation;
        _dialogs = dialogs;
        _wizard = wizard;
        _formatter = formatter;
        _errors = errors;
        _settings = settings;
        _paths = paths;
        _busy = busy;
        _dispatcher = dispatcher;
        _logger = logger;

        Tour = new LinuxTourViewModel(localizer, tours, dispatcher);
        foreach (var stage in Enum.GetValues<CreationStage>())
        {
            Stages.Add(new StageItemViewModel(stage, localizer.Get("Stage_" + stage)));
        }
    }

    public ObservableCollection<StageItemViewModel> Stages { get; } = [];

    public ObservableCollection<LogItemViewModel> Logs { get; } = [];

    /// <summary>First steps with the distribution being written, to read while waiting.</summary>
    public LinuxTourViewModel Tour { get; }

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _currentStep = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _hasFailed;

    [ObservableProperty]
    private bool _wasCancelled;

    [ObservableProperty]
    private string _errorTitle = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _errorDetails = string.Empty;

    [ObservableProperty]
    private bool _driveMayBeUnusable;

    public bool CanLeave => !IsRunning;

    public void OnNavigatedTo(object? parameter)
    {
        if (_wizard.Source is null || _wizard.Target is null)
        {
            _navigation.NavigateTo(PageKeys.Distros, clearHistory: true);
            return;
        }

        // Only right after the confirmation on the drive page: showing this page again never writes the drive again.
        if (!_wizard.TakeCreationConfirmation())
        {
            _navigation.NavigateTo(_wizard.LastResult is null ? PageKeys.Distros : PageKeys.Done, clearHistory: true);
            return;
        }

        Subtitle = _localizer.Format("Progress_Subtitle", _wizard.SourceName, _wizard.Target.FriendlyName);
        Tour.Use(_wizard.Distro?.Id, _wizard.Distro?.Name);
        Tour.Play();
        _ = RunAsync();
    }

    public void OnNavigatedFrom() => Tour.Pause();

    [RelayCommand]
    private Task RetryAsync() => RunAsync();

    [RelayCommand]
    private void Back() => _navigation.GoBack();

    [RelayCommand]
    private void OpenLogs() => SystemActions.OpenFolder(_paths.Logs);

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task CancelAsync()
    {
        if (_busy.IsWriting)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                _localizer.Get("Progress_CancelWriteTitle"),
                _localizer.Get("Progress_CancelWriteMessage"),
                _localizer.Get("Progress_CancelWritePrimary"),
                _localizer.Get("Progress_CancelWriteClose"),
                destructive: true);
            if (!confirmed)
            {
                return;
            }
        }

        _busy.CurrentOperation?.Cancel();
    }

    private async Task RunAsync()
    {
        if (IsRunning)
        {
            return;
        }

        Reset();
        IsRunning = true;
        var cancellation = new CancellationTokenSource();
        _busy.CurrentOperation = cancellation;
        _busy.IsBusy = true;
        _navigation.IsLocked = true;

        var settings = _settings.Current;
        var job = new CreationJob(_wizard.Source!, _wizard.Target!, settings, settings.DownloadFolder ?? _paths.DefaultDownloads);
        var progress = new InlineProgress<CreationProgress>(p => _dispatcher.Post(() => OnProgress(p)));
        void Log(CreationLogEntry entry) => _dispatcher.Post(() => Logs.Add(new LogItemViewModel(entry, _localizer, _formatter)));

        try
        {
            var result = await Task.Run(() => _pipeline.RunAsync(job, progress, Log, cancellation.Token));
            _wizard.LastResult = result;
            _wizard.BackupPending = false;
            var personalFolders = await FindPersonalFoldersAsync(result);
            Unlock();

            // Ask about a backup first when the user has personal files: installing Linux may erase them.
            if (personalFolders.Count > 0)
            {
                _navigation.NavigateTo(PageKeys.Backup, personalFolders);
            }
            else
            {
                _navigation.NavigateTo(PageKeys.Done);
            }
        }
        catch (OperationCanceledException)
        {
            var wasWriting = _busy.IsWriting;
            Unlock();
            WasCancelled = true;
            DriveMayBeUnusable = wasWriting;
            MarkRunningStage(StageState.Failed);
            Logs.Add(new LogItemViewModel(_localizer.Get("Log_Cancelled"), _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creation failed");
            var wasWriting = _busy.IsWriting;
            Unlock();
            var description = _errors.Describe(ex);
            ErrorTitle = description.Title;
            ErrorMessage = description.Message;
            ErrorDetails = description.Details;
            HasFailed = true;
            // Nothing is written when the target is refused up front; otherwise the drive may be half written.
            DriveMayBeUnusable = wasWriting && ex is not UsbWriteException
            {
                Failure: UsbWriteFailure.TargetChanged or UsbWriteFailure.TargetRejected or UsbWriteFailure.ImageTooLarge or UsbWriteFailure.VolumeBusy
                    or UsbWriteFailure.WriteBlocked,
            };
            MarkRunningStage(StageState.Failed);
            Logs.Add(new LogItemViewModel(description.Message, _localizer));
        }
        finally
        {
            cancellation.Dispose();
        }

        void Unlock()
        {
            _busy.CurrentOperation = null;
            _busy.IsWriting = false;
            _busy.IsBusy = false;
            _navigation.IsLocked = false;
            IsRunning = false;
        }
    }

    private async Task<IReadOnlyList<PersonalFolder>> FindPersonalFoldersAsync(CreationResult result)
    {
        try
        {
            return await Task.Run(() => PersonalFolders.WithFiles(PersonalFolders.ForCurrentUser(), [result.ImagePath]));
        }
        catch (Exception ex)
        {
            // The drive is ready: never turn a success into an error for this reminder.
            _logger.LogWarning(ex, "Could not look for personal files");
            return [];
        }
    }

    private void Reset()
    {
        foreach (var stage in Stages)
        {
            stage.State = StageState.Pending;
            stage.Detail = string.Empty;
        }

        Logs.Clear();
        HasFailed = false;
        WasCancelled = false;
        DriveMayBeUnusable = false;
        ProgressValue = 0;
        IsIndeterminate = true;
        ProgressText = string.Empty;
        CurrentStep = string.Empty;
    }

    private void OnProgress(CreationProgress progress)
    {
        var stage = Stages.First(s => s.Stage == progress.Stage);
        stage.State = progress.State;

        // Every step before a running one is finished (or was skipped).
        if (progress.State == StageState.Running)
        {
            foreach (var previous in Stages.TakeWhile(s => s.Stage != progress.Stage).Where(s => s.State == StageState.Pending))
            {
                previous.State = StageState.Skipped;
            }

            _busy.IsWriting = progress.Stage == CreationStage.Write;
            CurrentStep = progress.Stage == CreationStage.Write && progress.WriteStage == UsbWriteStage.Verifying
                ? _localizer.Get("Stage_WriteVerifying")
                : stage.Label;
        }

        if (progress.State != StageState.Running)
        {
            if (progress.State == StageState.Done && progress.Stage == CreationStage.Write)
            {
                _busy.IsWriting = false;
            }

            return;
        }

        if (progress.Fraction is double fraction)
        {
            IsIndeterminate = false;
            ProgressValue = fraction * 100;
            var parts = new List<string> { _formatter.Percent(fraction) };
            if (progress.Total > 0)
            {
                parts.Add(_localizer.Format("Progress_Bytes", _formatter.Size(progress.Bytes), _formatter.Size(progress.Total)));
            }

            if (progress.BytesPerSecond > 0)
            {
                parts.Add(_formatter.Speed(progress.BytesPerSecond));
            }

            if (progress.Remaining is TimeSpan remaining && remaining > TimeSpan.Zero)
            {
                parts.Add(_localizer.Format("Progress_Remaining", _formatter.Duration(remaining)));
            }

            ProgressText = string.Join(" · ", parts);
            stage.Detail = _formatter.Percent(fraction);
        }
        else
        {
            IsIndeterminate = true;
            ProgressText = string.Empty;
        }
    }

    private void MarkRunningStage(StageState state)
    {
        foreach (var stage in Stages.Where(s => s.State == StageState.Running))
        {
            stage.State = state;
        }

        IsIndeterminate = false;
    }
}
