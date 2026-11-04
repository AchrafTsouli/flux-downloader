using System.Text.Json.Serialization;
namespace SwiftFetch.Core;

public sealed class DownloadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string? BrowserRequestId { get; set; }
    public VideoSelection? Video { get; set; }
    public string? MediaWorkDirectory { get; set; }
    public long MediaDownloadedBytes { get; set; }
    public Dictionary<string,long> MediaTrackBytes { get; set; } = [];
    public Dictionary<string,long> MediaTrackTotals { get; set; } = [];
    public bool VideoSizeEstimated { get; set; }
    public HashSet<string> MediaTrackEstimates { get; set; } = [];
    public List<string> MediaTracks { get; set; } = [];
    [JsonIgnore] public string? MediaActiveTrack { get; set; }
    [JsonIgnore] public DownloadSession? Session { get; set; }
    [JsonIgnore] public object SegmentGate { get; } = new();
    public int DynamicSplits { get; set; }
    public string Destination { get; set; } = "";
    public bool NameIsCustom { get; set; }
    [JsonIgnore] public DownloadState State { get; set; } = DownloadState.Ready;
    public string Status
    {
        get => State.ToString();
        set => State = Enum.TryParse<DownloadState>(value, true, out var state) && Enum.IsDefined(state) ? state : throw new InvalidDataException("Unknown download state: " + value);
    }
    public string Error { get; set; } = "";
    public DownloadErrorKind ErrorKind { get; set; }
    public string? FinalUrl { get; set; }
    public string? ContentType { get; set; }
    public int? HttpStatus { get; set; }
    public DateTimeOffset? CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int RetriesPerformed { get; set; }
    public int? MaximumRetries { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string Category { get; set; } = "Other";
    public string Queue { get; set; } = "Default";
    public string? GrabberProject { get; set; }
    public string Description { get; set; } = "";
    public bool? ShowCompleteDialog { get; set; }
    public string? ExpectedSha256 { get; set; }
    public string? ActualSha256 { get; set; }
    public bool IntegrityVerified { get; set; }
    [JsonIgnore] public bool Retryable { get; set; }
    [JsonIgnore] public TimeSpan? RetryAfter { get; set; }
    public long? TotalBytes { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public bool RangeSupported { get; set; }
    public string ResumeNote { get; set; } = "";
    public int RequestedConnections { get; set; } = 4;
    public long? SpeedLimitBytes { get; set; }
    public string? TemporaryDirectory { get; set; }
    [JsonIgnore] public BandwidthLimiter FileLimiter { get; } = new();
    public List<DownloadSegment> Segments { get; set; } = [];
    public string FileName => Path.GetFileName(Destination);
    public string PartPath(int index) => TemporaryDirectory is { Length: > 0 } ? Path.Combine(TemporaryDirectory, $"{Id}.{index}.part") : Destination + $".{Id}.{index}.part";
    public string MergePath => Destination + $".{Id}.assembling";
}

public sealed class DownloadSegment
{
    public long Start { get; set; }
    public long? End { get; set; }
    public long? CommittedBytes { get; set; }
    [JsonIgnore] public long WrittenBytes { get; set; }
    [JsonIgnore] public bool Active { get; set; }
    [JsonIgnore] public bool Finished { get; set; }
    [JsonIgnore] public string StatusText { get; set; } = "Connecting...";
}

public record DownloadProgress(long Bytes, long? Total, double BytesPerSecond, int Connections);
