using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Backup;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A personal folder that holds files, with what it holds and a button to open it.</summary>
public sealed partial class PersonalFolderItemViewModel : ObservableObject
{
    public PersonalFolderItemViewModel(PersonalFolder folder, ILocalizer localizer)
    {
        Name = localizer.Get("Folder_" + folder.Kind);
        FullPath = folder.Path;
        OpenName = localizer.Format("Backup_OpenFolderName", Name);
        Size = localizer.Get("Backup_Measuring");
    }

    public string Name { get; }

    public string FullPath { get; }

    /// <summary>"12.3 GB · 4,512 files", or "Measuring…" until it is known.</summary>
    [ObservableProperty]
    private string _size = string.Empty;

    /// <summary>Accessible name of the open button.</summary>
    public string OpenName { get; }

    [RelayCommand]
    private void Open() => SystemActions.OpenFolder(FullPath);
}

/// <summary>
/// Shown between the progress and the result pages when the user has personal files: asks whether they are backed up
/// on a USB drive or an external disk before Linux is installed.
/// </summary>
public sealed partial class BackupViewModel : ObservableObject, INavigationAware
{
    // Same key as the backup step of the guide's checklist.
    private const string BackupChecklistKey = "backup";

    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly ISettingsStore _settings;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private CancellationTokenSource? _measuring;

    public BackupViewModel(ILocalizer localizer, INavigationService navigation, ISettingsStore settings, WizardState wizard, DisplayFormatter formatter)
    {
        _localizer = localizer;
        _navigation = navigation;
        _settings = settings;
        _wizard = wizard;
        _formatter = formatter;
    }

    public ObservableCollection<PersonalFolderItemViewModel> Folders { get; } = [];

    /// <summary>"About 38.2 GB to back up", empty until every folder is measured.</summary>
    [ObservableProperty]
    private string _total = string.Empty;

    public void OnNavigatedTo(object? parameter)
    {
        if (_wizard.LastResult is null || _wizard.Target is null)
        {
            _navigation.NavigateTo(PageKeys.Distros, clearHistory: true);
            return;
        }

        if (parameter is not IReadOnlyList<PersonalFolder> { Count: > 0 } folders)
        {
            _navigation.NavigateTo(PageKeys.Done);
            return;
        }

        Folders.Clear();
        foreach (var folder in folders)
        {
            Folders.Add(new PersonalFolderItemViewModel(folder, _localizer));
        }

        _ = MeasureAsync(_wizard.LastResult.ImagePath);
    }

    public void OnNavigatedFrom() => _measuring?.Cancel();

    /// <summary>Sizes the folders one by one, so that the user knows how large the backup drive must be.</summary>
    private async Task MeasureAsync(string imagePath)
    {
        var cancellation = new CancellationTokenSource();
        _measuring = cancellation;
        long total = 0;
        try
        {
            foreach (var folder in Folders.ToList())
            {
                var size = await Task.Run(() => PersonalFolders.Measure(folder.FullPath, [imagePath], cancellation.Token), cancellation.Token);
                folder.Size = _localizer.Format("Backup_FolderSize", _formatter.Size(size.Bytes), size.Files);
                total += size.Bytes;
            }

            Total = _localizer.Format("Backup_Total", _formatter.Size(total));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _measuring = null;
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        _wizard.BackupPending = false;
        var settings = _settings.Current.WithChecklistItemDone(BackupChecklistKey);
        if (settings != _settings.Current)
        {
            _settings.Save(settings);
        }

        _navigation.NavigateTo(PageKeys.Done);
    }

    [RelayCommand]
    private void Later()
    {
        _wizard.BackupPending = true;
        _navigation.NavigateTo(PageKeys.Done);
    }
}
