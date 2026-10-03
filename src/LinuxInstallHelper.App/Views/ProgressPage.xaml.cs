using System.Collections.Specialized;
using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

public sealed partial class ProgressPage : Page
{
    public ProgressPage()
    {
        ViewModel = App.GetService<ProgressViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.Logs.CollectionChanged += OnLogsChanged;
        Unloaded += (_, _) => ViewModel.Logs.CollectionChanged -= OnLogsChanged;
    }

    public ProgressViewModel ViewModel { get; }

    // Keep the latest log line visible.
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && ViewModel.Logs.Count > 0)
        {
            LogList.ScrollIntoView(ViewModel.Logs[^1]);
        }
    }
}
