using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstallHelper.Core.Workflow;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>One line of the steps list.</summary>
public sealed partial class StageItemViewModel : ObservableObject
{
    public StageItemViewModel(CreationStage stage, string label)
    {
        Stage = stage;
        Label = label;
    }

    public CreationStage Stage { get; }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(IconBrush), nameof(IsRunning), nameof(IsNotRunning), nameof(LabelOpacity))]
    private StageState _state = StageState.Pending;

    [ObservableProperty]
    private string _detail = string.Empty;

    public bool IsRunning => State == StageState.Running;

    public bool IsNotRunning => State != StageState.Running;

    public double LabelOpacity => State is StageState.Pending or StageState.Skipped ? 0.6 : 1.0;

    public string Glyph => State switch
    {
        StageState.Done => "",
        StageState.Failed => "",
        StageState.Skipped => "",
        _ => "",
    };

    public Brush IconBrush => (Brush)Application.Current.Resources[State switch
    {
        StageState.Done => "SystemFillColorSuccessBrush",
        StageState.Failed => "SystemFillColorCriticalBrush",
        StageState.Skipped => "TextFillColorTertiaryBrush",
        _ => "TextFillColorDisabledBrush",
    }];
}
