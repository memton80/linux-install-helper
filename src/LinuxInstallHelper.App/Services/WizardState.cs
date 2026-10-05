using LinuxInstallHelper.Core.Catalog;
using LinuxInstallHelper.Core.Disks;
using LinuxInstallHelper.Core.Workflow;

namespace LinuxInstallHelper.App.Services;

/// <summary>Choices made along the steps: image source, target drive, result.</summary>
public sealed class WizardState
{
    private bool _creationConfirmed;

    public Distro? Distro { get; private set; }

    public string? LocalIsoPath { get; private set; }

    public long LocalIsoSize { get; private set; }

    public string? LocalExpectedSha256 { get; private set; }

    public DiskInfo? Target { get; private set; }

    public CreationResult? LastResult { get; set; }

    /// <summary>The user has personal files and answered that they are not backed up yet.</summary>
    public bool BackupPending { get; set; }

    public bool IsLocalIso => LocalIsoPath is not null;

    public CreationSource? Source =>
        Distro is not null ? new DistroImageSource(Distro)
        : LocalIsoPath is not null ? new LocalImageSource(LocalIsoPath, LocalExpectedSha256)
        : null;

    /// <summary>Name shown in the next steps.</summary>
    public string SourceName =>
        Distro is not null ? $"{Distro.DisplayName} {Distro.Version}"
        : LocalIsoPath is not null ? Path.GetFileName(LocalIsoPath)
        : string.Empty;

    /// <summary>Approximate size of the image, used to hide drives that are too small.</summary>
    public long ImageSize => Distro?.Image.Size ?? LocalIsoSize;

    public void SelectDistro(Distro distro)
    {
        Distro = distro;
        LocalIsoPath = null;
        LocalExpectedSha256 = null;
        LocalIsoSize = 0;
        Target = null;
        LastResult = null;
        BackupPending = false;
        _creationConfirmed = false;
    }

    /// <summary>The user has confirmed <paramref name="target"/> on the drive page: one creation may start.</summary>
    public void ConfirmCreation(DiskInfo target)
    {
        Target = target;
        _creationConfirmed = true;
    }

    /// <summary>
    /// True once per confirmation: the progress page writes only right after it, never when it is shown again.
    /// </summary>
    public bool TakeCreationConfirmation()
    {
        var confirmed = _creationConfirmed;
        _creationConfirmed = false;
        return confirmed;
    }

    public void SelectLocalIso(string path, long size, string? expectedSha256)
    {
        Distro = null;
        LocalIsoPath = path;
        LocalIsoSize = size;
        LocalExpectedSha256 = string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256.Trim();
        Target = null;
        LastResult = null;
        BackupPending = false;
        _creationConfirmed = false;
    }

    public void Reset()
    {
        Distro = null;
        LocalIsoPath = null;
        LocalExpectedSha256 = null;
        LocalIsoSize = 0;
        Target = null;
        LastResult = null;
        BackupPending = false;
        _creationConfirmed = false;
    }
}
