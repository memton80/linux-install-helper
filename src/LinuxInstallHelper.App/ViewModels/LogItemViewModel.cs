using System.Globalization;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A localized line of the activity log.</summary>
public sealed class LogItemViewModel
{
    // Arguments that are byte counts, by message key and position.
    private static readonly Dictionary<string, int> SizeArguments = new()
    {
        ["Log_LinkOk"] = 1,
        ["Log_Downloaded"] = 0,
        ["Log_WriteDone"] = 0,
    };

    public LogItemViewModel(CreationLogEntry entry, ILocalizer localizer, DisplayFormatter formatter)
    {
        var args = entry.Args.ToArray();
        if (SizeArguments.TryGetValue(entry.Key, out var index) && index < args.Length && args[index] is long bytes)
        {
            args[index] = formatter.Size(bytes);
        }

        Time = entry.Time.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
        Message = localizer.Format(entry.Key, args);
        Glyph = entry.Kind switch
        {
            LogKind.Success => "",
            LogKind.Warning => "",
            _ => "",
        };
        IconBrush = (Brush)Application.Current.Resources[entry.Kind switch
        {
            LogKind.Success => "SystemFillColorSuccessBrush",
            LogKind.Warning => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        }];
    }

    public LogItemViewModel(string message, ILocalizer localizer)
    {
        Time = DateTimeOffset.Now.ToString("T", CultureInfo.CurrentCulture);
        Message = message;
        Glyph = "";
        IconBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
    }

    public string Time { get; }

    public string Message { get; }

    public string Glyph { get; }

    public Brush IconBrush { get; }
}
