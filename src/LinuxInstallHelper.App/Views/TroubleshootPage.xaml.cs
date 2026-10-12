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
}
