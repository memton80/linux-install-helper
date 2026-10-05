using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A line of the "at a glance" card of the distribution page.</summary>
public sealed record DistroFact(string Label, string Value);

/// <summary>
/// The page between the list of distributions and the drive selection: what the distribution is, its official website,
/// and the button that creates the drive with it.
/// </summary>
public sealed partial class DistroDetailsViewModel : ObservableObject, INavigationAware
{
    private readonly ICatalogService _catalog;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private readonly Glossary _glossary;
    private Distro? _distro;

    public DistroDetailsViewModel(ICatalogService catalog, ILocalizer localizer, INavigationService navigation, WizardState wizard, DisplayFormatter formatter, Glossary glossary)
    {
        _catalog = catalog;
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _formatter = formatter;
        _glossary = glossary;
        WordsLabel = localizer.Get("DistroDetails_WordsButton");
    }

    [ObservableProperty]
    private DistroItemViewModel? _item;

    /// <summary>What the distribution is, in plain words when the application has a simple explanation for it.</summary>
    [ObservableProperty]
    private string _explanation = string.Empty;

    /// <summary>The description of the catalog, shown under a simple explanation; empty otherwise.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DistroFact> _facts = [];

    [ObservableProperty]
    private IReadOnlyList<ExplainedWord> _words = [];

    [ObservableProperty]
    private string _website = string.Empty;

    [ObservableProperty]
    private string _websiteLabel = string.Empty;

    [ObservableProperty]
    private string _createLabel = string.Empty;

    [ObservableProperty]
    private bool _showSecureBootWarning;

    public string WordsLabel { get; }

    public void OnNavigatedTo(object? parameter)
    {
        if (parameter is Distro distro)
        {
            Show(distro);
        }
        else
        {
            // Opened without a distribution (--page DistroDetails, used by the CI): the one of the wizard, or the first one.
            _ = ShowDefaultAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void Create()
    {
        if (_distro is not null)
        {
            _wizard.SelectDistro(_distro);
            _navigation.NavigateTo(PageKeys.Drive);
        }
    }

    [RelayCommand]
    private void OpenWebsite() => SystemActions.OpenUrl(Website);

    [RelayCommand]
    private void Back()
    {
        if (!_navigation.GoBack())
        {
            _navigation.NavigateTo(PageKeys.Distros);
        }
    }

    private async Task ShowDefaultAsync()
    {
        var distros = (_catalog.Current ?? await _catalog.LoadAsync()).Catalog.Distros;
        if ((_wizard.Distro ?? distros.FirstOrDefault()) is { } distro)
        {
            Show(distro);
        }
    }

    private void Show(Distro distro)
    {
        _distro = distro;
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var description = distro.Description.Get(language);
        var key = "Simple_" + distro.Id;
        var simple = _localizer.Get(key);

        Item = new DistroItemViewModel(distro, _localizer, _formatter, language);
        Explanation = simple == key ? description : simple;
        Description = simple == key ? string.Empty : description;
        Facts = BuildFacts(distro);
        Words = _glossary.ForDistro(distro, mentionsSecureBoot: true, mentionsDownload: true);
        Website = distro.Homepage;
        WebsiteLabel = _localizer.Format("DistroDetails_Website", Host(distro.Homepage));
        CreateLabel = _localizer.Format("Advisor_CreateBest", distro.Name);
        ShowSecureBootWarning = !distro.SecureBoot;
    }

    private List<DistroFact> BuildFacts(Distro distro)
    {
        var facts = new List<DistroFact>();
        if (!string.IsNullOrWhiteSpace(distro.Desktop) && distro.Desktop != "None")
        {
            facts.Add(new DistroFact(_localizer.Get("DistroDetails_Fact_Desktop"), distro.Desktop));
        }

        if (distro.Categories.Count > 0)
        {
            facts.Add(new DistroFact(_localizer.Get("DistroDetails_Fact_Uses"), string.Join(" · ", distro.Categories.Select(c => _localizer.Get("Category_" + c)))));
        }

        facts.Add(new DistroFact(_localizer.Get("DistroDetails_Fact_Family"), _localizer.Get("Family_" + distro.Family)));
        facts.Add(new DistroFact(_localizer.Get("DistroDetails_Fact_Download"), _formatter.Size(distro.Image.Size)));
        if (distro.Requirements is { RamMb: { } ram, DiskGb: { } disk })
        {
            facts.Add(new DistroFact(
                _localizer.Get("DistroDetails_Fact_Needs"),
                _localizer.Format("DistroDetails_NeedsValue", (ram / 1024.0).ToString("0.#", CultureInfo.CurrentCulture), disk)));
        }

        facts.Add(new DistroFact(
            _localizer.Get("DistroDetails_Fact_SecureBoot"),
            _localizer.Get(distro.SecureBoot ? "DistroDetails_SecureBootOk" : "DistroDetails_SecureBootOff")));
        return facts;
    }

    // "https://www.ubuntu.com/desktop" reads "ubuntu.com".
    private static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : url;
}
