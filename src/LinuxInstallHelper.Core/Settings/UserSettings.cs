using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Settings;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Preferences saved in <c>%LOCALAPPDATA%\LinuxInstallHelper\settings.json</c>.</summary>
public sealed record UserSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary><c>null</c> follows Windows, otherwise <c>en-US</c> or <c>fr-FR</c>.</summary>
    public string? Language { get; init; }

    /// <summary>Folder for downloaded ISO files, <c>null</c> for the default one.</summary>
    public string? DownloadFolder { get; init; }

    /// <summary>Keep the ISO after a successful write (to create another drive later without downloading).</summary>
    public bool KeepIsoAfterWrite { get; init; } = true;

    /// <summary>Read the drive back after writing to compare it with the image.</summary>
    public bool VerifyAfterWrite { get; init; } = true;

    /// <summary>Safely remove the drive when it is ready.</summary>
    public bool EjectWhenDone { get; init; } = true;

    /// <summary>
    /// Items of the "before leaving Windows" checklist already done, as comma-separated keys (a string keeps the record's
    /// value equality).
    /// </summary>
    public string? ChecklistDone { get; init; }

    public static IReadOnlyList<string> SupportedLanguages { get; } = ["en-US", "fr-FR"];

    /// <summary>Returns these settings with the checklist item <paramref name="key"/> marked as done.</summary>
    public UserSettings WithChecklistItemDone(string key)
    {
        var done = (ChecklistDone ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return done.Contains(key, StringComparer.Ordinal) ? this : this with { ChecklistDone = string.Join(',', [.. done, key]) };
    }
}

public interface ISettingsStore
{
    UserSettings Current { get; }

    event EventHandler<UserSettings>? Changed;

    void Save(UserSettings settings);
}

public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly ILogger _logger;

    public SettingsStore(AppPaths paths, ILogger<SettingsStore>? logger = null)
    {
        _path = paths.SettingsFile;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        Current = Load();
    }

    public UserSettings Current { get; private set; }

    public event EventHandler<UserSettings>? Changed;

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Current = Normalize(settings);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the settings to {Path}", _path);
        }

        Changed?.Invoke(this, Current);
    }

    private UserSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return Normalize(JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(_path), Json) ?? new UserSettings());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Ignoring invalid settings file {Path}", _path);
        }

        return new UserSettings();
    }

    /// <summary>Drops unsupported values (unknown language, empty folder).</summary>
    internal static UserSettings Normalize(UserSettings settings) => settings with
    {
        Language = settings.Language is not null && UserSettings.SupportedLanguages.Contains(settings.Language) ? settings.Language : null,
        DownloadFolder = string.IsNullOrWhiteSpace(settings.DownloadFolder) ? null : settings.DownloadFolder,
    };
}
