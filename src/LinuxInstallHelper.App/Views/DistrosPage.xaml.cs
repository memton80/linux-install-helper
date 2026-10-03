using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class DistrosPage : Page
{
    public DistrosPage()
    {
        ViewModel = App.GetService<DistrosViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DistrosViewModel ViewModel { get; }

    private void OnDistroClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DistroItemViewModel item)
        {
            ViewModel.Select(item);
        }
    }
}
