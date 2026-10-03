using LinuxInstallHelper.Core.Disks;

namespace LinuxInstallHelper.Core.Writing;

public enum UsbWriteStage
{
    Preparing,
    Writing,
    Flushing,
    Verifying,
    Finished,
}

public sealed record UsbWriteProgress(UsbWriteStage Stage, long BytesDone, long TotalBytes, double BytesPerSecond = 0, TimeSpan? Remaining = null)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)BytesDone / TotalBytes, 0, 1) : 0;
}

/// <param name="ImagePath">ISO file to write.</param>
/// <param name="Target">The disk the user confirmed. It is checked again just before writing.</param>
/// <param name="VerifyAfterWrite">Read the drive back and compare it with the image.</param>
public sealed record UsbWriteRequest(string ImagePath, DiskInfo Target, bool VerifyAfterWrite = true);

public sealed record UsbWriteResult(long BytesWritten, string Sha256, bool Verified);

public enum UsbWriteFailure
{
    /// <summary>The drive was unplugged or replaced since it was selected.</summary>
    TargetChanged,

    /// <summary>The drive is not (or no longer) an acceptable target.</summary>
    TargetRejected,

    /// <summary>The image does not fit on the drive.</summary>
    ImageTooLarge,

    /// <summary>A volume of the drive is in use and cannot be locked.</summary>
    VolumeBusy,

    /// <summary>Windows refused or failed the raw access.</summary>
    DeviceError,

    /// <summary>Windows dropped the writes without an error, typically because of security software. Nothing was written.</summary>
    WriteBlocked,

    /// <summary>What was read back from the drive differs from the image.</summary>
    VerificationFailed,
}

public sealed class UsbWriteException : Exception
{
    public UsbWriteException(UsbWriteFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public UsbWriteFailure Failure { get; }
}

/// <summary>
/// Writes a bootable image to a USB drive. Implementations must re-check the target right before
/// erasing it. The default implementation (<see cref="RawDiskWriter"/>) copies the ISO as-is; another tool
/// (Ventoy...) can be plugged in behind this interface.
/// </summary>
public interface IUsbWriter
{
    /// <summary>Short technical name, shown in the logs.</summary>
    string Name { get; }

    Task<UsbWriteResult> WriteAsync(UsbWriteRequest request, IProgress<UsbWriteProgress>? progress = null, CancellationToken cancellationToken = default);
}
