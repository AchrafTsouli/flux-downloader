using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public sealed class VideoSelection
{
    // Presets let yt-dlp choose per video (playlists); explicit IDs come from a quality list.
    public const string PresetPrefix = "apex-";
    public static readonly int[] PresetHeights = [2160, 1440, 1080, 720, 480, 360];
    public static readonly string[] Extensions = ["mp4", "webm", "mkv", "m4a"];
    public string FormatId { get; set; } = "";
    public string Extension { get; set; } = "mp4";
    public string Label { get; set; } = "";
    public long? TotalBytes { get; set; }
    public bool Approximate { get; set; }
    public bool AudioOnly { get; set; }
    public bool NoAudio { get; set; }
    [JsonIgnore] public bool IsPreset => FormatId.StartsWith(PresetPrefix, StringComparison.Ordinal);
    [JsonIgnore] public int? PresetHeight => IsPreset && int.TryParse(FormatId[PresetPrefix.Length..], out int height) ? height : null;

    public static VideoSelection Preset(int? maxHeight, bool audioOnly) => audioOnly
        ? new() { FormatId = PresetPrefix + "audio", Extension = "m4a", Label = "Best audio only", AudioOnly = true }
        : maxHeight is int height
            ? new() { FormatId = PresetPrefix + height, Extension = "mp4", Label = $"Up to {height}p · video + audio" }
            : new() { FormatId = PresetPrefix + "best", Extension = "mp4", Label = "Best quality · video + audio" };

    public static bool ValidFormatId(string? id) =>
        id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.@-]{0,99}(\+[A-Za-z0-9][A-Za-z0-9_.@-]{0,99})?\z");

    public void Validate(string url)
    {
        bool format = IsPreset
            ? FormatId == PresetPrefix + "best" || FormatId == PresetPrefix + "audio" && AudioOnly || PresetHeight is int height && PresetHeights.Contains(height)
            : ValidFormatId(FormatId);
        if (VideoPages.Canonicalize(url) == null || !format || !Extensions.Contains(Extension) || Label == null || Label.Length > 200 || TotalBytes is < 0 || AudioOnly && NoAudio)
            throw new ArgumentException("Invalid video format selection.");
    }
}
public sealed record VideoFormat(string FormatId, string Label, string Extension, long? TotalBytes, bool Approximate, bool AudioOnly = false, bool NoAudio = false)
{
    public VideoSelection ToSelection() => new() { FormatId = FormatId, Label = Label, Extension = Extension, TotalBytes = TotalBytes, Approximate = Approximate, AudioOnly = AudioOnly, NoAudio = NoAudio };
}
public sealed record VideoCatalog(string Title, IReadOnlyList<VideoFormat> Formats, string? Site = null);
public sealed record VideoPlaylistEntry(string Url, string Title, double? DurationSeconds);
public sealed record VideoPlaylist(string Title, IReadOnlyList<VideoPlaylistEntry> Entries);
/// <summary>Either a single video's qualities or a playlist's entries.</summary>
public sealed record VideoProbe(VideoCatalog? Catalog, VideoPlaylist? Playlist);

