using LinuxInstallHelper.Core.Readiness;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// What the application does for the user about their computer, from the readiness, result, guide and troubleshooting
/// pages: restart into the boot options or the UEFI settings, turn fast startup off, open the right Windows page.
/// </summary>
public sealed class ReadinessActions
{
    public const string RecoveryKeyUrl = "https://account.microsoft.com/devices/recoverykey";

    private readonly PcInfo _pc;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _localizer;
    private readonly AppBusyState _busy;
    private readonly ILogger<ReadinessActions> _logger;

    public ReadinessActions(PcInfo pc, IDialogService dialogs, ILocalizer localizer, AppBusyState busy, ILogger<ReadinessActions> logger)
    {
        _pc = pc;
        _dialogs = dialogs;
        _localizer = localizer;
        _busy = busy;
        _logger = logger;
    }

    /// <summary>Runs <paramref name="action"/>. True when the facts of the computer changed and the checks must be shown again.</summary>
    public async Task<bool> RunAsync(ReadinessAction action)
    {
        switch (action)
        {
            case ReadinessAction.OpenFirmwareSettings:
                await RestartToFirmwareAsync();
                return false;
            case ReadinessAction.DisableFastStartup:
                return await DisableFastStartupAsync();
            case ReadinessAction.OpenRecoveryKeyPage:
                SystemActions.OpenUrl(RecoveryKeyUrl);
                return false;
            case ReadinessAction.OpenStorageSettings:
                SystemActions.OpenWindowsSettings("storagesense");
                return false;
            default:
                return false;
        }
    }

    /// <summary>After a confirmation, restarts into the Windows startup options, where "Use a device" starts from the drive.</summary>
    public Task RestartToDriveAsync() => RestartAsync(SystemActions.StartupOptions, "Restart_Drive");

    /// <summary>After a confirmation, restarts into the UEFI settings (to turn Secure Boot off).</summary>
    public Task RestartToFirmwareAsync() => RestartAsync(SystemActions.FirmwareSettings, "Restart_Firmware");

    /// <summary>The sentence that tells which keys open the boot menu and the settings of this computer.</summary>
    public string DescribeKeys(FirmwareKeys? keys)
    {
        if (keys is null)
        {
            return _localizer.Get("Boot_KeysUnknown");
        }

        var text = keys switch
        {
            { Note: "Hold", Setup: { } setup } => _localizer.Format("Boot_KeysHold", keys.Vendor, KeyName(keys.BootMenu), KeyName(setup)),
            { Setup: null } => _localizer.Format("Boot_KeysNoSetup", keys.Vendor, KeyName(keys.BootMenu)),
            _ => _localizer.Format("Boot_Keys", keys.Vendor, KeyName(keys.BootMenu), KeyName(keys.Setup)),
        };

        return keys.Note is { } note && note != "Hold" ? $"{text} {_localizer.Get("Boot_Note_" + note)}" : text;
    }

    // F1…F12 are the same in every language; Esc, Del and the buttons of a tablet are translated.
    private string KeyName(string? key) =>
        key is null ? string.Empty
        : key.Length > 1 && key[0] == 'F' && key[1..].All(char.IsAsciiDigit) ? key
        : _localizer.Get("Key_" + key);

    private async Task<bool> DisableFastStartupAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get("FastStartup_ConfirmTitle"),
            _localizer.Get("FastStartup_ConfirmMessage"),
            _localizer.Get("FastStartup_ConfirmPrimary"),
            _localizer.Get("Dialog_Cancel"));
        if (!confirmed)
        {
            return false;
        }

        try
        {
            await _pc.DisableFastStartupAsync();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Could not turn fast startup off");
            await _dialogs.ShowMessageAsync(_localizer.Get("FastStartup_FailedTitle"), _localizer.Get("FastStartup_Failed"));
            return false;
        }
    }

    private async Task RestartAsync(string destination, string texts)
    {
        if (_busy.IsBusy)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            _localizer.Get(texts + "Title"),
            _localizer.Get(texts + "Message"),
            _localizer.Get(texts + "Primary"),
            _localizer.Get("Dialog_Cancel"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        _logger.LogInformation("Restarting the computer ({Destination})", destination);
        bool restarting;
        try
        {
            restarting = await SystemActions.RestartAsync(destination);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not restart the computer");
            restarting = false;
        }

        if (!restarting)
        {
            await _dialogs.ShowMessageAsync(_localizer.Get("Restart_FailedTitle"), _localizer.Get(texts + "Failed"));
        }
    }
}
