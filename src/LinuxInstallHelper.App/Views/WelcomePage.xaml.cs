using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        ViewModel = App.GetService<WelcomeViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public WelcomeViewModel ViewModel { get; }
}
