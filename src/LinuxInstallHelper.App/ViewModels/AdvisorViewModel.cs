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

    public AdvisorQuestionViewModel(string title, string hint, IReadOnlyList<string> options, IReadOnlyList<ExplainedWord> words, string wordsLabel, int selectedIndex, Action changed)
    {
        Title = title;
        Hint = hint;
        Options = options;
        Words = words;
        WordsLabel = wordsLabel;
        _selectedIndex = selectedIndex;
        _changed = changed;
    }

    public string Title { get; }

    /// <summary>Extra explanation, may be empty.</summary>
    public string Hint { get; }

    public IReadOnlyList<string> Options { get; }

    /// <summary>The words of the question that a beginner may not know, shown behind its "?" button.</summary>
    public IReadOnlyList<ExplainedWord> Words { get; }

    /// <summary>Tooltip and accessible name of the "?" button.</summary>
    public string WordsLabel { get; }

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
    public AdvisorResultViewModel(DistroItemViewModel item, string explanation, IReadOnlyList<string> reasons, string requirements, IReadOnlyList<ExplainedWord> words, string wordsLabel, string chooseLabel, Action choose)
    {
        Item = item;
        Explanation = explanation;
        Reasons = reasons;
        Requirements = requirements;
        Words = words;
        WordsLabel = wordsLabel;
        ChooseLabel = chooseLabel;
        ChooseCommand = new RelayCommand(choose);
    }

    public DistroItemViewModel Item { get; }

    public string Explanation { get; }

    public IReadOnlyList<string> Reasons { get; }

    public string Requirements { get; }

    /// <summary>The words of the suggestion that a beginner may not know, shown behind its "?" button.</summary>
    public IReadOnlyList<ExplainedWord> Words { get; }

    public string WordsLabel { get; }

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

    // The words of the glossary explained behind the "?" button of each question.
    private static readonly string[][] QuestionWords =
    [
        ["linux", "distribution", "terminal"],
        ["memory", "gb", "memory-where"],
        ["office", "programming", "advanced"],
        ["desktop", "taskbar", "clean"],
        ["update", "bug", "update-unsure"],
        ["bios", "secureboot", "bios-unsure"],
    ];

    private readonly ICatalogService _catalog;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private readonly Glossary _glossary;
    private IReadOnlyList<Distro> _distros = [];

    public AdvisorViewModel(ICatalogService catalog, ILocalizer localizer, INavigationService navigation, WizardState wizard, DisplayFormatter formatter, Glossary glossary)
    {
        _catalog = catalog;
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _formatter = formatter;
        _glossary = glossary;

        // The memory of this PC preselects the second answer: the drive is usually made for the same computer.
        var memory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var modest = memory > 0 && memory < DistroAdvisor.ModestMemoryBytes * 7 / 8;
        var memoryHint = memory > 0
            ? localizer.Format("Advisor_MemoryHint", Math.Round(memory / (1024.0 * 1024 * 1024)).ToString(CultureInfo.CurrentCulture))
            : string.Empty;

        for (var i = 1; i <= QuestionCount; i++)
        {
            var options = Enumerable.Range(1, 3)
                .Select(o => Optional($"Advisor_Q{i}_A{o}"))
                .OfType<string>()
                .ToList();
            var hint = i == 2 ? memoryHint : Optional($"Advisor_Q{i}_Hint") ?? string.Empty;
            var selected = i == 2 && memory > 0 ? (modest ? 1 : 0) : -1;
            Questions.Add(new AdvisorQuestionViewModel(
                localizer.Format("Advisor_QuestionTitle", i, localizer.Get($"Advisor_Q{i}")),
                hint,
                options,
                glossary.Get(QuestionWords[i - 1]),
                localizer.Format("Advisor_WordsButton", i),
                selected,
                OnAnswerChanged));
        }
    }

    public ObservableCollection<AdvisorQuestionViewModel> Questions { get; } = [];

    /// <summary>The best suggestion (a single item, so that it shares the template of the others).</summary>
    public ObservableCollection<AdvisorResultViewModel> Best { get; } = [];

    public ObservableCollection<AdvisorResultViewModel> Alternatives { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecommendCommand))]
    private bool _canRecommend;

    /// <summary>The suggestion replaces the questionnaire until the user edits the answers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQuestions))]
    private bool _hasResult;

    public bool ShowQuestions => !HasResult;

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
    private void EditAnswers() => HasResult = false;

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

    /// <summary>The string for <paramref name="key"/>, or null when the questionnaire does not define it.</summary>
    private string? Optional(string key) => _localizer.Get(key) is var text && text != key ? text : null;

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
            _glossary.ForDistro(distro, mentionsSecureBoot: suggestion.Reasons.Contains(AdvisorReason.SecureBoot), mentionsDownload: false),
            _localizer.Get("Advisor_ResultWordsButton"),
            _localizer.Format(best ? "Advisor_CreateBest" : "Advisor_Choose", distro.Name),
            () =>
            {
                _wizard.SelectDistro(distro);
                _navigation.NavigateTo(PageKeys.Drive);
            });
    }
}
