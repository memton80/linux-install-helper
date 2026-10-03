using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>One lesson of the Linux tour. <see cref="Windows"/>, <see cref="Linux"/> and <see cref="Command"/> may be empty.</summary>
public sealed record LinuxLesson(string Glyph, string Title, string Body, string Windows, string Linux, string Command);

/// <summary>
/// Short lessons on everyday Linux for Windows users. While the drive is being created they advance on their own
/// (<see cref="Play"/>); the Linux guide page lists them all.
/// </summary>
public sealed partial class LinuxTourViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan AutoAdvanceInterval = TimeSpan.FromSeconds(20);

    // Texts are Tour_{n}_Title and Tour_{n}_Body, plus Tour_{n}_Windows and Tour_{n}_Linux for a comparison
    // and Tour_{n}_Command for an example to type.
    private static readonly (string Glyph, bool Comparison, bool Command)[] Specs =
    [
        ("\uE768", false, false), // Try it from the drive
        ("\uE7F4", true, false),  // The desktop
        ("\uE719", true, false),  // Installing software
        ("\uE895", true, false),  // Updates
        ("\uE8B7", true, false),  // Files and drives
        ("\uE71D", true, false),  // Equivalent applications
        ("\uE7FC", true, false),  // Games and Windows software
        ("\uE756", true, true),   // The terminal
        ("\uE7EF", true, true),   // Administrator rights
        ("\uE897", false, false), // Getting help
    ];

    private readonly ILocalizer _localizer;
    private readonly IUiDispatcher? _dispatcher;
    private Timer? _timer;
    private int _index;
    private LinuxLesson _current;
    private string _position = string.Empty;
    private bool _isPlaying;

    /// <param name="dispatcher">Needed only to advance automatically.</param>
    public LinuxTourViewModel(ILocalizer localizer, IUiDispatcher? dispatcher = null)
    {
        _localizer = localizer;
        _dispatcher = dispatcher;
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

    /// <summary>The lessons advance on their own every <see cref="AutoAdvanceInterval"/>.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayLabel));
                OnPropertyChanged(nameof(PlayGlyph));
            }
        }
    }

    public string PlayLabel => _localizer.Get(IsPlaying ? "Tour_Pause" : "Tour_Play");

    public string PlayGlyph => IsPlaying ? "\uE769" : "\uE768";

    public void Play()
    {
        if (_dispatcher is null)
        {
            return;
        }

        IsPlaying = true;
        RestartTimer();
    }

    public void Pause()
    {
        IsPlaying = false;
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Pause();

    // A lesson chosen by hand stays on screen for a whole interval.
    [RelayCommand]
    private void Next()
    {
        Show(_index + 1);
        RestartTimer();
    }

    [RelayCommand]
    private void Previous()
    {
        Show(_index - 1);
        RestartTimer();
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    private void RestartTimer()
    {
        if (!IsPlaying || _dispatcher is not { } dispatcher)
        {
            return;
        }

        _timer?.Dispose();
        _timer = new Timer(_ => dispatcher.Post(() =>
        {
            if (IsPlaying)
            {
                Show(_index + 1);
            }
        }), null, AutoAdvanceInterval, AutoAdvanceInterval);
    }

    // The tour loops: after the last lesson comes the first one again.
    private void Show(int index)
    {
        _index = (index + Lessons.Count) % Lessons.Count;
        Current = Lessons[_index];
        Position = _localizer.Format("Tour_Position", _index + 1, Lessons.Count);
    }
}
