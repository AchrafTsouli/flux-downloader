namespace SwiftFetch.Core;

/// <summary>Time-based display smoothing; never affects transfer limits or saved bytes.</summary>
public sealed class TransferRateDisplay(TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private const double SmoothingSeconds = 2;
    private double rate;
    private long timestamp;
    private bool initialized;

    public void Update(double bytesPerSecond)
    {
        double sample = double.IsFinite(bytesPerSecond) ? Math.Max(0, bytesPerSecond) : 0;
        long now = clock.GetTimestamp();
        if (!initialized)
        {
            if (sample == 0) return;
            rate = sample;
            initialized = true;
        }
        else
        {
            double seconds = Math.Max(0, clock.GetElapsedTime(timestamp, now).TotalSeconds);
            double weight = 1 - Math.Exp(-seconds / SmoothingSeconds);
            rate += weight * (sample - rate);
        }
        timestamp = now;
    }

    public double BytesPerSecond
    {
        get
        {
            if (!initialized) return 0;
            double age = clock.GetElapsedTime(timestamp).TotalSeconds;
            // A stalled connection must not keep its last healthy speed forever.
            if (age >= 10) return 0;
            return rate * Math.Exp(-Math.Max(0, age - 1) / SmoothingSeconds);
        }
    }

    public void Reset() { rate = 0; initialized = false; }
}
