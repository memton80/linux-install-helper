using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Workflow;
using LinuxInstallHelper.Core.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>What a problem of the troubleshooting page offers to do.</summary>
public enum TroubleshootAction
{
    None,
    RestartToDrive,
    OpenFirmwareSettings,
    OpenReadiness,
}

/// <summary>A problem met when starting or installing Linux, and its solutions. Its texts are Trouble_{Key}_Title and _Body.</summary>
public sealed class TroubleshootProblemViewModel
{
    public TroubleshootProblemViewModel(string key, TroubleshootAction action, ILocalizer localizer, Func<TroubleshootAction, Task> act)
    {
        Title = localizer.Get($"Trouble_{key}_Title");
        Body = localizer.Get($"Trouble_{key}_Body");
        HasAction = action != TroubleshootAction.None;
        ActionLabel = HasAction ? localizer.Get($"Trouble_Action_{action}") : string.Empty;
        ActionCommand = new AsyncRelayCommand(() => act(action));
    }

    public string Title { get; }

    public string Body { get; }

    public bool HasAction { get; }

    public string ActionLabel { get; }

    public IAsyncRelayCommand ActionCommand { get; }
}

/// <summary>An image the drive can be compared with.</summary>
public sealed record ImageChoice(string Path, string Label);

/// <summary>
/// "My drive does not start": reads a USB drive back to compare it with an image (read only), then lists the usual problems
/// and their solutions.
/// </summary>
public sealed partial class TroubleshootViewModel : ObservableObject, INavigationAware
{
    private static readonly (string Key, TroubleshootAction Action)[] ProblemList =
    [
        ("NotListed", TroubleshootAction.RestartToDrive),
        ("WindowsStarts", TroubleshootAction.RestartToDrive),
        ("SecureBoot", TroubleshootAction.OpenFirmwareSettings),
        ("BlackScreen", TroubleshootAction.None),
        ("Errors", TroubleshootAction.None),
        ("NoDisk", TroubleshootAction.OpenReadiness),
        ("NoAlongside", TroubleshootAction.OpenReadiness),
        ("NoWifi", TroubleshootAction.OpenReadiness),
    ];

    private readonly DriveScanner _scanner;
    private readonly IDiskService _disks;
    private readonly IRawDiskReader _reader;
    private readonly IFilePickerService _picker;
    private readonly ILocalizer _localizer;
    private readonly DisplayFormatter _formatter;
    private readonly ErrorDescriber _errors;
    private readonly ISettingsStore _settings;
    private readonly AppPaths _paths;
    private readonly INavigationService _navigation;
    private readonly IUiDispatcher _dispatcher;
    private readonly WizardState _wizard;
    private readonly ReadinessActions _actions;
    private readonly ILogger<TroubleshootViewModel> _logger;
    private CancellationTokenSource? _verification;

    public TroubleshootViewModel(
        DriveScanner scanner,
        IDiskService disks,
        IRawDiskReader reader,
        IFilePickerService picker,
        ILocalizer localizer,
        DisplayFormatter formatter,
        ErrorDescriber errors,
        ISettingsStore settings,
        AppPaths paths,
        INavigationService navigation,
        IUiDispatcher dispatcher,
        WizardState wizard,
        ReadinessActions actions,
        ILogger<TroubleshootViewModel> logger)
    {
        _scanner = scanner;
        _disks = disks;
        _reader = reader;
        _picker = picker;
        _localizer = localizer;
        _formatter = formatter;
        _errors = errors;
        _settings = settings;
        _paths = paths;
        _navigation = navigation;
        _dispatcher = dispatcher;
        _wizard = wizard;
        _actions = actions;
        _logger = logger;

        Problems = ProblemList.Select(p => new TroubleshootProblemViewModel(p.Key, p.Action, localizer, ActAsync)).ToList();
    }

    public IReadOnlyList<TroubleshootProblemViewModel> Problems { get; }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = [];

    public ObservableCollection<ImageChoice> Images { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private int _driveIndex = -1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private int _imageIndex = -1;

    [ObservableProperty]
    private bool _hasNoDrives;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isVerifying;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private InfoBarSeverity _resultSeverity;

    [ObservableProperty]
    private string _resultTitle = string.Empty;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public void OnNavigatedTo(object? parameter)
    {
        LoadImages();
        _ = RefreshAsync();
    }

    public void OnNavigatedFrom() => _verification?.Cancel();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsVerifying)
        {
            return;
        }

