namespace SwiftFetch.Core;

public sealed record DownloadMetadata(string FinalUrl, long? TotalBytes, bool RangeSupported,
    string? SuggestedFileName, string? ContentType, string? ETag, DateTimeOffset? LastModified);
