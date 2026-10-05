using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class DistroDetailsPage : Page
{
    public DistroDetailsPage()
    {
        ViewModel = App.GetService<DistroDetailsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DistroDetailsViewModel ViewModel { get; }
}
