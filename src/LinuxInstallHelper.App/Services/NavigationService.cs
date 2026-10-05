using LinuxInstallHelper.App.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace LinuxInstallHelper.App.Services;

public sealed class NavigationService : INavigationService
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        [PageKeys.Advisor] = typeof(AdvisorPage),
        [PageKeys.Distros] = typeof(DistrosPage),
        [PageKeys.DistroDetails] = typeof(DistroDetailsPage),
        [PageKeys.LocalIso] = typeof(LocalIsoPage),
        [PageKeys.Restore] = typeof(RestorePage),
        [PageKeys.Guide] = typeof(GuidePage),
        [PageKeys.Drive] = typeof(DrivePage),
        [PageKeys.Progress] = typeof(ProgressPage),
        [PageKeys.Done] = typeof(DonePage),
        [PageKeys.Settings] = typeof(SettingsPage),
        [PageKeys.About] = typeof(AboutPage),
    };

    private readonly ILogger _logger;
    private Frame? _frame;

    public NavigationService(ILogger<NavigationService>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public event EventHandler<string>? Navigated;

    public bool CanGoBack => !IsLocked && (_frame?.CanGoBack ?? false);

    public string? CurrentPageKey { get; private set; }

    public bool IsLocked { get; set; }

    public void Initialize(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnFrameNavigated;
        _frame.Navigating += OnFrameNavigating;
        _frame.NavigationFailed += OnNavigationFailed;
    }

    // XAML only reports "NavigationFailed was unhandled": log the page and the real error first.
    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e) =>
        _logger.LogError(e.Exception, "Could not open {Page}: {Message}", e.SourcePageType?.FullName, e.Exception?.ToString());

    public bool NavigateTo(string pageKey, object? parameter = null, bool clearHistory = false)
    {
        if (_frame is null || IsLocked || !Pages.TryGetValue(pageKey, out var pageType))
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
            Navigated?.Invoke(this, pageKey);
        }

        return navigated;
    }

    public bool GoBack()
    {
        if (!IsLocked && _frame is { CanGoBack: true })
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
