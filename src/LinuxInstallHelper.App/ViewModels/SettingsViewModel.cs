using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly string?[] Languages = [null, "en-US", "fr-FR"];

    private readonly ISettingsStore _store;
    private readonly IThemeService _theme;
    private readonly IFilePickerService _picker;
    private readonly ILocalizer _localizer;
    private readonly AppPaths _paths;
    private readonly string? _startupLanguage;
    private bool _loading = true;

    public SettingsViewModel(ISettingsStore store, IThemeService theme, IFilePickerService picker, ILocalizer localizer, AppPaths paths)
    {
        _store = store;
        _theme = theme;
        _picker = picker;
        _localizer = localizer;
        _paths = paths;

        var settings = store.Current;
        _startupLanguage = settings.Language;
        ThemeIndex = (int)settings.Theme;
        LanguageIndex = Math.Max(0, Array.IndexOf(Languages, settings.Language));
        KeepIso = settings.KeepIsoAfterWrite;
        VerifyAfterWrite = settings.VerifyAfterWrite;
        EjectWhenDone = settings.EjectWhenDone;
        UpdateFolder();
        _loading = false;
    }

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
    }

    private void Save(UserSettings settings)
    {
        if (!_loading)
        {
            _store.Save(settings);
        }
    }
}
