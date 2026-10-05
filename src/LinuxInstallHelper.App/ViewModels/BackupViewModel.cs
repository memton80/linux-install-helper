using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Backup;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A personal folder that holds files, with a button to open it.</summary>
public sealed partial class PersonalFolderItemViewModel
{
    public PersonalFolderItemViewModel(PersonalFolder folder, ILocalizer localizer)
    {
        Name = localizer.Get("Folder_" + folder.Kind);
        FullPath = folder.Path;
        OpenName = localizer.Format("Backup_OpenFolderName", Name);
    }

    public string Name { get; }

    public string FullPath { get; }

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

    public BackupViewModel(ILocalizer localizer, INavigationService navigation, ISettingsStore settings, WizardState wizard)
    {
        _localizer = localizer;
        _navigation = navigation;
        _settings = settings;
        _wizard = wizard;
    }

    public ObservableCollection<PersonalFolderItemViewModel> Folders { get; } = [];

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
    }

    public void OnNavigatedFrom()
    {
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
