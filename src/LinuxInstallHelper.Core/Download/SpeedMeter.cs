namespace LinuxInstallHelper.Core.Download;

/// <summary>Download speed over a sliding window, and the estimated remaining time.</summary>
public sealed class SpeedMeter
{
    private readonly TimeSpan _window;
    private readonly Queue<(TimeSpan Time, long Bytes)> _samples = new();

    public SpeedMeter(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Records the total number of bytes received at <paramref name="elapsed"/>.</summary>
    public void Add(TimeSpan elapsed, long totalBytes)
    {
        _samples.Enqueue((elapsed, totalBytes));
        while (_samples.Count > 2 && elapsed - _samples.Peek().Time > _window)
        {
            _samples.Dequeue();
        }
    }

    /// <summary>Bytes per second over the window, 0 until two samples exist.</summary>
    public double BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2)
            {
                return 0;
            }

            var first = _samples.Peek();
            var last = _samples.Last();
            var seconds = (last.Time - first.Time).TotalSeconds;
            return seconds <= 0 ? 0 : (last.Bytes - first.Bytes) / seconds;
        }
    }

    public TimeSpan? Remaining(long received, long? total)
    {
        var speed = BytesPerSecond;
        if (total is null || speed <= 0 || received >= total)
        {
            return null;
        }

        return TimeSpan.FromSeconds((total.Value - received) / speed);
    }

    public void Reset() => _samples.Clear();
}
