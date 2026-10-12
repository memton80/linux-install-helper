using System.Runtime.InteropServices;
using LinuxInstallHelper.App.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
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
    private readonly UpdateNotifier _updates;
    private readonly AdvancedModeState _advanced;
    private bool _closeConfirmed;
    private bool _showingAdvanced;
    private RectInt32? _passthrough;

    public MainWindow(INavigationService navigation, ILocalizer localizer, AppBusyState busy, IDialogService dialogs, WizardState wizard, UpdateNotifier updates, AdvancedModeState advanced)
    {
        _navigation = navigation;
        _localizer = localizer;
        _busy = busy;
        _dialogs = dialogs;
        _wizard = wizard;
        _updates = updates;
        _advanced = advanced;
        InitializeComponent();
        _updates.PropertyChanged += (_, _) => ShowUpdate();
        _advanced.PropertyChanged += (_, _) => ShowAdvanced();
        ShowAdvanced();

        Title = localizer.Get("AppDisplayName");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Closing += OnClosing;
        ResizeAndCenter(DefaultWidth, DefaultHeight);

        // The title bar is a drag area: keep the advanced mode switch clickable wherever it is drawn.
        AppTitleBar.Loaded += (_, _) =>
        {
            AppTitleBar.XamlRoot.Changed += (_, _) => UpdateCaptionButtonsColumn();
            UpdateCaptionButtonsColumn();
        };
        AppTitleBar.SizeChanged += (_, _) => UpdateCaptionButtonsColumn();
        AdvancedToggle.LayoutUpdated += (_, _) => UpdatePassthrough();

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
            _navigation.NavigateTo(PageKeys.Advisor);
        }
    }

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var key = args.IsSettingsInvoked ? PageKeys.Settings : args.InvokedItemContainer?.Tag as string;
        if (key is null)
        {
            return;
        }

        if (key is PageKeys.Advisor or PageKeys.Distros or PageKeys.LocalIso)
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

    private void ShowUpdate()
    {
        if (_updates.Available is { } release)
        {
            UpdateBar.Message = _localizer.Format("UpdateBar_Message", release.Version.ToString(3), AppInfo.Version);
        }

        UpdateBar.IsOpen = _updates.IsAvailable;
    }

    private void OnDownloadUpdate(object sender, RoutedEventArgs e) => _updates.OpenReleasePage();

    /// <summary>The switch and the orange frame follow the advanced mode.</summary>
    private void ShowAdvanced()
    {
        _showingAdvanced = true;
        AdvancedToggle.IsOn = _advanced.IsOn;
        _showingAdvanced = false;
        AdvancedFrame.Visibility = _advanced.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Turning the advanced mode on asks first (its details can look worrying); turning it off does not.</summary>
    private async void OnAdvancedToggled(object sender, RoutedEventArgs e)
    {
        if (_showingAdvanced || AdvancedToggle.IsOn == _advanced.IsOn)
        {
            return;
        }

        if (!AdvancedToggle.IsOn)
        {
            _advanced.Set(false);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("Advanced_ConfirmTitle"),
            _localizer.Get("Advanced_ConfirmMessage"),
            _localizer.Get("Advanced_ConfirmPrimary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);
        if (confirmed)
        {
            _advanced.Set(true);
        }

        // Puts the switch back when the warning is refused.
        ShowAdvanced();
    }

    /// <summary>Keeps the advanced mode switch clear of the minimize, maximize and close buttons drawn by Windows.</summary>
    private void UpdateCaptionButtonsColumn()
    {
        if (AppTitleBar.XamlRoot is { } root)
        {
            CaptionButtonsColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / root.RasterizationScale);
        }
    }

    /// <summary>
    /// With <see cref="Window.ExtendsContentIntoTitleBar"/>, the title bar is a drag area that receives no click: the area of
    /// the switch is made a "passthrough" region, again each time the switch moves (window resized, DPI changed).
    /// </summary>
    private void UpdatePassthrough()
    {
        if (AdvancedToggle.XamlRoot is not { } root || AdvancedToggle.ActualWidth <= 0)
        {
            return;
        }

        var scale = root.RasterizationScale;
        var bounds = AdvancedToggle.TransformToVisual(null).TransformBounds(new Rect(0, 0, AdvancedToggle.ActualWidth, AdvancedToggle.ActualHeight));
        var region = new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
        if (_passthrough is { } last && last.X == region.X && last.Y == region.Y && last.Width == region.Width && last.Height == region.Height)
        {
            return;
        }

        _passthrough = region;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [region]);
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
