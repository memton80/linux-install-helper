using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstallHelper.App.Services;
using LinuxInstallHelper.Core.Tour;

namespace LinuxInstallHelper.App.ViewModels;

/// <summary>A numbered step of a lesson: the same number is drawn on its picture.</summary>
public sealed record LessonStep(string Number, string Text);

/// <summary>
/// One lesson of the Linux tour, in the current language. <see cref="Image"/>, the path of its picture, may be null;
/// <see cref="Windows"/>, <see cref="Linux"/> and <see cref="Command"/> may be empty.
/// </summary>
public sealed record LinuxLesson(
    string Glyph,
    string Title,
    string Body,
    string? Image,
    IReadOnlyList<LessonStep> Steps,
    string Windows,
    string Linux,
    string Command)
{
    /// <summary>The pictures of the tour, copied next to the application from <c>tour/images</c>.</summary>
    public static readonly string PictureFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Tour");

    public bool HasImage => Image is not null;

    /// <summary>Builds the lesson; a lesson whose picture is missing shows its icon instead.</summary>
    /// <param name="language">Two-letter language code.</param>
    public static LinuxLesson From(TourLesson lesson, string language) => new(
        lesson.Glyph,
        lesson.Title.Get(language),
        lesson.Body.Get(language),
        lesson.ImageFile(language) is { } file && File.Exists(Path.Combine(PictureFolder, file)) ? Path.Combine(PictureFolder, file) : null,
        lesson.Steps.Select((step, i) => new LessonStep((i + 1).ToString(CultureInfo.CurrentCulture), step.Get(language))).ToList(),
        lesson.Windows?.Get(language) ?? string.Empty,
        lesson.Linux?.Get(language) ?? string.Empty,
        lesson.Command?.Get(language) ?? string.Empty);
}

/// <summary>
/// Short lessons for the distribution being written, from starting on the drive to everyday use. While the drive is
/// being created they advance on their own (<see cref="Play"/>); the Linux guide page lists them all.
/// </summary>
public sealed partial class LinuxTourViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan AutoAdvanceInterval = TimeSpan.FromSeconds(30);

    private readonly ILocalizer _localizer;
    private readonly TourBook _book;
    private readonly IUiDispatcher? _dispatcher;
    private Timer? _timer;
    private int _index;
    private IReadOnlyList<LinuxLesson> _lessons;
    private LinuxLesson _current;
    private string _position = string.Empty;
    private string _header;
    private string _subtitle;
    private bool _isPlaying;

    /// <param name="dispatcher">Needed only to advance automatically.</param>
    public LinuxTourViewModel(ILocalizer localizer, TourBook book, IUiDispatcher? dispatcher = null)
    {
        _localizer = localizer;
        _book = book;
        _dispatcher = dispatcher;
        _lessons = LessonsFor(book, null);
        _current = _lessons[0];
        _header = localizer.Get("Tour_Header");
        _subtitle = localizer.Get("Tour_Subtitle");
        Show(0);
    }

    public IReadOnlyList<LinuxLesson> Lessons
    {
        get => _lessons;
        private set => SetProperty(ref _lessons, value);
    }

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

    /// <summary>"While you wait, discover Ubuntu".</summary>
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
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

    public string PlayGlyph => IsPlaying ? "" : "";

    /// <summary>The lessons of a distribution in the current language: its own tour, or the generic one.</summary>
    public static IReadOnlyList<LinuxLesson> LessonsFor(TourBook book, string? distroId)
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return book.For(distroId).Select(lesson => LinuxLesson.From(lesson, language)).ToList();
    }

    /// <summary>Starts the tour of a distribution (null for a local image) from its first lesson.</summary>
    /// <param name="name">Name of the distribution, shown in the header.</param>
    public void Use(string? distroId, string? name)
    {
        var own = _book.Has(distroId) && !string.IsNullOrWhiteSpace(name);
        Header = own ? _localizer.Format("Tour_HeaderFor", name) : _localizer.Get("Tour_Header");
        Subtitle = _localizer.Get(own ? "Tour_SubtitleFor" : "Tour_Subtitle");
        Lessons = LessonsFor(_book, distroId);
        Show(0);
        RestartTimer();
    }

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
