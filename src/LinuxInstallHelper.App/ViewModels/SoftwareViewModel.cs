using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Software;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>An installed program and what to use on Linux.</summary>
public sealed class SoftwareItemViewModel
{
    public SoftwareItemViewModel(InstalledProgram program, SoftwareAdvice advice, ILocalizer localizer)
    {
        Program = program;
        Advice = advice;
        Name = program.Name;
        Details = string.Join(" · ", new[] { program.Publisher, program.Version }.Where(v => !string.IsNullOrWhiteSpace(v)));
        Suggestion = advice.Suggestions is { } suggestions ? localizer.Format("Software_Suggestions", suggestions) : string.Empty;
        CanSearch = advice.Verdict is SoftwareVerdict.Unknown or SoftwareVerdict.WindowsOnly;
        SearchLabel = localizer.Get("Software_Search");
        SearchName = localizer.Format("Software_SearchName", program.Name);
        SearchCommand = new RelayCommand(() =>
            SystemActions.OpenUrl("https://alternativeto.net/browse/search/?q=" + Uri.EscapeDataString(SearchTerms(program.Name))));
    }

    public InstalledProgram Program { get; }

    public SoftwareAdvice Advice { get; }

    public string Name { get; }

    /// <summary>Publisher and version.</summary>
    public string Details { get; }

    /// <summary>"On Linux: …", empty when there is nothing to suggest.</summary>
    public string Suggestion { get; }

    public bool CanSearch { get; }

    public string SearchLabel { get; }

    /// <summary>Accessible name of the search button.</summary>
    public string SearchName { get; }

    public IRelayCommand SearchCommand { get; }

    // "Some Tool 2.3.1 (x64)" is searched as "Some Tool": versions and architectures do not help.
    private static string SearchTerms(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .TakeWhile(word => !char.IsAsciiDigit(word[0]) && !word.StartsWith('('))
            .ToList();
        return words.Count == 0 ? name : string.Join(' ', words);
    }
}

/// <summary>The programs that share a verdict, under a title that explains it.</summary>
public sealed record SoftwareGroupViewModel(string Title, string Description, IReadOnlyList<SoftwareItemViewModel> Items);

/// <summary>
/// "My programs on Linux": the programs installed on Windows, grouped by what happens to them on Linux, with a list to
/// save next to the files to back up.
/// </summary>
public sealed partial class SoftwareViewModel : ObservableObject, INavigationAware
{
    // The groups the user must look at first come first.
    private static readonly SoftwareVerdict[] Order =
    [
        SoftwareVerdict.WindowsOnly,
        SoftwareVerdict.Unknown,
        SoftwareVerdict.Alternative,
        SoftwareVerdict.Web,
        SoftwareVerdict.Native,
        SoftwareVerdict.NotNeeded,
    ];

    private readonly IInstalledProgramSource _source;
    private readonly ILocalizer _localizer;
    private readonly ILogger<SoftwareViewModel> _logger;
    private string? _savedPath;

    public SoftwareViewModel(IInstalledProgramSource source, ILocalizer localizer, ILogger<SoftwareViewModel> logger)
    {
        _source = source;
        _localizer = localizer;
        _logger = logger;
    }

    public ObservableCollection<SoftwareGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _hasPrograms;

    [ObservableProperty]
    private bool _isSaved;

    [ObservableProperty]
    private string _savedMessage = string.Empty;

    public void OnNavigatedTo(object? parameter)
    {
        if (Groups.Count == 0 && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand(CanExecute = nameof(HasPrograms))]
    private async Task SaveAsync()
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var path = Path.Combine(folder, _localizer.Get("Software_FileName") + ".txt");
            await File.WriteAllTextAsync(path, Report(), Encoding.UTF8);
            _savedPath = path;
            SavedMessage = _localizer.Format("Software_Saved", path);
            IsSaved = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save the list of programs");
            SavedMessage = _localizer.Format("Software_SaveFailed", ex.Message);
            IsSaved = true;
        }
    }

    [RelayCommand]
    private void ShowSaved()
    {
        if (_savedPath is not null)
        {
            SystemActions.ShowInExplorer(_savedPath);
        }
    }

    private async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        try
        {
            var programs = await _source.ReadAsync();
            var items = programs.Select(p => new SoftwareItemViewModel(p, SoftwareEquivalents.For(p.Name), _localizer)).ToList();
            Groups.Clear();
            foreach (var verdict in Order)
            {
                var members = items.Where(i => i.Advice.Verdict == verdict).ToList();
                if (members.Count > 0)
                {
                    Groups.Add(new SoftwareGroupViewModel(
                        _localizer.Format($"Software_Group_{verdict}", members.Count),
                        _localizer.Get($"Software_Group_{verdict}_Help"),
                        members));
                }
            }

            HasPrograms = items.Count > 0;
            Summary = _localizer.Format(
                "Software_Summary",
                items.Count,
                items.Count(i => i.Advice.Verdict is SoftwareVerdict.Native or SoftwareVerdict.Alternative or SoftwareVerdict.Web or SoftwareVerdict.NotNeeded),
                items.Count(i => i.Advice.Verdict is SoftwareVerdict.Unknown or SoftwareVerdict.WindowsOnly));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not list the installed programs");
            HasError = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>The list as plain text, to keep with the backup.</summary>
    private string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(_localizer.Format("Software_FileTitle", DateTime.Now.ToString("d", CultureInfo.CurrentCulture)));
        text.AppendLine(Summary);
        foreach (var group in Groups)
        {
            text.AppendLine();
            text.AppendLine(group.Title);
            text.AppendLine(group.Description);
            foreach (var item in group.Items)
            {
                var details = item.Details.Length > 0 ? $" ({item.Details})" : string.Empty;
                var suggestion = item.Suggestion.Length > 0 ? $" — {item.Suggestion}" : string.Empty;
                text.AppendLine($"- {item.Name}{details}{suggestion}");
            }
        }

        return text.ToString();
    }
}
