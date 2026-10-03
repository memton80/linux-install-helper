using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Services;

/// <summary>Frame based navigation, usable from view models without referencing pages.</summary>
public interface INavigationService
{
    /// <summary>Raised after a page has been shown, with its key.</summary>
    event EventHandler<string>? Navigated;

    bool CanGoBack { get; }

    string? CurrentPageKey { get; }

    void Initialize(Frame frame);

    bool NavigateTo(string pageKey, object? parameter = null, bool clearHistory = false);

    bool GoBack();
}

/// <summary>Implemented by view models that want to know when their page is shown or left.</summary>
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);

    void OnNavigatedFrom();
}