        try
        {
            var selected = DriveIndex >= 0 && DriveIndex < Drives.Count ? Drives[DriveIndex].Disk : null;
            var scan = await _scanner.ScanAsync(0, [_settings.Current.DownloadFolder ?? _paths.DefaultDownloads, _paths.Root]);
            Drives.Clear();
            foreach (var disk in scan.Eligible)
            {
                Drives.Add(new DriveItemViewModel(disk, _formatter, _localizer));
            }

            var index = selected is null ? -1 : Drives.ToList().FindIndex(d => d.IsSameDrive(selected));
            DriveIndex = index >= 0 ? index : Drives.Count == 1 ? 0 : -1;
            HasNoDrives = Drives.Count == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not list the drives");
            ShowResult(InfoBarSeverity.Error, _errors.Describe(ex));
        }
    }

    [RelayCommand]
    private void Browse()
    {
        var path = _picker.PickIsoFile(_localizer.Get("LocalIso_PickerTitle"), _localizer.Get("LocalIso_PickerFilter"));
        if (path is null)
        {
            return;
        }

        var index = Images.ToList().FindIndex(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Images.Insert(0, Describe(path));
            index = 0;
        }

        ImageIndex = index;
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        var drive = Drives[DriveIndex].Disk;
        var image = Images[ImageIndex].Path;
        HasResult = false;
        IsVerifying = true;
        Progress = 0;
        ProgressText = string.Empty;
        // Leaving the page cancels the comparison: a creation never reads and writes the same drive at the same time.
        var cancellation = new CancellationTokenSource();
        _verification = cancellation;
        var token = cancellation.Token;
        try
        {
            // Read only, but the drive must still be the one chosen: disk numbers are reused when drives are swapped.
            var current = (await _disks.GetDisksAsync(token)).FirstOrDefault(d => d.Number == drive.Number);
            if (TargetGuard.Compare(drive, current) != TargetCheck.Same)
            {
                ShowResult(InfoBarSeverity.Warning, _localizer.Get("Verify_DriveChanged_Title"), _localizer.Get("Verify_DriveChanged"));
                await RefreshAsync();
                return;
            }

            var length = new FileInfo(image).Length;
            var progress = new InlineProgress<UsbWriteProgress>(p => _dispatcher.Post(() => OnProgress(p)));
            _logger.LogInformation("Comparing disk {Number} with {Image}", drive.Number, image);
            var result = await Task.Run(
                () =>
                {
                    using var device = _reader.OpenForReading(drive);
                    using var stream = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                    return new DriveImageComparer().Compare(stream, length, device, progress, token);
                },
                token);
            _logger.LogInformation("Comparison of disk {Number}: {Result}", drive.Number, result);
            ShowComparison(result);
        }
        catch (OperationCanceledException)
        {
            HasResult = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not compare the drive with the image");
            ShowResult(InfoBarSeverity.Error, _errors.Describe(ex));
        }
        finally
        {
            IsVerifying = false;
            _verification = null;
            cancellation.Dispose();
        }
    }

    private bool CanVerify() =>
        !IsVerifying && DriveIndex >= 0 && DriveIndex < Drives.Count && ImageIndex >= 0 && ImageIndex < Images.Count;

    [RelayCommand(CanExecute = nameof(IsVerifying))]
    private void Cancel() => _verification?.Cancel();

    private void LoadImages()
    {
        var paths = new List<string>();
        if (_wizard.LastResult?.ImagePath is { } last)
        {
            paths.Add(last);
        }

        if (_wizard.LocalIsoPath is { } local)
        {
            paths.Add(local);
        }

        try
        {
            paths.AddRange(StoredImages.List(_settings.Current.DownloadFolder ?? _paths.DefaultDownloads).Where(i => !i.IsPartial).Select(i => i.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list the images of the download folder");
        }

        Images.Clear();
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Images.Add(Describe(path));
        }

        ImageIndex = Images.Count > 0 ? 0 : -1;
    }

    private ImageChoice Describe(string path)
    {
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            size = 0;
        }

        return new ImageChoice(path, $"{Path.GetFileName(path)} · {_formatter.Size(size)}");
    }

    private void OnProgress(UsbWriteProgress progress)
    {
        Progress = progress.Fraction * 100;
        var parts = new List<string> { _formatter.Percent(progress.Fraction), _formatter.Speed(progress.BytesPerSecond) };
        if (progress.Remaining is TimeSpan remaining && remaining > TimeSpan.Zero)
        {
            parts.Add(_localizer.Format("Progress_Remaining", _formatter.Duration(remaining)));
        }

        ProgressText = string.Join(" · ", parts);
    }

    private void ShowComparison(DriveComparison result)
    {
        var at = result.FirstDifference is { } offset ? _formatter.Size(offset) : string.Empty;
        var (severity, key) = result.Outcome switch
        {
            DriveComparisonOutcome.Identical => (InfoBarSeverity.Success, "Verify_Identical"),
            DriveComparisonOutcome.NearlyIdentical => (InfoBarSeverity.Success, "Verify_NearlyIdentical"),
            DriveComparisonOutcome.DriveTooSmall => (InfoBarSeverity.Error, "Verify_TooSmall"),
            _ => (InfoBarSeverity.Error, "Verify_Different"),
        };

        ShowResult(severity, _localizer.Get(key + "_Title"), _localizer.Format(key, at, result.DifferentSectors));
    }

    private void ShowResult(InfoBarSeverity severity, ErrorDescription error) =>
        ShowResult(severity, error.Title, $"{error.Message}\n{error.Details}");

    private void ShowResult(InfoBarSeverity severity, string title, string message)
    {
        ResultSeverity = severity;
        ResultTitle = title;
        ResultMessage = message;
        HasResult = true;
    }

    private Task ActAsync(TroubleshootAction action) => action switch
    {
        TroubleshootAction.RestartToDrive => _actions.RestartToDriveAsync(),
        TroubleshootAction.OpenFirmwareSettings => _actions.RestartToFirmwareAsync(),
        TroubleshootAction.OpenReadiness => Task.FromResult(_navigation.NavigateTo(PageKeys.Readiness)),
        _ => Task.CompletedTask,
    };
}
