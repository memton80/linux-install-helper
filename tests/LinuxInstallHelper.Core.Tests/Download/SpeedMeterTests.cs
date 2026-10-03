using LinuxInstallHelper.Core.Download;

namespace LinuxInstallHelper.Core.Tests.Download;

public class SpeedMeterTests
{
    [Fact]
    public void Computes_speed_and_remaining_time()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(10));
        meter.Add(TimeSpan.Zero, 0);
        meter.Add(TimeSpan.FromSeconds(2), 2_000_000);

        Assert.Equal(1_000_000, meter.BytesPerSecond, 3);
        Assert.Equal(TimeSpan.FromSeconds(8), meter.Remaining(2_000_000, 10_000_000));
    }

    [Fact]
    public void Old_samples_leave_the_window()
    {
        var meter = new SpeedMeter(TimeSpan.FromSeconds(2));
        meter.Add(TimeSpan.Zero, 0);
        meter.Add(TimeSpan.FromSeconds(1), 10_000_000);
        meter.Add(TimeSpan.FromSeconds(5), 10_000_000);
        meter.Add(TimeSpan.FromSeconds(6), 11_000_000);

        Assert.Equal(1_000_000, meter.BytesPerSecond, 3);
    }

    [Fact]
    public void Unknown_until_two_samples()
    {
        var meter = new SpeedMeter();
        meter.Add(TimeSpan.Zero, 100);

        Assert.Equal(0, meter.BytesPerSecond);
        Assert.Null(meter.Remaining(100, 1000));
    }
}
