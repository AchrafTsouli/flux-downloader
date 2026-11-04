using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Rule";
    public bool Enabled { get; set; } = true;

    // Conditions (null/empty = matches any)
    public string? DomainPattern { get; set; }
    public string? ExtensionPattern { get; set; } // e.g. "iso,mkv,zip"
    public string? UrlRegex { get; set; }

    // Actions (applied if matched)
    public string? TargetFolder { get; set; }
    public string? Category { get; set; }
    public string? Queue { get; set; }
    public int? Connections { get; set; }
    public long? SpeedLimitBytes { get; set; }

    public bool Matches(string url, string fileName)
    {
        if (!Enabled) return false;

        if (!string.IsNullOrWhiteSpace(DomainPattern))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (!uri.Host.Contains(DomainPattern.Trim(), StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(ExtensionPattern))
        {
            string ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            var allowed = ExtensionPattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                          .Select(e => e.TrimStart('.').ToLowerInvariant());
            if (!allowed.Contains(ext)) return false;
        }

        if (!string.IsNullOrWhiteSpace(UrlRegex))
        {
            try
            {
                if (!Regex.IsMatch(url, UrlRegex, RegexOptions.IgnoreCase)) return false;
            }
            catch { return false; }
        }

        return true;
    }

    public void Apply(DownloadJob job)
    {
        if (!string.IsNullOrWhiteSpace(Category)) job.Category = Category;
        if (!string.IsNullOrWhiteSpace(Queue)) job.Queue = Queue;
        if (Connections.HasValue && Connections.Value is 1 or 2 or 4 or 8 or 16 or 32)
            job.RequestedConnections = Connections.Value;
        if (SpeedLimitBytes.HasValue) job.SpeedLimitBytes = SpeedLimitBytes.Value;
        if (!string.IsNullOrWhiteSpace(TargetFolder) && Directory.Exists(TargetFolder))
        {
            job.Destination = Path.Combine(TargetFolder, Path.GetFileName(job.Destination));
        }
    }
}
