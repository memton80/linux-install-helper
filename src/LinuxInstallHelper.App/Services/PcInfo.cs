using LinuxInstallHelper.Core.Readiness;
using Microsoft.Extensions.Logging;

namespace LinuxInstallHelper.App.Services;

/// <summary>
/// What is known about this computer, read once in the background and shared by the pages (questionnaire, drive, result,
/// guide, readiness): reading WMI takes a moment.
/// </summary>
public sealed class PcInfo
{
    private readonly IPcProbe _probe;
    private readonly ILogger<PcInfo> _logger;
    private Task<PcFacts>? _facts;

    public PcInfo(IPcProbe probe, ILogger<PcInfo> logger)
    {
        _probe = probe;
        _logger = logger;
    }

    /// <summary>The facts, read on the first call. Never fails: what cannot be read stays unknown.</summary>
    public Task<PcFacts> GetAsync() => _facts ??= LoadAsync();

    /// <summary>Reads the facts again (after a setting was changed).</summary>
    public Task<PcFacts> RefreshAsync() => _facts = LoadAsync();

    /// <summary>The boot menu and setup keys of this computer's maker, when known.</summary>
    public static FirmwareKeys? KeysOf(PcFacts facts) =>
        FirmwareKeys.For(facts.Manufacturer, facts.Model, facts.Family, facts.BoardManufacturer);

    /// <summary>Turns Windows fast startup off, then reads the facts again.</summary>
    public Task<PcFacts> DisableFastStartupAsync()
    {
        _probe.DisableFastStartup();
        return RefreshAsync();
    }

    private async Task<PcFacts> LoadAsync()
    {
        try
        {
            return await _probe.ProbeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the facts of this computer");
            return PcFacts.Basic();
        }
    }
}
