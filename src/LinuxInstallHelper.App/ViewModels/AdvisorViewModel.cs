using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A question of the questionnaire: its answers are picked by index.</summary>
public sealed class AdvisorQuestionViewModel : ObservableObject
{
    private readonly Action _changed;
    private int _selectedIndex;

    public AdvisorQuestionViewModel(string title, string hint, IReadOnlyList<string> options, int selectedIndex, Action changed)
    {
        Title = title;
        Hint = hint;
        Options = options;
        _selectedIndex = selectedIndex;
        _changed = changed;
    }

    public string Title { get; }

    /// <summary>Extra explanation, may be empty.</summary>
    public string Hint { get; }

    public IReadOnlyList<string> Options { get; }

    /// <summary>-1 while unanswered.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
            {
                _changed();
            }
        }
    }
}

/// <summary>A suggested distribution, explained in plain words.</summary>
public sealed class AdvisorResultViewModel
{
    public AdvisorResultViewModel(DistroItemViewModel item, string explanation, IReadOnlyList<string> reasons, string requirements, string chooseLabel, Action choose)
    {
        Item = item;
        Explanation = explanation;
        Reasons = reasons;
        Requirements = requirements;
        ChooseLabel = chooseLabel;
        ChooseCommand = new RelayCommand(choose);
    }

    public DistroItemViewModel Item { get; }

    public string Explanation { get; }

    public IReadOnlyList<string> Reasons { get; }

    public string Requirements { get; }

    public string ChooseLabel { get; }

    public IRelayCommand ChooseCommand { get; }
}

/// <summary>
/// The start page: a short questionnaire, then the distribution that fits best (and two others), explained simply.
/// Choosing one leads to the drive selection, like the distributions page.
/// </summary>
public sealed partial class AdvisorViewModel : ObservableObject, INavigationAware
{
    private const int QuestionCount = 6;
    private readonly ICatalogService _catalog;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private IReadOnlyList<Distro> _distros = [];

    public AdvisorViewModel(ICatalogService catalog, ILocalizer localizer, INavigationService navigation, WizardState wizard, DisplayFormatter formatter)
    {
        _catalog = catalog;
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _formatter = formatter;

        // The memory of this PC preselects the second answer: the drive is usually made for the same computer.
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var modest = memory > 0 && memory < DistroAdvisor.ModestMemoryBytes * 7 / 8;
        var memoryHint = memory > 0
            ? localizer.Format("Advisor_MemoryHint", Math.Round(memory / (1024.0 * 1024 * 1024)).ToString(CultureInfo.CurrentCulture))
            : string.Empty;

        for (var i = 1; i <= QuestionCount; i++)
        {
            var options = Enumerable.Range(1, 3)
                .Select(o => $"Advisor_Q{i}_A{o}")
                .Select(key => localizer.Get(key) is var text && text != key ? text : null)
                .OfType<string>()
                .ToList();
            var hint = i == 2 ? memoryHint : localizer.Get($"Advisor_Q{i}_Hint") is var h && h != $"Advisor_Q{i}_Hint" ? h : string.Empty;
            var selected = i == 2 && memory > 0 ? (modest ? 1 : 0) : -1;
            Questions.Add(new AdvisorQuestionViewModel(localizer.Format("Advisor_QuestionTitle", i, localizer.Get($"Advisor_Q{i}")), hint, options, selected, OnAnswerChanged));
        }
    }

    public ObservableCollection<AdvisorQuestionViewModel> Questions { get; } = [];

    /// <summary>The best suggestion (a single item, so that it shares the template of the others).</summary>
    public ObservableCollection<AdvisorResultViewModel> Best { get; } = [];

    public ObservableCollection<AdvisorResultViewModel> Alternatives { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecommendCommand))]
    private bool _canRecommend;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isLoading;

    public void OnNavigatedTo(object? parameter)
    {
        if (_distros.Count == 0 && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand(CanExecute = nameof(CanRecommend))]
    private void Recommend()
    {
        Best.Clear();
        Alternatives.Clear();
        if (Answers() is not { } answers || _distros.Count == 0)
        {
            HasResult = false;
            return;
        }

        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var suggestions = DistroAdvisor.Recommend(_distros, answers);
        for (var i = 0; i < suggestions.Count; i++)
        {
            (i == 0 ? Best : Alternatives).Add(ToResult(suggestions[i], language, best: i == 0));
        }

        HasResult = Best.Count > 0;
    }

    [RelayCommand]
    private void ShowAllDistros() => _navigation.NavigateTo(PageKeys.Distros);

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _distros = (_catalog.Current ?? await _catalog.LoadAsync()).Catalog.Distros;
        }
        finally
        {
            IsLoading = false;
        }

        OnAnswerChanged();
    }

    private void OnAnswerChanged()
    {
        CanRecommend = _distros.Count > 0 && Questions.All(q => q.SelectedIndex >= 0);

        // Once a suggestion is shown, it follows the answers.
        if (HasResult)
        {
            Recommend();
        }
    }

    private AdvisorAnswers? Answers()
    {
        if (Questions.Any(q => q.SelectedIndex < 0))
        {
            return null;
        }

        var a = Questions.Select(q => q.SelectedIndex).ToArray();
        return new AdvisorAnswers(
            (LinuxExperience)a[0],
            (ComputerPower)a[1],
            (MainUse)a[2],
            (DesktopLook)a[3],
            (UpdatePace)a[4],
            AvoidFirmwareSettings: a[5] == 0);
    }

    private AdvisorResultViewModel ToResult(DistroRecommendation suggestion, string language, bool best)
    {
        var distro = suggestion.Distro;
        var key = "Simple_" + distro.Id;
        var simple = _localizer.Get(key);
        var explanation = simple == key ? distro.Description.Get(language) : simple;

        var reasons = suggestion.Reasons.Select(r => _localizer.Get("Advisor_Reason_" + r)).ToList();
        var requirements = distro.Requirements is { RamMb: { } ram, DiskGb: { } disk }
            ? _localizer.Format("Advisor_Requirements", (ram / 1024.0).ToString("0.#", CultureInfo.CurrentCulture), disk)
            : string.Empty;

        return new AdvisorResultViewModel(
            new DistroItemViewModel(distro, _localizer, _formatter, language),
            explanation,
            reasons,
            requirements,
            _localizer.Format(best ? "Advisor_CreateBest" : "Advisor_Choose", distro.Name),
            () =>
            {
                _wizard.SelectDistro(distro);
                _navigation.NavigateTo(PageKeys.Drive);
            });
    }
}
