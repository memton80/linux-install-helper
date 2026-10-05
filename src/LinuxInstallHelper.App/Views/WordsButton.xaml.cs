using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinuxInstallHelper.App.Views;

/// <summary>A "?" button that explains difficult words in plain words. Hidden when there is no word to explain.</summary>
public sealed partial class WordsButton : UserControl
{
    public static readonly DependencyProperty WordsProperty =
        DependencyProperty.Register(nameof(Words), typeof(IReadOnlyList<ExplainedWord>), typeof(WordsButton), new PropertyMetadata(null, OnWordsChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(WordsButton), new PropertyMetadata(string.Empty));

    public WordsButton()
    {
        Title = App.GetService<ILocalizer>().Get("Words_Title");
        InitializeComponent();
        UpdateVisibility();
    }

    public string Title { get; }

    public IReadOnlyList<ExplainedWord>? Words
    {
        get => (IReadOnlyList<ExplainedWord>?)GetValue(WordsProperty);
        set => SetValue(WordsProperty, value);
    }

    /// <summary>Tooltip and accessible name of the button.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnWordsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WordsButton)d).UpdateVisibility();

    private void UpdateVisibility() => Visibility = Words is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
}
