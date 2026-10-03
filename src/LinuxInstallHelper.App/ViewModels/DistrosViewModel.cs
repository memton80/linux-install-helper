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
    private readonly WizardState _wizard;
    private readonly DisplayFormatter _formatter;
    private IReadOnlyList<Distro> _all = [];
    private bool _loaded;

    public DistrosViewModel(ICatalogService catalog, ILocalizer localizer, INavigationService navigation, WizardState wizard, DisplayFormatter formatter)
    {
        _catalog = catalog;
        _localizer = localizer;
        _navigation = navigation;
        _wizard = wizard;
        _formatter = formatter;

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
    }

    public ObservableCollection<DistroItemViewModel> Distros { get; } = [];

    public IReadOnlyList<FilterOption> Families { get; }

    public IReadOnlyList<FilterOption> Categories { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _familyIndex;

    [ObservableProperty]
    private int _categoryIndex;

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

    public void Select(DistroItemViewModel item)
    {
        _wizard.SelectDistro(item.Distro);
        _navigation.NavigateTo(PageKeys.Drive);
    }

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

    private void ApplyFilter()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var query = new DistroQuery(
            SearchText,
            Families.ElementAtOrDefault(FamilyIndex)?.Key,
            Categories.ElementAtOrDefault(CategoryIndex)?.Key);

        Distros.Clear();
        foreach (var distro in DistroFilter.Apply(_all, query, language))
        {
            Distros.Add(new DistroItemViewModel(distro, _localizer, _formatter, language));
        }

        IsEmpty = !IsLoading && _loaded && Distros.Count == 0;
    }
}
