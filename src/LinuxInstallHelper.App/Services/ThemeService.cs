using LinuxInstallHelper.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace LinuxInstallHelper.App.Services;

public interface IThemeService
{
    void Apply(AppTheme theme);
}

/// <summary>Light, dark or Windows theme, including the colors of the caption buttons.</summary>
public sealed class ThemeService : IThemeService
{
    private bool _subscribed;

    public void Apply(AppTheme theme)
    {
        var root = App.MainWindow.RootElement;
        root.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (!_subscribed)
        {
            root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
            _subscribed = true;
        }

        UpdateCaptionButtons();
    }

    private static void UpdateCaptionButtons()
    {
        var window = App.MainWindow;
        var dark = window.RootElement.ActualTheme == ElementTheme.Dark;
        var titleBar = window.AppWindow.TitleBar;

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A) : Color.FromArgb(0xFF, 0x70, 0x70, 0x70);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00);
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00);
    }
}
