using LinuxInstallHelper.Core.Readiness;

namespace LinuxInstallHelper.App.Services;

/// <summary>What the application says about the processor an image is made for, compared with the one of this PC.</summary>
public static class ProcessorText
{
    /// <summary>"Intel or AMD" or "ARM".</summary>
    public static string Name(ILocalizer localizer, string architecture) => localizer.Get("Processor_" + architecture);

    /// <summary>
    /// Why a drive made from an image for <paramref name="architecture"/> will not start on this PC, or an empty string when
    /// it will (or when the processor of the image or of the PC is unknown). It never blocks: the drive may be for another PC.
    /// </summary>
    public static string Mismatch(ILocalizer localizer, string? architecture) =>
        PcReadiness.CannotStart(PcFacts.ThisProcessor, architecture) && PcFacts.ArchitectureOf(PcFacts.ThisProcessor) is { } machine
            ? localizer.Format("Architecture_Mismatch", Name(localizer, architecture!), Name(localizer, machine))
            : string.Empty;
}
