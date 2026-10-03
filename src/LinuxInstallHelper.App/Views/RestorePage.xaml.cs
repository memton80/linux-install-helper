using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class RestorePage : Page
{
    public RestorePage()
    {
        ViewModel = App.GetService<RestoreViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public RestoreViewModel ViewModel { get; }

    private void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedDrive = (sender as ListView)?.SelectedItem as DriveItemViewModel;
}
