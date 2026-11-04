internal sealed class RateTestClock : TimeProvider
{
    private long milliseconds;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => milliseconds;
    public void Advance(double seconds) => milliseconds += (long)(seconds * 1000);
}
