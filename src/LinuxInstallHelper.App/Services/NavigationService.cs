using LinuxInstallHelper.App.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace LinuxInstallHelper.App.Services;

public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        [PageKeys.Distros] = typeof(DistrosPage),
        [PageKeys.LocalIso] = typeof(LocalIsoPage),
        [PageKeys.Settings] = typeof(SettingsPage),
        [PageKeys.About] = typeof(AboutPage),
    };

    private Frame? _frame;

    public event EventHandler<string>? Navigated;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public string? CurrentPageKey { get; private set; }

    public void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnFrameNavigated;
        _frame.Navigating += OnFrameNavigating;
    }

    public bool NavigateTo(string pageKey, object? parameter = null, bool clearHistory = false)
    {
        if (_frame is null || !Pages.TryGetValue(pageKey, out var pageType))
        {
            return false;
        }

        if (CurrentPageKey == pageKey && parameter is null && !clearHistory)
        {
            return false;
        }

        var navigated = _frame.Navigate(pageType, parameter, new EntranceNavigationTransitionInfo());
        if (navigated && clearHistory)
        {
            _frame.BackStack.Clear();
        }

        return navigated;
    }

    public bool GoBack()
    {
        if (_frame is { CanGoBack: true })
        {
            _frame.GoBack();
            return true;
        }

        return false;
    }

    private void OnFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_frame?.Content is FrameworkElement { DataContext: INavigationAware previous })
        {
            previous.OnNavigatedFrom();
        }
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        CurrentPageKey = Pages.FirstOrDefault(p => p.Value == e.SourcePageType).Key;

        if (e.Content is FrameworkElement { DataContext: INavigationAware current })
        {
            current.OnNavigatedTo(e.Parameter);
        }

        if (CurrentPageKey is not null)
        {
            Navigated?.Invoke(this, CurrentPageKey);
        }
    }
}
