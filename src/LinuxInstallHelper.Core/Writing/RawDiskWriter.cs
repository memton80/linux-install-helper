using LinuxInstallHelper.Core.Disks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LinuxInstallHelper.Core.Writing;

/// <summary>
/// Default <see cref="IUsbWriter"/>: copies the hybrid ISO byte for byte to the whole drive, like
/// <c>dd</c>, Fedora Media Writer or balenaEtcher. The distribution's own boot loaders are kept, so
/// UEFI and Secure Boot work exactly as designed by the distribution.
/// </summary>
public sealed class RawDiskWriter : IUsbWriter
{
    private readonly IDiskService _disks;
    private readonly IRawDiskAccess _access;
    private readonly ImageWriteEngine _engine;
    private readonly ILogger _logger;

    public RawDiskWriter(IDiskService disks, IRawDiskAccess access, ImageWriteEngine? engine = null, ILogger<RawDiskWriter>? logger = null)
    {
        _disks = disks;
        _access = access;
        _engine = engine ?? new ImageWriteEngine();
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public string Name => "raw";

    public async Task<UsbWriteResult> WriteAsync(UsbWriteRequest request, IProgress<UsbWriteProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var imageLength = new FileInfo(request.ImagePath).Length;
        progress?.Report(new UsbWriteProgress(UsbWriteStage.Preparing, 0, imageLength));

        // Last line of defense: the drive must still be the confirmed one, and still acceptable.
        var current = (await _disks.GetDisksAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(d => d.Number == request.Target.Number);
        var check = TargetGuard.Compare(request.Target, current);
        if (check != TargetCheck.Same)
        {
            throw new UsbWriteException(UsbWriteFailure.TargetChanged, "The USB drive was unplugged or replaced since it was selected. Nothing was written.");
        }

        var protectedDisks = await _disks.GetProtectedDisksAsync([request.ImagePath], cancellationToken).ConfigureAwait(false);
        var rejection = DiskFilter.EvaluateForImage(current!, imageLength, protectedDisks);
        if (rejection == DiskRejection.TooSmallForImage)
        {
            throw new UsbWriteException(UsbWriteFailure.ImageTooLarge, "The image does not fit on this USB drive.");
        }

        if (rejection != DiskRejection.None)
        {
            throw new UsbWriteException(UsbWriteFailure.TargetRejected, $"This drive cannot be used ({rejection}). Nothing was written.");
        }

        _logger.LogWarning("Writing {Image} ({Length} bytes) to disk {Number} \"{Name}\" ({Size} bytes, serial {Serial})",
            request.ImagePath, imageLength, current!.Number, current.FriendlyName, current.Size, current.SerialNumber);

        return await Task.Run(() => WriteCore(request, current, imageLength, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private UsbWriteResult WriteCore(UsbWriteRequest request, DiskInfo disk, long imageLength, IProgress<UsbWriteProgress>? progress, CancellationToken cancellationToken)
    {
        using var device = _access.Open(disk);
        if (device.Size != disk.Size)
        {
            throw new UsbWriteException(UsbWriteFailure.TargetChanged, "The size reported by the drive changed. Nothing was written.");
        }

        // Cosmetic (removes an old backup GPT): a refused write here must not hide whether the image itself can be written.
        try
        {
            ImageWriteEngine.WipeTail(device);
        }
        catch (UsbWriteException ex) when (ex.Failure == UsbWriteFailure.DeviceError)
        {
            _logger.LogWarning(ex, "Could not wipe the end of disk {Number}, writing the image anyway", disk.Number);
        }

        string sha256;
        using (var image = new FileStream(request.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan))
        {
            sha256 = _engine.Write(image, imageLength, device, progress, cancellationToken);
        }

        _logger.LogInformation("Wrote {Length} bytes to disk {Number} (SHA-256 {Hash})", imageLength, disk.Number, sha256);

        if (request.VerifyAfterWrite)
        {
            _engine.Verify(device, imageLength, sha256, progress, cancellationToken);
            _logger.LogInformation("Disk {Number} verified", disk.Number);
        }

        progress?.Report(new UsbWriteProgress(UsbWriteStage.Finished, imageLength, imageLength));
        return new UsbWriteResult(imageLength, sha256, request.VerifyAfterWrite);
    }
}
