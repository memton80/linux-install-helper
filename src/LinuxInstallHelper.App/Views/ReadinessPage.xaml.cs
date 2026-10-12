using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class ReadinessPage : Page
{
    public ReadinessPage()
    {
        ViewModel = App.GetService<ReadinessViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ReadinessViewModel ViewModel { get; }
}
