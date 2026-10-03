using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Verification;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class LocalIsoViewModel : ObservableObject
{
    private readonly IFilePickerService _picker;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private readonly IUiDispatcher _dispatcher;
    private CancellationTokenSource? _hashing;

    public LocalIsoViewModel(IFilePickerService picker, ILocalizer localizer, INavigationService navigation, WizardState wizard, DisplayFormatter formatter, IUiDispatcher dispatcher)
    {
        _picker = picker;
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _formatter = formatter;
        _dispatcher = dispatcher;
    }

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _hasFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isUsable;

    [ObservableProperty]
    private string _fileDetails = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private string _statusTitle = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _expectedSha256 = string.Empty;

    [ObservableProperty]
    private bool _isExpectedHashInvalid;

    [ObservableProperty]
    private string? _computedSha256;

    [ObservableProperty]
    private bool _isHashing;

    [ObservableProperty]
    private double _hashProgress;

    [ObservableProperty]
    private bool _hashMatches;

    [ObservableProperty]
    private bool _hashDiffers;

    private long _size;

    [RelayCommand]
    private void Pick()
    {
        var path = _picker.PickIsoFile(_localizer.Get("LocalIso_PickerTitle"), _localizer.Get("LocalIso_PickerFilter"));
        if (path is null)
        {
            return;
        }

        _hashing?.Cancel();
        FilePath = path;
        HasFile = true;
        ComputedSha256 = null;
        UpdateHashComparison();

        try
        {
            var info = IsoInspector.Inspect(path);
            _size = info.Size;
            FileDetails = info.VolumeLabel is null
                ? _formatter.Size(info.Size)
                : $"{info.VolumeLabel} · {_formatter.Size(info.Size)}";

            (StatusSeverity, StatusTitle, StatusMessage, IsUsable) = info switch
            {
                { IsIso9660: false } => (InfoBarSeverity.Error, _localizer.Get("LocalIso_NotIso_Title"), _localizer.Get("LocalIso_NotIso"), false),
                { IsHybrid: false } => (InfoBarSeverity.Warning, _localizer.Get("LocalIso_NotHybrid_Title"), _localizer.Get("LocalIso_NotHybrid"), true),
                _ => (InfoBarSeverity.Success, _localizer.Get("LocalIso_Hybrid_Title"), _localizer.Get("LocalIso_Hybrid"), true),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (StatusSeverity, StatusTitle, StatusMessage, IsUsable) = (InfoBarSeverity.Error, _localizer.Get("LocalIso_Unreadable_Title"), ex.Message, false);
            FileDetails = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ComputeHashAsync()
    {
        if (FilePath is null || IsHashing)
        {
            return;
        }

        _hashing = new CancellationTokenSource();
        IsHashing = true;
        HashProgress = 0;
        var size = Math.Max(1, _size);
        try
        {
            var path = FilePath;
            var progress = new Core.Workflow.InlineProgress<long>(read => _dispatcher.Post(() => HashProgress = 100d * read / size));
            ComputedSha256 = await Task.Run(() => FileHasher.ComputeAsync(path, HashAlgorithmKind.Sha256, progress, _hashing.Token));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusSeverity = InfoBarSeverity.Error;
            StatusTitle = _localizer.Get("LocalIso_Unreadable_Title");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsHashing = false;
        }

        UpdateHashComparison();
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _wizard.SelectLocalIso(FilePath!, _size, ExpectedSha256);
        _navigation.NavigateTo(PageKeys.Drive);
    }

    private bool CanContinue() => IsUsable && !IsExpectedHashInvalid;

    partial void OnExpectedSha256Changed(string value)
    {
        var trimmed = value.Trim();
        IsExpectedHashInvalid = trimmed.Length > 0 && !Sha256Regex().IsMatch(trimmed);
        ContinueCommand.NotifyCanExecuteChanged();
        UpdateHashComparison();
    }

    private void UpdateHashComparison()
    {
        var expected = ExpectedSha256.Trim();
        var known = ComputedSha256 is not null && expected.Length == 64 && !IsExpectedHashInvalid;
        HashMatches = known && FileHasher.HashEquals(expected, ComputedSha256!);
        HashDiffers = known && !HashMatches;
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();
}
