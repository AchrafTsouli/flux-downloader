using System.Diagnostics;

namespace SwiftFetch.Core;

public sealed class BandwidthLimiter
{
    private readonly object gate = new();
    private long bytesPerSecond;
    private double availableTokens;
    private long lastTimestamp;
    private long revision;

    public BandwidthLimiter(long bytesPerSecond = 0)
    {
        this.bytesPerSecond = bytesPerSecond;
        availableTokens = bytesPerSecond;
        lastTimestamp = Stopwatch.GetTimestamp();
    }

    public long BytesPerSecond
    {
        get => bytesPerSecond;
        set
        {
            lock (gate)
            {
                bytesPerSecond = Math.Max(0, value);
                availableTokens = bytesPerSecond;
                revision++;
            }
        }
    }

    public async ValueTask ThrottleAsync(int bytes, CancellationToken token = default)
    {
        if (bytes <= 0 || bytesPerSecond <= 0) return;

        double delayMs = 0;
        long configuredRevision;
        lock (gate)
        {
            configuredRevision = revision;
            long now = Stopwatch.GetTimestamp();
            double elapsedSeconds = (double)(now - lastTimestamp) / Stopwatch.Frequency;
            lastTimestamp = now;

            // Replenish tokens up to 1 second burst capacity
            availableTokens = Math.Min(bytesPerSecond, availableTokens + elapsedSeconds * bytesPerSecond);
            availableTokens -= bytes;

            if (availableTokens < 0)
            {
                delayMs = (-availableTokens / bytesPerSecond) * 1000.0;
            }
        }

        while (delayMs > 1 && Volatile.Read(ref revision) == configuredRevision)
        {
            int slice = (int)Math.Min(delayMs, 250);
            await Task.Delay(slice, token);
            delayMs -= slice;
        }
    }
}
