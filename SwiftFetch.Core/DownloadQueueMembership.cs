namespace SwiftFetch.Core;

public static class DownloadQueueMembership
{
    public static bool IsUnfinished(DownloadState state) => state is
        DownloadState.Ready or DownloadState.Queued or DownloadState.Paused or
        DownloadState.Connecting or DownloadState.Resolving or DownloadState.Downloading or
        DownloadState.Retrying or DownloadState.Verifying or DownloadState.Finishing;

    public static bool IsFailure(DownloadState state) => state is
        DownloadState.Failed or DownloadState.AuthRequired or DownloadState.ExpiredUrl or DownloadState.NoDiskSpace;
}
