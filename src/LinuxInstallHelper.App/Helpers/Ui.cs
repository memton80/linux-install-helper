using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LinuxInstallHelper.App.Helpers;

/// <summary>Small conversion functions used from x:Bind.</summary>
public static class Ui
{
    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Collapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleIfAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisibleIfText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static bool Not(bool value) => !value;

    public static bool Both(bool first, bool second) => first && second;

    /// <summary>A picture from a file, or nothing.</summary>
    public static ImageSource? Picture(string? path) => string.IsNullOrEmpty(path) ? null : new BitmapImage(new Uri(path));

    /// <summary>
    /// Width of the picture column of a lesson: five parts for six of text, so that both shrink with the window, or the
    /// width of the icon when there is no picture.
    /// </summary>
    public static GridLength PictureColumn(bool hasPicture) => hasPicture ? new GridLength(5, GridUnitType.Star) : GridLength.Auto;
}
