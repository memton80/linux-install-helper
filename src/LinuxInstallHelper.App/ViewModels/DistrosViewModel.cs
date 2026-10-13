using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>An entry of a filter combo box (key null = everything).</summary>
public sealed record FilterOption(string? Key, string Label);

public sealed partial class DistrosViewModel : ObservableObject, INavigationAware
{
    private readonly ICatalogService _catalog;
    private readonly ILocalizer _localizer;
    private readonly INavigationService _navigation;
    private readonly DisplayFormatter _formatter;
    private readonly GuidanceState _guidance;
    private IReadOnlyList<Distro> _all = [];
    private bool _loaded;

    public DistrosViewModel(ICatalogService catalog, ILocalizer localizer, INavigationService navigation, DisplayFormatter formatter, GuidanceState guidance)
    {
        _catalog = catalog;
        _localizer = localizer;
        _navigation = navigation;
        _formatter = formatter;
        _guidance = guidance;

        // A singleton: it follows the mode when it is changed in the settings.
        IsAlone = guidance.IsAlone;
        guidance.PropertyChanged += (_, _) => IsAlone = _guidance.IsAlone;

        Families =
        [
            new FilterOption(null, localizer.Get("Filter_AllFamilies")),
            .. DistroFamilies.All.Select(f => new FilterOption(f, localizer.Get("Family_" + f))),
        ];
        Categories =
        [
            new FilterOption(null, localizer.Get("Filter_AllCategories")),
            .. DistroCategories.All.Select(c => new FilterOption(c, localizer.Get("Category_" + c))),
        ];
        // Every processor by default: the drive may be for another PC than this one.
        Processors =
        [
            new FilterOption(null, localizer.Get("Filter_AllProcessors")),
            .. DistroArchitectures.All.Select(a => new FilterOption(a, ProcessorText.Name(localizer, a))),
        ];
    }

    public ObservableCollection<DistroItemViewModel> Distros { get; } = [];

    public IReadOnlyList<FilterOption> Families { get; }

    public IReadOnlyList<FilterOption> Categories { get; }

    public IReadOnlyList<FilterOption> Processors { get; }

    /// <summary>The user is on their own: the page explains how to read the cards and choose one.</summary>
    [ObservableProperty]
    private bool _isAlone;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _familyIndex;

    [ObservableProperty]
    private int _categoryIndex;

    [ObservableProperty]
    private int _processorIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _isOffline;

    [ObservableProperty]
    private string _catalogStatus = string.Empty;

    public void OnNavigatedTo(object? parameter)
    {
        if (!_loaded && !IsLoading)
        {
            _ = LoadAsync();
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void OpenAdvisor() => _navigation.NavigateTo(PageKeys.Advisor);

    /// <summary>Opens the page that presents the distribution, from which the drive is created.</summary>
    public void Select(DistroItemViewModel item) => _navigation.NavigateTo(PageKeys.DistroDetails, item.Distro);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        IsEmpty = false;
        try
        {
            var result = await _catalog.LoadAsync();
            _all = result.Catalog.Distros;
            IsOffline = result.Source != CatalogSource.Remote;
            CatalogStatus = _localizer.Format("Catalog_Status_" + result.Source, result.Catalog.UpdatedDate.ToString("d", CultureInfo.CurrentCulture));
            _loaded = true;
        }
        finally
        {
            IsLoading = false;
        }

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnFamilyIndexChanged(int value) => ApplyFilter();

    partial void OnCategoryIndexChanged(int value) => ApplyFilter();

    partial void OnProcessorIndexChanged(int value) => ApplyFilter();

    private void ApplyFilter()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var query = new DistroQuery(
            SearchText,
            Families.ElementAtOrDefault(FamilyIndex)?.Key,
            Categories.ElementAtOrDefault(CategoryIndex)?.Key,
            Processors.ElementAtOrDefault(ProcessorIndex)?.Key);

        Distros.Clear();
        foreach (var distro in DistroFilter.Apply(_all, query, language))
        {
            Distros.Add(new DistroItemViewModel(distro, _localizer, _formatter, language));
        }

        IsEmpty = !IsLoading && _loaded && Distros.Count == 0;
    }
}
