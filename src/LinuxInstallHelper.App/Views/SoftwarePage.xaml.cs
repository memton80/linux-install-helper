using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class SoftwarePage : Page
{
    public SoftwarePage()
    {
        ViewModel = App.GetService<SoftwareViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public SoftwareViewModel ViewModel { get; }
}
