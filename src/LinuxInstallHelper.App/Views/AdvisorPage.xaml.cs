using System.ComponentModel;
using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class AdvisorPage : Page
{
    public AdvisorPage()
    {
        ViewModel = App.GetService<AdvisorViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelChanged;
    }

    public AdvisorViewModel ViewModel { get; }

    // The suggestion replaces the questions (and the other way round): start reading it from the top.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdvisorViewModel.HasResult))
        {
            DispatcherQueue.TryEnqueue(() => PageScroll.ChangeView(null, 0, null, disableAnimation: true));
        }
    }
}
