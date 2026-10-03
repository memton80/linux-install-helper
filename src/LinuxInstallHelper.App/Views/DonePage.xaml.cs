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
}
