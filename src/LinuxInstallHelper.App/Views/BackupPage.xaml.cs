using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class BackupPage : Page
{
    public BackupPage()
    {
        ViewModel = App.GetService<BackupViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public BackupViewModel ViewModel { get; }
}
