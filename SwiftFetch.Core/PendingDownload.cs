namespace SwiftFetch.Core;

// Browser handoffs awaiting a user's choice are not library downloads.
public sealed class PendingDownload
{
    public string RequestId { get; set; } = "";
    public string Url { get; set; } = "";
    public string? FileName { get; set; }
    public DownloadSession? Session { get; set; }
    public VideoSelection? Video { get; set; }
    public void Validate()
    {
        if (!Guid.TryParse(RequestId, out _) || !Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || FileName?.Length > 1024)
            throw new InvalidDataException("Invalid pending browser download.");
        Session?.Validate(Url);
        Video?.Validate(Url);
    }
}
