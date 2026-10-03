using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>One lesson of the Linux tour. <see cref="Windows"/>, <see cref="Linux"/> and <see cref="Command"/> may be empty.</summary>
public sealed record LinuxLesson(string Glyph, string Title, string Body, string Windows, string Linux, string Command);

/// <summary>Short lessons on everyday Linux for Windows users, browsed while the drive is being created.</summary>
public sealed partial class LinuxTourViewModel : ObservableObject
{
    // Texts are Tour_{n}_Title and Tour_{n}_Body, plus Tour_{n}_Windows and Tour_{n}_Linux for a comparison
    // and Tour_{n}_Command for an example to type.
    private static readonly (string Glyph, bool Comparison, bool Command)[] Specs =
    [
        ("", false, false), // Try it from the drive
        ("", true, false),  // The desktop
        ("", true, false),  // Installing software
        ("", true, false),  // Updates
        ("", true, false),  // Files and drives
        ("", true, false),  // Equivalent applications
        ("", true, false),  // Games and Windows software
        ("", true, true),   // The terminal
        ("", true, true),   // Administrator rights
        ("", false, false), // Getting help
    ];

    private readonly ILocalizer _localizer;
    private int _index;
    private LinuxLesson _current;
    private string _position = string.Empty;

    public LinuxTourViewModel(ILocalizer localizer)
    {
        _localizer = localizer;
        Lessons = Specs.Select((spec, i) =>
        {
            var key = $"Tour_{i + 1}_";
            return new LinuxLesson(
                spec.Glyph,
                localizer.Get(key + "Title"),
                localizer.Get(key + "Body"),
                spec.Comparison ? localizer.Get(key + "Windows") : string.Empty,
                spec.Comparison ? localizer.Get(key + "Linux") : string.Empty,
                spec.Command ? localizer.Get(key + "Command") : string.Empty);
        }).ToList();
        _current = Lessons[0];
        Show(0);
    }

    public IReadOnlyList<LinuxLesson> Lessons { get; }

    public LinuxLesson Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    /// <summary>"3 / 10".</summary>
    public string Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

    [RelayCommand]
    private void Next() => Show(_index + 1);

    [RelayCommand]
    private void Previous() => Show(_index - 1);

    // The tour loops: after the last lesson comes the first one again.
    private void Show(int index)
    {
        _index = (index + Lessons.Count) % Lessons.Count;
        Current = Lessons[_index];
        Position = _localizer.Format("Tour_Position", _index + 1, Lessons.Count);
    }
}
