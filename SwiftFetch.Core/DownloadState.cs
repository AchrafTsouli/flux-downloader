namespace SwiftFetch.Core;

public enum DownloadState
{
    Ready, Queued, Connecting, Resolving, Downloading, Paused, Retrying,
    Verifying, Finishing, Completed, Failed, Cancelled, AuthRequired, ExpiredUrl, NoDiskSpace
}

public enum DownloadErrorKind
{
    None, Network, Server, RateLimited, Authentication, ExpiredUrl, NotFound,
    Permission, DiskSpace, Storage, Protocol, Integrity, Unknown, AccessDenied
}

public sealed class DownloadFailureException(string message, DownloadErrorKind kind, bool retryable = false) : IOException(message)
{
    public DownloadErrorKind Kind { get; } = kind;
    public bool Retryable { get; } = retryable;
}
