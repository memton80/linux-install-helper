using CommunityToolkit.Mvvm.ComponentModel;

namespace LinuxInstallHelper.App.Services;

/// <summary>True while a USB drive is being created: navigation and closing are guarded.</summary>
public sealed partial class AppBusyState : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Cancels the running operation, if any.</summary>
    public CancellationTokenSource? CurrentOperation { get; set; }

    /// <summary>True when the drive is being written (cancelling leaves it unusable).</summary>
    public bool IsWriting { get; set; }
}
