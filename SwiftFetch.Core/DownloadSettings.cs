namespace SwiftFetch.Core;

public sealed class DownloadSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int DefaultConnections { get; set; } = 4;
    public int MaximumActiveDownloads { get; set; } = 3;
    public int MaximumRetries { get; set; } = 3;
    public long SavedGlobalSpeedLimitBytes { get; set; } = 512 * 1024;
    public long GlobalSpeedLimitBytes { get; set; } = 0; // 0 = unlimited
    public bool VerifySavedBytesWithoutValidator { get; set; }
    public bool ConfirmDeleteDownloads { get; set; } = true;
    public bool ShowDownloadCompleteDialog { get; set; } = true;
    public bool ShowStartDownloadDialog { get; set; } = true;
    public bool MonitorClipboard { get; set; }
    public bool QueueNewDownloads { get; set; }
    public bool ShowProgressOnStart { get; set; } = true;
    public bool PreserveServerDate { get; set; }
    public bool IgnoreLastModifiedChanges { get; set; }
    public bool PlayCompletionSound { get; set; }
    public bool PlayErrorSound { get; set; }
    public string ManualUserAgent { get; set; } = DownloadEngine.DefaultUserAgent;
    public string TemporaryDirectory { get; set; } = "";
    public string CaptureExtensions { get; set; } = "zip rar 7z tar gz iso exe msi bin mp4 mkv avi webm mp3 flac pdf apk";
    public string ExcludedSites { get; set; } = "";
    public string ExcludedAddresses { get; set; } = "";
    public bool BrowserCaptureEnabled { get; set; } = true;
    public bool BrowserContextMenuEnabled { get; set; } = true;
    public bool BrowserMediaPanelEnabled { get; set; }
    public bool ShowQueueSelection { get; set; } = true;
    public bool ShowBatchQueueSelection { get; set; } = true;
    public bool ProgressDetailsVisible { get; set; } = true;
    public bool ProgressSpeedTabVisible { get; set; } = true;
    public string BypassKey { get; set; } = "Alt";
    public string ForceKey { get; set; } = "Shift";
    public string DuplicatePolicy { get; set; } = "New copy";
    public string ProxyAddress { get; set; } = "";
    public string ProxyUsername { get; set; } = "";
    public string ProxyPassword { get; set; } = "";
    public string VpnProfile { get; set; } = "";
    public bool ConnectVpnBeforeDownload { get; set; }
    public bool DisconnectVpnWhenIdle { get; set; }
    public string VirusScanner { get; set; } = "";
    public string VirusScannerArguments { get; set; } = "{file}";
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 12;
    public bool ShowCategories { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool DarkMode { get; set; } = true;
    public bool ShowTrayIcon { get; set; } = true;
    public string Language { get; set; } = "en";
    public string UpdateFeedUrl { get; set; } = "";
    public bool AutoCheckUpdates { get; set; } = true;
    public bool AutoUpdateVideoTools { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public DateTimeOffset? LastVideoToolsCheck { get; set; }
    public string HomePageUrl { get; set; } = "";
    public Dictionary<string, bool> ColumnVisibility { get; set; } = [];
    public List<SiteLogin> SiteLogins { get; set; } = [];
    public List<DownloadQueueDefinition> Queues { get; set; } = [new(), new() { Name = "Synchronization queue", Synchronization = true }];
    public Dictionary<string, string> CategoryExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CategoryDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AutomationRule> Rules { get; set; } = [];

    public string GetDirectoryForCategory(string category)
    {
        if (CategoryDirectories.TryGetValue(category, out var dir) && !string.IsNullOrWhiteSpace(dir))
        {
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
        string fallback = Path.Combine(DownloadDirectory, category);
        try { Directory.CreateDirectory(fallback); } catch { }
        return fallback;
    }
    public void Validate()
    {
        if (MaximumActiveDownloads is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(MaximumActiveDownloads), "Use 1–16 simultaneous downloads.");
        if (MaximumRetries is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(MaximumRetries), "Use 0–10 automatic retries.");
        if (DefaultConnections is not (1 or 2 or 4 or 8 or 16)) throw new ArgumentOutOfRangeException(nameof(DefaultConnections), "Use 1, 2, 4, 8 or 16 connections.");
        if (!Path.IsPathFullyQualified(DownloadDirectory)) throw new ArgumentException("The download folder must be an absolute path.");
        if (CategoryDirectories.Keys.Concat(CategoryExtensions.Keys).Any(name => string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) throw new ArgumentException("Category names must be valid folder names.");
        if (CategoryDirectories.Values.Any(folder => !Path.IsPathFullyQualified(folder))) throw new ArgumentException("Category folders must use absolute paths.");
        if (GlobalSpeedLimitBytes < 0) throw new ArgumentException("Speed limit must be positive or zero.");
        if (TemporaryDirectory.Length > 0 && !Path.IsPathFullyQualified(TemporaryDirectory)) throw new ArgumentException("Temporary folder must be an absolute path.");
        if (ProxyAddress.Length > 0 && (!Uri.TryCreate(ProxyAddress, UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https" or "socks4" or "socks4a" or "socks5") || proxy.UserInfo.Length > 0 || proxy.Host.Length == 0)) throw new ArgumentException("Use a proxy address such as http://host:8080 or socks5://host:1080, with credentials in their fields.");
        if (ManualUserAgent.Any(char.IsControl) || ManualUserAgent.Length > 8192) throw new ArgumentException("Invalid User-Agent.");
        if (!double.IsFinite(FontSize) || FontSize is < 9 or > 24) throw new ArgumentException("Font size must be 9–24.");
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Any(char.IsControl)) throw new ArgumentException("Enter a valid font family name.");
        if (Queues.Count == 0 || Queues.Any(q => string.IsNullOrWhiteSpace(q.Name) || q.MaximumActive is < 1 or > 16 || q.Retries is < 0 or > 10 || q.IntervalMinutes is < 1 or > 1440 || q.Weekdays.Any(day => !Enum.IsDefined(day)) || q.PowerWhenDone is not (QueueCompletionAction.None or QueueCompletionAction.Sleep or QueueCompletionAction.Shutdown or QueueCompletionAction.Hibernate) || q.StartTime is { } start && !TimeOnly.TryParse(start, out _) || q.StopTime is { } stop && !TimeOnly.TryParse(stop, out _)) || Queues.Select(q => q.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Queues.Count || Queues.Select(q => q.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Queues.Count) throw new ArgumentException("Queues need unique names, 1–16 slots, and valid HH:mm schedule times.");
        if (UpdateFeedUrl.Length > 0 && (!Uri.TryCreate(UpdateFeedUrl,UriKind.Absolute,out var feed) || feed.Scheme != "https" || feed.UserInfo.Length > 0)) throw new ArgumentException("Update feed must use HTTPS without embedded credentials.");
        if (HomePageUrl.Length > 0 && (!Uri.TryCreate(HomePageUrl,UriKind.Absolute,out var home) || home.Scheme is not ("http" or "https") || home.UserInfo.Length > 0)) throw new ArgumentException("Home page must use HTTP/HTTPS without embedded credentials.");
        foreach (var login in SiteLogins) if (!Uri.TryCreate(login.Origin, UriKind.Absolute, out var origin) || origin.Scheme is not ("http" or "https") || origin.AbsolutePath != "/" || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0) throw new ArgumentException("Site login origin must be an exact HTTP/HTTPS origin, e.g. https://example.com/.");
    }

    public string CategoryForFile(string name) => CategoryExtensions.FirstOrDefault(pair => pair.Value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Any(ext => ext.TrimStart('.').Equals(Path.GetExtension(name).TrimStart('.'), StringComparison.OrdinalIgnoreCase))).Key ?? DownloadCategories.ForFile(name);
    public DownloadSession ManualSession(Uri uri)
    {
        string origin = uri.GetLeftPart(UriPartial.Authority);
        var login = SiteLogins.FirstOrDefault(value => value.Enabled && (value.Username.Length > 0 || value.Password.Length > 0) && new Uri(value.Origin).GetLeftPart(UriPartial.Authority) == origin);
        return new() { Origin = origin + "/", UserAgent = string.IsNullOrWhiteSpace(ManualUserAgent) ? null : ManualUserAgent, Authorization = login == null ? null : "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(login.Username + ":" + login.Password)) };
    }
}

public sealed class SiteLogin
{
    public bool Enabled { get; set; } = true;
    public string Origin { get; set; } = "https://example.com/";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public sealed class DownloadQueueDefinition
{
    public string Name { get; set; } = "Default";
    public int MaximumActive { get; set; } = 3;
    public string? StartTime { get; set; }
    public string? StopTime { get; set; }
    public bool Daily { get; set; } = true;
    public bool Enabled { get; set; }
    public string Label { get; set; } = "";
    public override string ToString() => DisplayName;
    public string DisplayName => Label.Length > 0 ? Label : Name == "Default" ? "Main download queue" : Name;
    public bool StartOnStartup { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public List<DayOfWeek> Weekdays { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public int? Retries { get; set; }
    public bool Synchronization { get; set; }
    public int IntervalMinutes { get; set; } = 60;
    public bool ExitWhenDone { get; set; }
    public QueueCompletionAction PowerWhenDone { get; set; }
    public bool ForcePowerAction { get; set; }
    public bool DisconnectVpnWhenDone { get; set; }
    public string OpenFileWhenDone { get; set; } = "";
    public List<string> FileOrder { get; set; } = [];
}

public sealed class EngineOptions
{
    public bool VerifySavedBytesWithoutValidator { get; set; }
    public int MaximumRetries { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan HeadersTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(45);
    public int MaximumConnections { get; set; } = 16;
    public int MaximumConnectionsPerHost { get; set; } = 32;
    public long MinimumSplitBytes { get; set; } = 524288;
    public int MaximumSegments { get; set; } = 128;
    public BandwidthLimiter GlobalLimiter { get; set; } = new(0);
    public System.Net.IWebProxy? Proxy { get; set; }
    public bool IgnoreLastModifiedChanges { get; set; }
    public bool PreserveServerDate { get; set; }
    public string VirusScanner { get; set; } = "";
    public string VirusScannerArguments { get; set; } = "{file}";
}
