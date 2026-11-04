using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public sealed class VideoTools(string directory, IMediaProcessRunner? runner = null, bool useUpdatedTools = false)
{
    private static readonly string[] OutputExtensions = ["mp4", "webm", "mkv", "m4a", "mov", "mp3", "ogg", "opus"];
    private readonly IMediaProcessRunner process = runner ?? new MediaProcessRunner();
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public bool Available => new[] {"yt-dlp.exe","ffmpeg.exe","ffprobe.exe"}.All(name => File.Exists(Path.Combine(DirectoryPath,name))) &&
        (File.Exists(Path.Combine(DirectoryPath,"qjs.exe")) || File.Exists(Path.Combine(DirectoryPath,"node.exe")));
    private ProcessStartInfo Start(string executable, IEnumerable<string> arguments)
    {
        if (!Available) throw new FileNotFoundException("Video tools are missing. Run scripts/Install-VideoTools.ps1, then rebuild the release.");
        // A verified newer yt-dlp (YtDlpUpdater) replaces the aging bundled copy; FFmpeg and QuickJS stay bundled.
        string path = executable == "yt-dlp.exe" && useUpdatedTools && YtDlpUpdater.PreferredExecutable(DirectoryPath) is { } updated ? updated : Path.Combine(DirectoryPath, executable);
        var start = new ProcessStartInfo(path) { WorkingDirectory = DirectoryPath };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }
    private List<string> Common(string? cookieFile, DownloadSession? session, bool playlist = false)
    {
        List<string> args = ["--ignore-config", playlist ? "--yes-playlist" : "--no-playlist", "--no-cache-dir", "--output-na-placeholder", "null", "--no-js-runtimes", "--js-runtimes", JavascriptRuntime, "--ffmpeg-location", DirectoryPath];
        if (cookieFile != null) args.AddRange(["--cookies", cookieFile]);
        // Streams and many non-YouTube sites check the embedding page and browser identity.
        if (session?.Referer is { Length: > 0 } referer) args.AddRange(["--referer", referer]);
        if (session?.UserAgent is { Length: > 0 } agent) args.AddRange(["--user-agent", agent]);
        return args;
    }
    private string JavascriptRuntime => File.Exists(Path.Combine(DirectoryPath,"qjs.exe"))
        ? "quickjs:" + Path.Combine(DirectoryPath,"qjs.exe") : "node:" + Path.Combine(DirectoryPath,"node.exe");
    private static string? Cookies(DownloadSession? session, string url)
    {
        if (session == null || session.Cookies.Count == 0) return null;
        session.Validate(url);
        string path = Path.Combine(Path.GetTempPath(), "apex-video-" + Guid.NewGuid().ToString("N") + ".cookies.txt");
        var text = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var cookie in session.Cookies)
            text.AppendLine(string.Join('\t', cookie.Domain, cookie.HostOnly ? "FALSE" : "TRUE", cookie.Path, cookie.Secure ? "TRUE" : "FALSE", (cookie.ExpiresUnixSeconds is > 0 ? (long)cookie.ExpiresUnixSeconds : 0).ToString(CultureInfo.InvariantCulture), cookie.Name, cookie.Value));
        File.WriteAllText(path,text.ToString(),new UTF8Encoding(false));
        return path;
    }
    private static void RemoveCookieFile(string? path) { if (path != null) try { File.Delete(path); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    private static string Failure(string error)
    {
        string text = Regex.Replace(error, @"https?://[^\s]+", "[media URL]", RegexOptions.IgnoreCase);
        var lines = text.Split('\n',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        text = lines.LastOrDefault(line => line.StartsWith("ERROR:")) ?? lines.LastOrDefault() ?? "Video tool failed.";
        if (text.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)) text = "No video was found on this page. Use the direct file link, or open the page in the browser and use the video panel.";
        return text.Length > 700 ? text[..700] : text;
    }
    private static (string Url, string? Cookie) Prepare(string page, DownloadSession? session, out string canonical)
    {
        canonical = VideoPages.Canonicalize(page) ?? throw new ArgumentException("Use a video page or an HLS/DASH stream address.");
        session?.Validate(page);
        return (canonical, Cookies(session,page));
    }
    public async Task<VideoCatalog> GetFormatsAsync(string page, DownloadSession? session, CancellationToken token)
    {
        var (canonical, cookie) = Prepare(page, session, out _);
        try
        {
            var args = Common(cookie,session); args.AddRange(["--skip-download", "--dump-single-json", "--no-check-formats", "--playlist-items", "1", "--", canonical]);
            var result = await process.RunAsync(Start("yt-dlp.exe",args),null,token);
            if (result.ExitCode != 0) throw new IOException(Failure(result.Error));
            return VideoFormats.Parse(result.Output);
        }
        finally { RemoveCookieFile(cookie); }
    }
    public async Task<VideoPlaylist> GetPlaylistAsync(string page, DownloadSession? session, CancellationToken token, int maximum = 1000)
    {
        string list = VideoPages.Playlist(page) ?? VideoPages.Canonicalize(page) ?? throw new ArgumentException("Use a playlist or channel address.");
        session?.Validate(page);
        string? cookie = Cookies(session,page);
        try
        {
            var args = Common(cookie,session,true); args.AddRange(["--flat-playlist", "--dump-single-json", "--playlist-items", "1:" + Math.Clamp(maximum,1,5000).ToString(CultureInfo.InvariantCulture), "--", list]);
            var result = await process.RunAsync(Start("yt-dlp.exe",args),null,token);
            if (result.ExitCode != 0) throw new IOException(Failure(result.Error));
            return VideoFormats.ParsePlaylist(result.Output, maximum);
        }
        finally { RemoveCookieFile(cookie); }
    }
    /// <summary>Playlist and channel addresses list their entries; anything else lists one video's qualities.</summary>
    public async Task<VideoProbe> ProbeAsync(string page, DownloadSession? session, CancellationToken token)
    {
        if (VideoPages.Playlist(page) != null) return new(null, await GetPlaylistAsync(page,session,token));
        return new(await GetFormatsAsync(page,session,token), null);
    }
    public static string WorkDirectory(DownloadJob job)
    {
        string root = job.TemporaryDirectory is { Length: > 0 } ? job.TemporaryDirectory : Path.GetDirectoryName(job.Destination)!;
        job.MediaWorkDirectory ??= Path.Combine(root,".apex-video-" + job.Id);
        if (!Guid.TryParseExact(job.Id,"N",out _) || !Path.IsPathFullyQualified(job.MediaWorkDirectory) || Path.GetFileName(job.MediaWorkDirectory) != ".apex-video-" + job.Id) throw new InvalidDataException("Invalid video temporary directory.");
        return job.MediaWorkDirectory;
    }
    /// <summary>Tracks shown for a video job: chosen format IDs, or those yt-dlp picked for a preset.</summary>
    public static IReadOnlyList<string> Tracks(DownloadJob job) =>
        job.MediaTracks.Count > 0 ? job.MediaTracks : job.Video is { IsPreset: false } video ? video.FormatId.Split('+') : [];
    private static List<string> FormatArguments(VideoSelection selection)
    {
        if (!selection.IsPreset)
            return selection.FormatId.Contains('+') ? ["--format", selection.FormatId, "--merge-output-format", selection.Extension is "m4a" ? "mp4" : selection.Extension] : ["--format", selection.FormatId];
        if (selection.AudioOnly) return ["--format", "ba[ext=m4a]/ba[acodec^=mp4a]/ba/b"];
        // Prefer MP4/M4A so the bundled tools can copy streams without re-encoding.
        string sort = selection.PresetHeight is int height ? $"res:{height},ext:mp4:m4a" : "ext:mp4:m4a";
        return ["--format", "bv*+ba/b", "--format-sort", sort];
    }
    public async Task DownloadAsync(DownloadJob job, IProgress<DownloadProgress>? progress, CancellationToken token, Action? checkpoint = null, long globalLimit = 0, Func<string,bool>? reservedPath = null)
    {
        var selection = job.Video ?? throw new ArgumentException("No video format selected.");
        selection.Validate(job.Url);
        job.StartedAt ??= DateTimeOffset.UtcNow; job.CompletedAt = null; job.Error = ""; job.ErrorKind = DownloadErrorKind.None;
        job.State = DownloadState.Resolving; job.RangeSupported = true;
        job.ResumeNote = "Video parts are retained for pause/resume; audio and video are assembled after downloading.";
        string? cookie = null;
        try
        {
            string work = WorkDirectory(job); Directory.CreateDirectory(work); Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
            checkpoint?.Invoke();
            cookie = Cookies(job.Session,job.Url);
            var args = Common(cookie,job.Session);
            args.AddRange(["--newline", "--progress", "--no-simulate", "--continue", "--no-overwrites", "--no-mtime", "--windows-filenames", "--abort-on-unavailable-fragments", "--playlist-items", "1", "--retries", Math.Clamp(job.MaximumRetries ?? 3,0,10).ToString(CultureInfo.InvariantCulture), "--fragment-retries", Math.Clamp(job.MaximumRetries ?? 3,0,10).ToString(CultureInfo.InvariantCulture), "--concurrent-fragments", Math.Clamp(job.RequestedConnections,1,16).ToString(CultureInfo.InvariantCulture)]);
            args.AddRange(FormatArguments(selection));
            args.AddRange(["--output", Path.Combine(work,"result.%(ext)s"), "--print", "before_dl:APEXFORMAT:%(format_id)j", "--print", "after_move:APEXFILE:%(filepath)j", "--progress-template", "download:APEXPROGRESS:{\"format\":%(info.format_id)j,\"bytes\":%(progress.downloaded_bytes)j,\"total\":%(progress.total_bytes)j,\"estimate\":%(progress.total_bytes_estimate)j,\"speed\":%(progress.speed)j}"]);
            long limit = job.SpeedLimitBytes is > 0 ? job.SpeedLimitBytes.Value : 0;
            if (globalLimit > 0) limit = limit > 0 ? Math.Min(limit,globalLimit) : globalLimit;
            if (limit > 0) args.AddRange(["--limit-rate",limit.ToString(CultureInfo.InvariantCulture)]);
            args.AddRange(["--",VideoPages.Canonicalize(job.Url)!]);
            string? resultFile = null;
            string[] ids = [.. Tracks(job)];
            void Line(string line)
            {
                if (line.StartsWith("APEXFILE:")) { try { resultFile = JsonSerializer.Deserialize<string>(line[9..]); } catch (JsonException) { } return; }
                if (line.StartsWith("APEXFORMAT:"))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<string>(line[11..]) is { } chosen && VideoSelection.ValidFormatId(chosen))
                            lock (job.SegmentGate) { ids = chosen.Split('+'); job.MediaTracks = [.. ids]; }
                    }
                    catch (JsonException) { }
                    return;
                }
                if (line.StartsWith("[Merger]") || line.StartsWith("[VideoRemuxer]") || line.StartsWith("[FixupM3u8]")) { job.State = DownloadState.Finishing; progress?.Report(new(job.MediaDownloadedBytes,job.TotalBytes,0,0)); }
                if (!line.StartsWith("APEXPROGRESS:")) return;
                try
                {
                    using var json = JsonDocument.Parse(line[13..]); var item = json.RootElement;
                    string id = item.GetProperty("format").GetString() ?? "video";
                    if (!ids.Contains(id)) return;
                    long bytes = item.GetProperty("bytes").ValueKind == JsonValueKind.Number && item.GetProperty("bytes").TryGetInt64(out var value) ? Math.Max(0,value) : 0;
                    lock (job.SegmentGate)
                    {
                        job.MediaTrackBytes[id] = bytes;
                        job.MediaDownloadedBytes = job.MediaTrackBytes.Where(pair => ids.Contains(pair.Key)).Sum(pair => pair.Value);
                        if (item.GetProperty("total").ValueKind == JsonValueKind.Number && item.GetProperty("total").TryGetInt64(out long total) && total > 0)
                        { job.MediaTrackTotals[id] = total; job.MediaTrackEstimates.Remove(id); }
                        else if (item.GetProperty("estimate").ValueKind == JsonValueKind.Number && item.GetProperty("estimate").TryGetDouble(out double estimate) && estimate > 0 && estimate < long.MaxValue)
                        { job.MediaTrackTotals[id] = (long)estimate; job.MediaTrackEstimates.Add(id); }
                        if (ids.All(job.MediaTrackTotals.ContainsKey)) { job.TotalBytes = ids.Sum(key => job.MediaTrackTotals[key]); job.VideoSizeEstimated = job.MediaTrackEstimates.Count > 0; }
                    }
                    job.MediaActiveTrack = id;
                    job.State = DownloadState.Downloading;
                    double speed = item.GetProperty("speed").ValueKind == JsonValueKind.Number && item.GetProperty("speed").TryGetDouble(out double rate) && double.IsFinite(rate) ? Math.Max(0,rate) : 0;
                    progress?.Report(new(job.MediaDownloadedBytes,job.TotalBytes,speed,1));
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or OverflowException or KeyNotFoundException) { }
            }
            var result = await process.RunAsync(Start("yt-dlp.exe",args),Line,token);
            token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) throw new IOException(Failure(result.Error));
            if (resultFile == null || !Path.GetFullPath(resultFile).StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resultFile).StartsWith("result.",StringComparison.Ordinal) || !File.Exists(resultFile)) throw new IOException("The video tool did not produce a completed media file.");
            job.State = DownloadState.Verifying;
            var probe = await process.RunAsync(Start("ffprobe.exe",["-v","error","-show_entries","stream=codec_type","-of","json",resultFile]),null,token);
            if (probe.ExitCode != 0) throw new IOException("The downloaded media could not be read: " + Failure(probe.Error));
            using (var info = JsonDocument.Parse(probe.Output))
            {
                if (info.RootElement.ValueKind != JsonValueKind.Object || !info.RootElement.TryGetProperty("streams", out var list) || list.ValueKind != JsonValueKind.Array)
                    throw new IOException("The downloaded media contains no readable streams.");
                var streams = list.EnumerateArray().Where(stream => stream.ValueKind == JsonValueKind.Object && stream.TryGetProperty("codec_type",out var type) && type.ValueKind == JsonValueKind.String).Select(stream => stream.GetProperty("codec_type").GetString()).ToArray();
                if (selection.AudioOnly) { if (!streams.Contains("audio")) throw new IOException("The output must contain an audio stream."); }
                else
                {
                    if (!streams.Contains("video")) throw new IOException("The output must contain a video stream.");
                    // A merged selection must keep its sound; combined formats from other sites may be silent.
                    if (!selection.NoAudio && ids.Length > 1 && !streams.Contains("audio")) throw new IOException("The output must contain both a video stream and an audio stream.");
                }
            }
            // yt-dlp can pick another container (presets, remuxed streams); name the file after its real format.
            string actual = Path.GetExtension(resultFile).TrimStart('.').ToLowerInvariant();
            if (OutputExtensions.Contains(actual) && !Path.GetExtension(job.Destination).TrimStart('.').Equals(actual,StringComparison.OrdinalIgnoreCase))
            {
                job.Destination = FileNames.UniquePath(Path.GetDirectoryName(job.Destination)!, Path.GetFileNameWithoutExtension(job.Destination) + "." + actual, reservedPath);
                checkpoint?.Invoke();
            }
            job.State = DownloadState.Finishing;
            await using (var source = File.OpenRead(resultFile))
            await using (var target = new FileStream(job.MergePath,FileMode.Create,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous))
            { await source.CopyToAsync(target,token); await target.FlushAsync(token); target.Flush(true); }
            token.ThrowIfCancellationRequested();
            if (job.ExpectedSha256 is { Length: > 0 } expected)
            {
                await using var input = File.OpenRead(job.MergePath);
                job.ActualSha256 = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input,token)).ToLowerInvariant();
                if (!job.ActualSha256.Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new IOException("The assembled video does not match the expected SHA-256 checksum.");
                job.IntegrityVerified = true;
            }
            File.Move(job.MergePath,job.Destination,false);
            job.TotalBytes = new FileInfo(job.Destination).Length; job.MediaDownloadedBytes = job.TotalBytes.Value;
            job.VideoSizeEstimated = false; job.CompletedAt = DateTimeOffset.UtcNow; job.State = DownloadState.Completed;
            progress?.Report(new(job.TotalBytes.Value,job.TotalBytes,0,0));
            try
            {
                foreach (string file in Directory.EnumerateFiles(work,"result.*")) try { File.Delete(file); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                if (!Directory.EnumerateFileSystemEntries(work).Any()) Directory.Delete(work);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* The completed destination is authoritative; leftover parts can be cleaned later. */ }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { job.State = DownloadState.Paused; }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or System.ComponentModel.Win32Exception)
        { job.State = DownloadState.Failed; job.ErrorKind = DownloadErrorKind.Protocol; job.Error = Failure(error.Message); }
        finally { job.MediaActiveTrack = null; RemoveCookieFile(cookie); }
    }
}
