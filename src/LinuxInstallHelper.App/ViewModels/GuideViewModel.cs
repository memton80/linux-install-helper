using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Readiness;
using LinuxInstallHelper.Core.Settings;
using LinuxInstallHelper.Core.Tour;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>One step of the "before leaving Windows" checklist. Its texts are Check_{Key}_Title and Check_{Key}_Body.</summary>
public sealed partial class ChecklistItemViewModel : ObservableObject
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

    /// <summary>What this computer says about the step ("BitLocker is on"), empty when nothing.</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>Label of the button that helps with the step, empty when there is none.</summary>
    [ObservableProperty]
    private string _actionLabel = string.Empty;

    [ObservableProperty]
    private ICommand? _actionCommand;

    public void SetAction(string label, ICommand command)
    {
        ActionLabel = label;
        ActionCommand = command;
    }

    public void ClearAction()
    {
        ActionLabel = string.Empty;
        ActionCommand = null;
    }

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
        ["backup", "passwords", "wifi", "licenses", "software", "games", "mail", "bitlocker", "hardware", "dualboot", "power"];

    private readonly ILocalizer _localizer;
    private readonly ISettingsStore _settings;
    private readonly TourBook _tours;
    private readonly ICatalogService _catalog;
    private readonly WizardState _wizard;
    private readonly PcInfo _pc;
    private readonly ReadinessActions _actions;
    private readonly INavigationService _navigation;
    private readonly DisplayFormatter _formatter;
    private string _progress = string.Empty;
    private IReadOnlyList<LinuxLesson> _lessons;
    private bool _hasDistros;

    public GuideViewModel(
        ILocalizer localizer,
        ISettingsStore settings,
        TourBook tours,
        ICatalogService catalog,
        WizardState wizard,
        PcInfo pc,
        ReadinessActions actions,
        INavigationService navigation,
        DisplayFormatter formatter)
    {
        _localizer = localizer;
        _settings = settings;
        _tours = tours;
        _catalog = catalog;
        _wizard = wizard;
        _pc = pc;
        _actions = actions;
        _navigation = navigation;
        _formatter = formatter;
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

        Step("wifi").SetAction(localizer.Get("Guide_Action_Wifi"), new RelayCommand(() => _navigation.NavigateTo(PageKeys.Readiness)));
        Step("software").SetAction(localizer.Get("Guide_Action_Software"), new RelayCommand(() => _navigation.NavigateTo(PageKeys.Software)));

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

        _ = ShowComputerAsync(refresh: false);
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

    private ChecklistItemViewModel Step(string key) => Checklist.First(item => item.Key == key);

    /// <summary>Adds to the steps what this computer says about them: BitLocker, fast startup, free space, laptop.</summary>
    private async Task ShowComputerAsync(bool refresh)
    {
        var facts = await (refresh ? _pc.RefreshAsync() : _pc.GetAsync());

        var bitlocker = Step("bitlocker");
        bitlocker.Note = facts.SystemDriveEncrypted switch
        {
            true => _localizer.Get("Check_bitlocker_On"),
            false => _localizer.Get("Check_bitlocker_Off"),
            null => string.Empty,
        };
        if (facts.SystemDriveEncrypted == true)
        {
            bitlocker.SetAction(_localizer.Get("Ready_Action_OpenRecoveryKeyPage"), new RelayCommand(() => SystemActions.OpenUrl(ReadinessActions.RecoveryKeyUrl)));
        }

        var dualboot = Step("dualboot");
        var notes = new List<string>();
        if (facts.SystemDriveFreeBytes is { } free)
        {
            notes.Add(_localizer.Format("Check_dualboot_Free", _formatter.Size(free)));
        }

        if (facts.FastStartupEnabled is { } fastStartup)
        {
            notes.Add(_localizer.Get(fastStartup ? "Check_dualboot_FastStartupOn" : "Check_dualboot_FastStartupOff"));
        }

        dualboot.Note = string.Join(" ", notes);
        if (facts.FastStartupEnabled == true)
        {
            dualboot.SetAction(_localizer.Get("Ready_Action_DisableFastStartup"), new AsyncRelayCommand(DisableFastStartupAsync));
        }
        else
        {
            dualboot.ClearAction();
        }

        Step("power").Note = facts.IsLaptop == true ? _localizer.Get("Check_power_Laptop") : string.Empty;
    }

    private async Task DisableFastStartupAsync()
    {
        if (await _actions.RunAsync(ReadinessAction.DisableFastStartup))
        {
            await ShowComputerAsync(refresh: true);
        }
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
