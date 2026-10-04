using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Tour;

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

/// <summary>
/// The Linux guide page: what to do before leaving Windows, then the lessons of the Linux tour, for the distribution
/// chosen in the list (by default the one chosen for the drive).
/// </summary>
public sealed partial class GuideViewModel : ObservableObject, INavigationAware
{
    private static readonly string[] ChecklistKeys =
        ["backup", "passwords", "licenses", "software", "games", "mail", "bitlocker", "hardware", "dualboot", "power"];

    private readonly ILocalizer _localizer;
    private readonly ISettingsStore _settings;
    private readonly TourBook _tours;
    private readonly ICatalogService _catalog;
    private readonly WizardState _wizard;
    private string _progress = string.Empty;
    private IReadOnlyList<LinuxLesson> _lessons;
    private bool _hasDistros;

    public GuideViewModel(ILocalizer localizer, ISettingsStore settings, TourBook tours, ICatalogService catalog, WizardState wizard)
    {
        _localizer = localizer;
        _settings = settings;
        _tours = tours;
        _catalog = catalog;
        _wizard = wizard;
        _lessons = LinuxTourViewModel.LessonsFor(tours, null);
        TourOptions.Add(new FilterOption(null, localizer.Get("Guide_TourGeneric")));

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
        if (catalog.Current is { } current)
        {
            AddDistros(current.Catalog.Distros);
        }
    }

    public ObservableCollection<ChecklistItemViewModel> Checklist { get; } = [];

    /// <summary>"Linux in general", then every distribution of the catalog that has its own tour.</summary>
    public ObservableCollection<FilterOption> TourOptions { get; } = [];

    [ObservableProperty]
    private int _tourIndex;

    public IReadOnlyList<LinuxLesson> Lessons
    {
        get => _lessons;
        private set => SetProperty(ref _lessons, value);
    }

    /// <summary>"3 of 10 done".</summary>
    public string Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public void OnNavigatedTo(object? parameter)
    {
        if (!_hasDistros)
        {
            _ = LoadDistrosAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    partial void OnTourIndexChanged(int value)
    {
        if (value >= 0 && value < TourOptions.Count)
        {
            Lessons = LinuxTourViewModel.LessonsFor(_tours, TourOptions[value].Key);
        }
    }

    private async Task LoadDistrosAsync() => AddDistros((_catalog.Current ?? await _catalog.LoadAsync()).Catalog.Distros);

    // The distribution chosen for the drive is selected: its lessons show what the drive will display.
    private void AddDistros(IReadOnlyList<Distro> distros)
    {
        if (_hasDistros)
        {
            return;
        }

        _hasDistros = true;
        foreach (var distro in distros.Where(d => _tours.Has(d.Id)))
        {
            TourOptions.Add(new FilterOption(distro.Id, distro.DisplayName));
        }

        var chosen = TourOptions.ToList().FindIndex(option => option.Key is not null && option.Key == _wizard.Distro?.Id);
        TourIndex = Math.Max(chosen, 0);
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
