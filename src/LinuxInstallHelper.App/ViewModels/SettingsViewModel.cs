using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Images;
using LinuxInstallHelper.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>An image kept in the download folder, with a button to delete it.</summary>
public sealed class StoredImageViewModel
{
    public StoredImageViewModel(StoredImage image, ILocalizer localizer, DisplayFormatter formatter, Func<StoredImageViewModel, Task> delete)
    {
        Image = image;
        Name = image.Name;
        var date = image.LastWrite.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
        Details = image.IsPartial
            ? localizer.Format("Settings_ImagePartial", formatter.Size(image.Size), date)
            : localizer.Format("Settings_ImageDetails", formatter.Size(image.Size), date);
        DeleteLabel = localizer.Get("Settings_ImageDelete");
        DeleteName = localizer.Format("Settings_ImageDeleteName", image.Name);
        DeleteCommand = new AsyncRelayCommand(() => delete(this));
    }

    public StoredImage Image { get; }

    public string Name { get; }

    public string Details { get; }

    public string DeleteLabel { get; }

    /// <summary>Accessible name of the delete button.</summary>
    public string DeleteName { get; }

    public IAsyncRelayCommand DeleteCommand { get; }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string?[] Languages = [null, "en-US", "fr-FR"];

    private readonly ISettingsStore _store;
    private readonly IThemeService _theme;
    private readonly IFilePickerService _picker;
    private readonly ILocalizer _localizer;
    private readonly AppPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly DisplayFormatter _formatter;
    private readonly AppBusyState _busy;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly string? _startupLanguage;
    private bool _loading = true;

    public SettingsViewModel(
        ISettingsStore store,
        IThemeService theme,
        IFilePickerService picker,
        ILocalizer localizer,
        AppPaths paths,
        IDialogService dialogs,
        DisplayFormatter formatter,
        AppBusyState busy,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _theme = theme;
        _picker = picker;
        _localizer = localizer;
        _paths = paths;
        _dialogs = dialogs;
        _formatter = formatter;
        _busy = busy;
        _logger = logger;

        var settings = store.Current;
        _startupLanguage = settings.Language;
        ThemeIndex = (int)settings.Theme;
        LanguageIndex = Math.Max(0, Array.IndexOf(Languages, settings.Language));
        KeepIso = settings.KeepIsoAfterWrite;
        VerifyAfterWrite = settings.VerifyAfterWrite;
        EjectWhenDone = settings.EjectWhenDone;
        CheckForUpdates = settings.CheckForUpdates;
        UpdateFolder();
        _loading = false;
    }

    /// <summary>The images kept in the download folder, newest first.</summary>
    public ObservableCollection<StoredImageViewModel> Images { get; } = [];

    /// <summary>"3 images, 12.4 GB", or that there is none.</summary>
    [ObservableProperty]
    private string _imagesSummary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteAllImagesCommand))]
    private bool _hasImages;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private bool _restartRequired;

    [ObservableProperty]
    private string _downloadFolder = string.Empty;

    [ObservableProperty]
    private bool _isCustomFolder;

    [ObservableProperty]
    private bool _keepIso;

    [ObservableProperty]
    private bool _verifyAfterWrite;

    [ObservableProperty]
    private bool _ejectWhenDone;

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        var theme = (AppTheme)Math.Clamp(value, 0, 2);
        Save(_store.Current with { Theme = theme });
        _theme.Apply(theme);
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        var language = Languages[Math.Clamp(value, 0, Languages.Length - 1)];
        Save(_store.Current with { Language = language });
        RestartRequired = language != _startupLanguage;
    }

    partial void OnKeepIsoChanged(bool value) => Save(_store.Current with { KeepIsoAfterWrite = value });

    partial void OnVerifyAfterWriteChanged(bool value) => Save(_store.Current with { VerifyAfterWrite = value });

    partial void OnEjectWhenDoneChanged(bool value) => Save(_store.Current with { EjectWhenDone = value });

    partial void OnCheckForUpdatesChanged(bool value) => Save(_store.Current with { CheckForUpdates = value });

    [RelayCommand(CanExecute = nameof(HasImages))]
    private async Task DeleteAllImagesAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Settings_DeleteAllTitle"),
            _localizer.Format("Settings_DeleteAllMessage", Images.Count, _formatter.Size(Images.Sum(i => i.Image.Size))),
            _localizer.Get("Settings_DeleteAllPrimary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);
        if (confirmed)
        {
            Delete(Images.Select(i => i.Image).ToList());
        }
    }

    private async Task DeleteImageAsync(StoredImageViewModel image)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Settings_DeleteTitle"),
            _localizer.Format("Settings_DeleteMessage", image.Name, _formatter.Size(image.Image.Size)),
            _localizer.Get("Settings_DeletePrimary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);
        if (confirmed)
        {
            Delete([image.Image]);
        }
    }

    private void Delete(IReadOnlyList<StoredImage> images)
    {
        // A drive being created may be reading one of them.
        if (_busy.IsBusy)
        {
            return;
        }

        foreach (var image in images)
        {
            try
            {
                StoredImages.Delete(image);
                _logger.LogInformation("Deleted {Image}", image.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete {Image}", image.Path);
            }
        }

        LoadImages();
    }

    private void LoadImages()
    {
        Images.Clear();
        IReadOnlyList<StoredImage> images;
        try
        {
            images = StoredImages.List(DownloadFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list the images of {Folder}", DownloadFolder);
            images = [];
        }

        foreach (var image in images)
        {
            Images.Add(new StoredImageViewModel(image, _localizer, _formatter, DeleteImageAsync));
        }

        HasImages = Images.Count > 0;
        ImagesSummary = HasImages
            ? _localizer.Format("Settings_ImagesSummary", Images.Count, _formatter.Size(images.Sum(i => i.Size)))
            : _localizer.Get("Settings_NoImages");
    }

    [RelayCommand]
    private void ChangeFolder()
    {
        var folder = _picker.PickFolder(_localizer.Get("Settings_PickFolderTitle"), DownloadFolder);
        if (folder is not null)
        {
            Save(_store.Current with { DownloadFolder = folder });
            UpdateFolder();
        }
    }

    [RelayCommand]
    private void ResetFolder()
    {
        Save(_store.Current with { DownloadFolder = null });
        UpdateFolder();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(DownloadFolder);
        SystemActions.OpenFolder(DownloadFolder);
    }

    [RelayCommand]
    private void OpenLogs() => SystemActions.OpenFolder(_paths.Logs);

    [RelayCommand]
    private void Restart()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            App.MainWindow.Close();
        }
    }

    private void UpdateFolder()
    {
        DownloadFolder = _store.Current.DownloadFolder ?? _paths.DefaultDownloads;
        IsCustomFolder = _store.Current.DownloadFolder is not null;
        LoadImages();
    }

    private void Save(UserSettings settings)
    {
        if (!_loading)
        {
            _store.Save(settings);
        }
    }
}
