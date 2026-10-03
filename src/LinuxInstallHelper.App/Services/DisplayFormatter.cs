using System.Globalization;

namespace LinuxInstallHelper.App.Services;

/// <summary>Sizes, speeds and durations in the user's language (binary units, like Windows Explorer).</summary>
public sealed class DisplayFormatter
{
    private readonly ILocalizer _localizer;

    public DisplayFormatter(ILocalizer localizer)
    {
        _localizer = localizer;
    }

    public string Size(long bytes)
    {
        const double Kb = 1024d;
        const double Mb = Kb * 1024;
        const double Gb = Mb * 1024;

        return bytes switch
        {
            >= (long)Gb => string.Format(CultureInfo.CurrentCulture, "{0:0.0} {1}", bytes / Gb, _localizer.Get("Unit_GB")),
            >= (long)Mb => string.Format(CultureInfo.CurrentCulture, "{0:0} {1}", bytes / Mb, _localizer.Get("Unit_MB")),
            >= (long)Kb => string.Format(CultureInfo.CurrentCulture, "{0:0} {1}", bytes / Kb, _localizer.Get("Unit_KB")),
            _ => string.Format(CultureInfo.CurrentCulture, "{0} {1}", bytes, _localizer.Get("Unit_Bytes")),
        };
    }

    public string Speed(double bytesPerSecond) =>
        _localizer.Format("Unit_PerSecond", Size((long)Math.Max(0, bytesPerSecond)));

    public string Duration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return _localizer.Format("Duration_Hours", (int)duration.TotalHours, duration.Minutes);
        }

        return duration.TotalMinutes >= 1
            ? _localizer.Format("Duration_Minutes", (int)duration.TotalMinutes, duration.Seconds)
            : _localizer.Format("Duration_Seconds", Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds)));
    }

    public string Percent(double fraction) => string.Format(CultureInfo.CurrentCulture, "{0:0} %", fraction * 100);

    /// <summary>Groups an OpenPGP fingerprint by 4 characters.</summary>
    public static string Fingerprint(string? fingerprint) =>
        fingerprint is { Length: 40 } f ? string.Join(' ', Enumerable.Range(0, 10).Select(i => f.Substring(i * 4, 4))) : fingerprint ?? string.Empty;
}
