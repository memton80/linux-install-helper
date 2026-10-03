using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

/// <summary>One lesson of the Linux tour, shown on the progress page and on the Linux guide page.</summary>
public sealed partial class LinuxLessonView : UserControl
{
    public static readonly DependencyProperty LessonProperty =
        DependencyProperty.Register(nameof(Lesson), typeof(LinuxLesson), typeof(LinuxLessonView), new PropertyMetadata(null));

    public LinuxLessonView()
    {
        InitializeComponent();
    }

    public LinuxLesson? Lesson
    {
        get => (LinuxLesson?)GetValue(LessonProperty);
        set => SetValue(LessonProperty, value);
    }
}
