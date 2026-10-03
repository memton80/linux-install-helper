using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Settings;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>One step of the "before leaving Windows" checklist. Its texts are Check_{Key}_Title and Check_{Key}_Body.</summary>
public sealed class ChecklistItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isDone;

    public ChecklistItemViewModel(string key, string title, string body, bool isDone, Action changed)
    {
        Key = key;
        Title = title;
        Body = body;
        _isDone = isDone;
        _changed = changed;
    }

    public string Key { get; }

    public string Title { get; }

    public string Body { get; }

    public bool IsDone
    {
        get => _isDone;
        set
        {
            if (SetProperty(ref _isDone, value))
            {
                _changed();
            }
        }
    }
}

/// <summary>The Linux guide page: what to do before leaving Windows, then the lessons of the Linux tour.</summary>
public sealed class GuideViewModel : ObservableObject
{
    private static readonly string[] ChecklistKeys =
        ["backup", "passwords", "licenses", "software", "games", "mail", "bitlocker", "hardware", "dualboot", "power"];

    private readonly ILocalizer _localizer;
    private readonly ISettingsStore _settings;
    private string _progress = string.Empty;

    public GuideViewModel(ILocalizer localizer, ISettingsStore settings)
    {
        _localizer = localizer;
        _settings = settings;
        Lessons = new LinuxTourViewModel(localizer).Lessons;

        var done = (settings.Current.ChecklistDone ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var key in ChecklistKeys)
        {
            Checklist.Add(new ChecklistItemViewModel(
                key,
                localizer.Get($"Check_{key}_Title"),
                localizer.Get($"Check_{key}_Body"),
                done.Contains(key, StringComparer.Ordinal),
                OnChecklistChanged));
        }

        UpdateProgress();
    }

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; } = [];

    public IReadOnlyList<LinuxLesson> Lessons { get; }

    /// <summary>"3 of 10 done".</summary>
    public string Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    private void OnChecklistChanged()
    {
        var done = string.Join(',', Checklist.Where(item => item.IsDone).Select(item => item.Key));
        _settings.Save(_settings.Current with { ChecklistDone = done.Length == 0 ? null : done });
        UpdateProgress();
    }

    private void UpdateProgress() =>
        Progress = _localizer.Format("Guide_ChecklistProgress", Checklist.Count(item => item.IsDone), Checklist.Count);
}
