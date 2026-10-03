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

    // Show the suggestion as soon as it appears, below the questions.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdvisorViewModel.HasResult) && ViewModel.HasResult)
        {
            DispatcherQueue.TryEnqueue(() => ResultsPanel.StartBringIntoView());
        }
    }
}
