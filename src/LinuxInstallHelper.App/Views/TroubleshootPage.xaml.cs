using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class TroubleshootPage : Page
{
    public TroubleshootPage()
    {
        ViewModel = App.GetService<TroubleshootViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public TroubleshootViewModel ViewModel { get; }

    /// <summary>The technical details of an error are only shown in the advanced mode.</summary>
    public AdvancedModeState Advanced { get; } = App.GetService<AdvancedModeState>();
}
