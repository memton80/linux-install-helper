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
    private readonly ILocalizer _localizer;
    private readonly AppBusyState _busy;
    private readonly IDialogService _dialogs;
    private readonly WizardState _wizard;
    private bool _closeConfirmed;

    public MainWindow(INavigationService navigation, ILocalizer localizer, AppBusyState busy, IDialogService dialogs, WizardState wizard)
    {
        _navigation = navigation;
        _localizer = localizer;
        _busy = busy;
        _dialogs = dialogs;
        _wizard = wizard;
        InitializeComponent();

        Title = localizer.Get("AppDisplayName");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Closing += OnClosing;
        ResizeAndCenter(DefaultWidth, DefaultHeight);

        _navigation.Navigated += OnNavigated;
        _navigation.Initialize(ContentFrame);
    }

    /// <summary>Root element, used to apply the requested theme and to host dialogs.</summary>
    public FrameworkElement RootElement => RootGrid;

    /// <summary>Shows the first page.</summary>
    public void Start(string pageKey)
    {
        if (!_navigation.NavigateTo(pageKey))
        {
            _navigation.NavigateTo(PageKeys.Distros);
        }
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var key = args.IsSettingsInvoked ? PageKeys.Settings : args.InvokedItemContainer?.Tag as string;
        if (key is null)
        {
            return;
        }

        if (key is PageKeys.Distros or PageKeys.LocalIso)
        {
            // Starting over from the menu: forget the previous choices.
            _wizard.Reset();
        }

        if (!_navigation.NavigateTo(key) && _navigation.CurrentPageKey is { } current)
        {
            SyncSelection(current);
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        => _navigation.GoBack();

    private void OnNavigated(object? sender, string pageKey)
    {
        NavView.IsBackEnabled = _navigation.CanGoBack;
        SyncSelection(pageKey);
    }

    private void SyncSelection(string pageKey)
    {
        if (pageKey == PageKeys.Settings)
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        var menuKey = PageKeys.MenuKeyFor(pageKey, _wizard.IsLocalIso);
        NavView.SelectedItem = NavView.MenuItems
            .Concat(NavView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag as string == menuKey);
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_busy.IsBusy)
        {
            return;
        }

        args.Cancel = true;
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Close_BusyTitle"),
            _localizer.Get(_busy.IsWriting ? "Close_BusyWriting" : "Close_BusyMessage"),
            _localizer.Get("Close_BusyPrimary"),
            _localizer.Get("Close_BusyClose"),
            destructive: true);

        if (confirmed)
        {
            _busy.CurrentOperation?.Cancel();
            _closeConfirmed = true;
            Close();
        }
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
