using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        ViewModel = App.GetService<GuideViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public GuideViewModel ViewModel { get; }
}