public static class VideoPages
{
    private static readonly string[] YouTubeHosts = ["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "www.youtube-nocookie.com", "youtube-nocookie.com", "youtu.be"];
    // Single-video page shapes on well-known sites; any other page can still be tried explicitly.
    private static readonly (string Host, Regex Path)[] KnownPages =
    [
        ("vimeo.com", new(@"^/(?:channels/[\w-]+/|groups/[\w-]+/videos/)?\d+(?:/[\w]+)?/?$")),
        ("player.vimeo.com", new(@"^/video/\d+/?$")),
        ("dailymotion.com", new(@"^/video/[A-Za-z0-9]+/?$")),
        ("dai.ly", new(@"^/[A-Za-z0-9]+/?$")),
        ("x.com", new(@"^/[\w]+/status/\d+(?:/video/\d+)?/?$")),
        ("twitter.com", new(@"^/[\w]+/status/\d+(?:/video/\d+)?/?$")),
        ("facebook.com", new(@"^/(?:watch/?|reel/\d+/?|[\w.-]+/videos/(?:[\w.-]+/)?\d+/?|share/[rv]/[\w-]+/?)$")),
        ("fb.watch", new(@"^/[\w-]+/?$")),
        ("instagram.com", new(@"^/(?:[\w.]+/)?(?:p|reel|reels|tv)/[\w-]+/?$")),
        ("tiktok.com", new(@"^/@[\w.-]+/video/\d+/?$")),
        ("twitch.tv", new(@"^/(?:videos/\d+|[\w]+/clip/[\w-]+)/?$")),
        ("clips.twitch.tv", new(@"^/[\w-]+/?$")),
        ("reddit.com", new(@"^/r/[\w]+/comments/[\w]+(?:/[^/]*)?/?$")),
        ("streamable.com", new(@"^/[\w]+/?$")),
        ("bilibili.com", new(@"^/video/(?:BV[\w]+|av\d+)/?$")),
        ("rumble.com", new(@"^/v[\w-]+\.html$")),
        ("odysee.com", new(@"^/@[^/]+/[^/]+$")),
        ("ted.com", new(@"^/talks/[\w]+/?$")),
        ("soundcloud.com", new(@"^/[\w-]+/(?!sets/)[\w-]+/?$")),
    ];

    private static bool IsYouTubeHost(string host) => YouTubeHosts.Contains(host);
    private static bool HostMatches(string host, string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    private static Uri? Web(string? value) =>
        value is { Length: > 0 and <= 4096 } && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : null;

    /// <summary>Normalizes a single-video page or stream address, or returns null when it cannot be one.</summary>
    public static string? Canonicalize(string? value)
    {
        var uri = Web(value);
        if (uri == null) return null;
        string host = uri.IdnHost.ToLowerInvariant();
        if (IsYouTubeHost(host)) return YouTube(uri);
        return new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri;
    }

    private static string? YouTube(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort) return null;
        string host = uri.IdnHost.ToLowerInvariant();
        string? id = null;
        if (host is "youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (uri.AbsolutePath == "/watch") id = Query(uri, "v");
        else if (Regex.Match(uri.AbsolutePath, @"^/(?:shorts|embed|live)/([A-Za-z0-9_-]{11})/?$") is { Success: true } match) id = match.Groups[1].Value;
        return id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z") ? "https://www.youtube.com/watch?v=" + id : null;
    }
    private static string? Query(Uri uri, string name) =>
        uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)).FirstOrDefault(pair => pair[0] == name)?.ElementAtOrDefault(1);

    public static bool IsYouTube(string? value) => Web(value) is { } uri && IsYouTubeHost(uri.IdnHost.ToLowerInvariant());

    /// <summary>A YouTube playlist or channel listing, normalized; other sites are detected by yt-dlp itself.</summary>
    public static string? Playlist(string? value)
    {
        var uri = Web(value);
        if (uri == null || uri.Scheme != "https" || !IsYouTubeHost(uri.IdnHost.ToLowerInvariant())) return null;
        if (Query(uri, "list") is { } list && Regex.IsMatch(list, @"\A[A-Za-z0-9_-]{2,64}\z")) return "https://www.youtube.com/playlist?list=" + list;
        var channel = Regex.Match(uri.AbsolutePath, @"^/(@[\w.-]{1,100}|channel/UC[\w-]{22}|c/[\w.-]{1,100}|user/[\w.-]{1,100})(?:/(videos|shorts|streams|playlists))?/?$");
        return channel.Success ? $"https://www.youtube.com/{channel.Groups[1].Value}/{(channel.Groups[2].Success ? channel.Groups[2].Value : "videos")}" : null;
    }

    public static bool IsStreamManifest(string? value) =>
        Web(value) is { } uri && Regex.IsMatch(uri.AbsolutePath, @"\.(m3u8|mpd)$", RegexOptions.IgnoreCase);

    /// <summary>Pages that should open the video quality list rather than a plain file download.</summary>
    public static bool IsKnownVideoPage(string? value)
    {
        var uri = Web(value);
        if (uri == null) return false;
        string host = uri.IdnHost.ToLowerInvariant();
        if (IsYouTubeHost(host)) return YouTube(uri) != null || Playlist(value) != null;
        if (IsStreamManifest(value)) return true;
        return KnownPages.Any(page => HostMatches(host, page.Host) && page.Path.IsMatch(uri.AbsolutePath) && (page.Host != "facebook.com" || uri.AbsolutePath.TrimEnd('/') != "/watch" || Query(uri, "v") != null));
    }

