using System.Runtime.InteropServices;
using LinuxInstallHelper.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace LinuxInstallHelper.App;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1180;
    private const int DefaultHeight = 820;

    private readonly INavigationService _navigation;

    public MainWindow(INavigationService navigation, ILocalizer localizer)
    {
        _navigation = navigation;
        InitializeComponent();

        Title = localizer.Get("AppDisplayName");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ResizeAndCenter(DefaultWidth, DefaultHeight);

        _navigation.Navigated += OnNavigated;
        _navigation.Initialize(ContentFrame);
        _navigation.NavigateTo(PageKeys.Distros);
    }

    /// <summary>Root element, used to apply the requested theme.</summary>
    public FrameworkElement RootElement => RootGrid;

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            _navigation.NavigateTo(PageKeys.Settings);
        }
        else if (args.InvokedItemContainer?.Tag is string key)
        {
            _navigation.NavigateTo(key);
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => _navigation.GoBack();

    private void OnNavigated(object? sender, string pageKey)
    {
        NavView.IsBackEnabled = _navigation.CanGoBack;

        if (pageKey == PageKeys.Settings)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        var menuKey = PageKeys.MenuKeyFor(pageKey);
        NavView.SelectedItem = NavView.MenuItems
            .Concat(NavView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == menuKey);
    }

    private void ResizeAndCenter(int width, int height)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

        var w = Math.Min((int)(width * scale), area.Width);
        var h = Math.Min((int)(height * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
