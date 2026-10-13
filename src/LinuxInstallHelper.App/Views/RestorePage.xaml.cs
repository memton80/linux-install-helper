using LinuxInstallHelper.App.Services;
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

    /// <summary>The technical details of an error are only shown in the advanced mode.</summary>
    public AdvancedModeState Advanced { get; } = App.GetService<AdvancedModeState>();

    private void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ViewModel.SelectedDrive = (sender as ListView)?.SelectedItem as DriveItemViewModel;
}