    public static string SiteName(string url)
    {
        if (IsStreamManifest(url)) return url.Contains(".mpd", StringComparison.OrdinalIgnoreCase) ? "DASH stream" : "HLS stream";
        string host = Web(url)?.IdnHost.ToLowerInvariant() ?? "";
        if (IsYouTubeHost(host)) return "YouTube";
        foreach (string prefix in new[] { "www.", "m.", "mobile." }) if (host.StartsWith(prefix, StringComparison.Ordinal)) host = host[prefix.Length..];
        return host;
    }
}

public static class VideoFormats
{
    private static string Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
    private static double Number(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double n) && double.IsFinite(n) ? n : 0;
    private static string Clip(string value, int length) => value.Length > length ? value[..length] : value;

    public static bool IsPlaylist(JsonElement root) => Text(root, "_type") is "playlist" or "multi_video";

    public static VideoCatalog Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Pages with several videos (a post with clips) return a playlist; use its first video.
        if (IsPlaylist(root) && root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0 && entries[0].ValueKind == JsonValueKind.Object && entries[0].TryGetProperty("formats", out _))
            root = entries[0];
        if (root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True) throw new InvalidDataException("Live broadcasts are not supported yet. Retry when the broadcast has finished.");
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array) throw new InvalidDataException("No video qualities were returned.");
        // Unknown codecs (common outside YouTube, e.g. HLS variants) count as present; "none" means absent.
        // audio_ext/video_ext are "none" on combined formats too, so only the codec fields signal absence.
        bool Known(JsonElement item, string codec) => Text(item, codec) is { Length: > 0 } value && value != "none";
        bool IsVideo(JsonElement item) => Text(item, "vcodec") != "none" && (Known(item, "vcodec") || Number(item, "height") > 0 || Number(item, "width") > 0 || Text(item, "video_ext") is not "none" && !Known(item, "acodec")) && Text(item, "resolution") != "audio only";
        bool IsAudio(JsonElement item) => Text(item, "acodec") != "none" && (Known(item, "acodec") || !IsVideo(item) || !Known(item, "vcodec"));
        bool Usable(JsonElement item) => !(item.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True) && VideoSelection.ValidFormatId(Text(item, "format_id")) &&
            (Text(item, "url").StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Text(item, "url").StartsWith("http://", StringComparison.OrdinalIgnoreCase)) &&
            Text(item, "protocol") is not ("rtmp" or "rtmpe" or "rtsp" or "mms" or "f4m" or "ism");
        var all = formats.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && Usable(item)).ToArray();
        var audio = all.Where(item => IsAudio(item) && !IsVideo(item)).OrderByDescending(item => Number(item,"language_preference")).ThenByDescending(item => Number(item,"abr")).ThenByDescending(item => Number(item,"tbr")).ToArray();
        (long? Bytes, bool Estimate) Size(JsonElement item)
        {
            double exact = Number(item,"filesize"), estimate = Number(item,"filesize_approx");
            double value = exact > 0 ? exact : estimate;
            return value > 0 && value < long.MaxValue ? ((long)value, exact <= 0) : (null, false);
        }
        string Codec(JsonElement item) => Text(item,"vcodec").Split('.')[0] switch { "" or "none" => "", "avc1" or "avc3" or "h264" => "H.264", "hvc1" or "hev1" or "h265" => "H.265", "av01" => "AV1", "vp09" or "vp9" => "VP9", var value => value.ToUpperInvariant() };
        var result = new List<VideoFormat>();
        var qualities = new HashSet<string>();
        foreach (var video in all.Where(IsVideo).OrderByDescending(item => Number(item,"height")).ThenByDescending(item => Number(item,"fps")).ThenByDescending(item => Number(item,"tbr")))
        {
            string ext = Text(video,"ext"); if (ext is not ("mp4" or "webm")) continue;
            string id = Text(video,"format_id"); var size = Size(video); long? total = size.Bytes; bool approximate = size.Estimate;
            bool noAudio = false;
            if (!IsAudio(video))
            {
                var sound = audio.FirstOrDefault(item => Text(item,"ext") == (ext == "mp4" ? "m4a" : "webm"));
                if (sound.ValueKind == JsonValueKind.Undefined) sound = audio.FirstOrDefault(item => Text(item,"ext") is "m4a" or "mp4" or "webm");
                if (sound.ValueKind == JsonValueKind.Undefined) noAudio = true;
                else
                {
                    if (Text(sound,"ext") != (ext == "mp4" ? "m4a" : "webm")) ext = "mkv";
                    id += "+" + Text(sound,"format_id"); var audioSize = Size(sound);
                    total = total is > 0 && audioSize.Bytes is > 0 && total < long.MaxValue - audioSize.Bytes ? total + audioSize.Bytes : null;
                    approximate |= audioSize.Estimate;
                }
            }
            if (id.Length > 201 || !VideoSelection.ValidFormatId(id)) continue;
            double height = Number(video,"height"), fps = Number(video,"fps");
            string codec = Codec(video);
            string quality = height > 0 ? height + "p" : Text(video,"format_note") is { Length: > 0 } note ? Clip(note, 40) : Number(video,"tbr") > 0 ? Math.Round(Number(video,"tbr")) + " kbps" : id;
            if (!qualities.Add($"{ext}|{quality}|{fps}|{codec}|{noAudio}")) continue;
            string label = $"{ext.ToUpperInvariant()} · {quality}{(fps > 30 ? " · " + fps.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture) + " fps" : "")}{(codec.Length > 0 ? " · " + codec : "")} · {(noAudio ? "video only" : "video + audio")}";
            if (result.All(item => item.FormatId != id)) result.Add(new(id,label,ext,total,approximate,false,noAudio));
            if (result.Count >= 50) break;
        }
        // One audio-only choice, in a container the bundled tools can write without re-encoding.
        var bestAudio = audio.FirstOrDefault(item => Text(item,"ext") == "m4a");
        if (bestAudio.ValueKind == JsonValueKind.Undefined) bestAudio = audio.FirstOrDefault(item => Text(item,"ext") == "webm");
        if (bestAudio.ValueKind != JsonValueKind.Undefined)
        {
            var size = Size(bestAudio); double rate = Number(bestAudio,"abr") > 0 ? Number(bestAudio,"abr") : Number(bestAudio,"tbr");
            string ext = Text(bestAudio,"ext");
            result.Add(new(Text(bestAudio,"format_id"), $"{ext.ToUpperInvariant()} · audio only{(rate > 0 ? " · " + Math.Round(rate) + " kbps" : "")}", ext, size.Bytes, size.Estimate, true));
        }
        if (result.Count == 0) throw new InvalidDataException("No unencrypted video is available for this page.");
        string title = Clip(Text(root,"title"), 200);
        string site = Clip(Text(root,"extractor_key"), 60);
        return new(title.Length == 0 ? "Video" : title, result, site.Length == 0 ? null : site);
    }

    public static VideoPlaylist ParsePlaylist(string json, int maximum = 1000)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!IsPlaylist(root) || !root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException("No playlist entries were returned.");
        var result = new List<VideoPlaylistEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            string title = Clip(Text(entry,"title"), 200);
            if (title is "[Private video]" or "[Deleted video]") continue;
            string? url = new[] { Text(entry,"url"), Text(entry,"webpage_url") }.Select(VideoPages.Canonicalize).FirstOrDefault(value => value != null && VideoPages.Playlist(value) == null);
            if (url == null || !seen.Add(url)) continue;
            double duration = Number(entry,"duration");
            result.Add(new(url, title.Length == 0 ? "Video " + (result.Count + 1) : title, duration > 0 ? duration : null));
            if (result.Count >= maximum) break;
        }
        if (result.Count == 0) throw new InvalidDataException("The playlist has no downloadable videos.");
        string name = Clip(Text(root,"title"), 200);
        return new(name.Length == 0 ? "Playlist" : name, result);
    }
}
