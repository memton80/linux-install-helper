using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class DrivePage : Page
{
    public DrivePage()
    {
        ViewModel = App.GetService<DriveViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DriveViewModel ViewModel { get; }

    private void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedDrive = (sender as ListView)?.SelectedItem as DriveItemViewModel;
}
