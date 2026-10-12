using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class DonePage : Page
{
    public DonePage()
    {
        ViewModel = App.GetService<DoneViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DoneViewModel ViewModel { get; }

    /// <summary>The hash and the key fingerprint are only shown in the advanced mode; otherwise the same result in plain words.</summary>
    public AdvancedModeState Advanced { get; } = App.GetService<AdvancedModeState>();
}
