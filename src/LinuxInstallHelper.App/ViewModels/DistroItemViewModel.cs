using System.Globalization;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Catalog;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A card of the distributions screen.</summary>
public sealed class DistroItemViewModel
{
    public DistroItemViewModel(Distro distro, ILocalizer localizer, DisplayFormatter formatter, string language)
    {
        Distro = distro;
        Name = distro.Name;
        Subtitle = string.IsNullOrWhiteSpace(distro.Edition) ? distro.Version : $"{distro.Edition} · {distro.Version}";
        Description = distro.Description.Get(language);
        Monogram = distro.Monogram;
        BadgeBrush = new SolidColorBrush(ParseColor(distro.Color));

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(distro.Desktop) && distro.Desktop != "None")
        {
            details.Add(distro.Desktop);
        }

        details.Add(formatter.Size(distro.Image.Size));
        details.Add(localizer.Get("Family_" + distro.Family));
        Details = string.Join(" · ", details);

        Categories = string.Join(" · ", distro.Categories.Select(c => localizer.Get("Category_" + c)));
        ShowSecureBootWarning = !distro.SecureBoot;
        SecureBootWarning = localizer.Get("Distro_NoSecureBoot");
        AutomationName = $"{distro.DisplayName} {distro.Version}";
    }

    public Distro Distro { get; }

    public string Name { get; }

    public string Subtitle { get; }

    public string Description { get; }

    public string Monogram { get; }

    public SolidColorBrush BadgeBrush { get; }

    public string Details { get; }

    public string Categories { get; }

    public bool ShowSecureBootWarning { get; }

    public string SecureBootWarning { get; }

    public string AutomationName { get; }

    private static Color ParseColor(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        return Colors.SteelBlue;
    }
}
