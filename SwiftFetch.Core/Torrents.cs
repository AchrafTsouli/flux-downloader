using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

public sealed record TorrentFile(int Index, string Path, long Length);
public sealed record TorrentInfo(string Name, string InfoHash, IReadOnlyList<TorrentFile> Files, long TotalBytes);
public sealed record TorrentStatus(long Completed, long Total, double BytesPerSecond, int Connections, int Seeders, string State, string? Error);
public sealed record TorrentRun(string MetadataFile, string Directory, IReadOnlyList<int> Files, long SpeedLimit, int SeedMinutes);

/// <summary>What a torrent job downloads: its cached metadata and the files the user chose.</summary>
public sealed class TorrentSelection
{
    public string InfoHash { get; set; } = "";
    public string Name { get; set; } = "";
    public string MetadataFile { get; set; } = "";
    /// <summary>1-based file indexes (aria2 --select-file); empty means every file.</summary>
    public List<int> Files { get; set; } = [];
    public long SelectedBytes { get; set; }
    public int SeedMinutes { get; set; }

    public void Validate(string url)
    {
        if (!Regex.IsMatch(InfoHash, @"\A[0-9a-f]{40}\z") || Torrents.MagnetHash(url) != InfoHash || Name.Length is 0 or > 255 || Name != FileNames.Sanitize(Name) ||
            !Path.IsPathFullyQualified(MetadataFile) || !Path.GetFileName(MetadataFile).Equals(InfoHash + ".torrent", StringComparison.OrdinalIgnoreCase) ||
            Files == null || Files.Count > 10000 || Files.Any(i => i is < 1 or > 100000) || SelectedBytes < 0 || SeedMinutes is < 0 or > 1440)
            throw new ArgumentException("Invalid torrent selection.");
    }
}

public static class Torrents
{
    public static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch", "torrents");

    // Well-known public trackers, added after the torrent's own: many magnet links carry none that work.
    public static readonly string[] PublicTrackers =
    [
        "udp://tracker.opentrackr.org:1337/announce", "udp://open.stealth.si:80/announce", "udp://tracker.torrent.eu.org:451/announce",
        "udp://exodus.desync.com:6969/announce", "udp://tracker.dler.org:6969/announce", "udp://open.demonii.com:1337/announce",
        "udp://tracker.openbittorrent.com:6969/announce", "https://tracker.tamersunion.org:443/announce", "http://tracker.opentrackr.org:1337/announce",
    ];

    /// <summary>The 40-hex info hash of a magnet link (hex or base32 form), or null.</summary>
    public static string? MagnetHash(string? magnet)
    {
        if (magnet == null || magnet.Length > 8192 || !magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (string pair in magnet[8..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !parts[0].Equals("xt", StringComparison.OrdinalIgnoreCase)) continue;
            string value = Uri.UnescapeDataString(parts[1]);
            if (!value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase)) continue;
            string hash = value[9..];
            if (Regex.IsMatch(hash, @"\A[0-9A-Fa-f]{40}\z")) return hash.ToLowerInvariant();
            if (Regex.IsMatch(hash, @"\A[A-Za-z2-7]{32}\z")) return Convert.ToHexString(Base32(hash)).ToLowerInvariant();
        }
        return null;
    }
    public static string? MagnetName(string magnet) =>
        magnet[8..].Split('&').Select(pair => pair.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "dn") is { } dn ? Uri.UnescapeDataString(dn[1].Replace('+', ' ')) : null;
    public static string Magnet(string hash, string name) => $"magnet:?xt=urn:btih:{hash}&dn={Uri.EscapeDataString(name)}";

    private static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (char c in text.ToUpperInvariant()) { buffer = buffer << 5 | alphabet.IndexOf(c); bits += 5; if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; } }
        return [.. bytes];
    }

    /// <summary>Reads a .torrent file: name, files (sanitized relative paths) and the SHA-1 info hash.</summary>
    public static TorrentInfo Parse(byte[] data)
    {
        if (data.Length is 0 or > 50 * 1024 * 1024) throw new InvalidDataException("This is not a valid torrent file.");
        int position = 0; (int Start, int End) infoSpan = default;
        object root = Bencode.Read(data, ref position, 0, (key, start, end) => { if (key == "info") infoSpan = (start, end); });
        if (root is not Dictionary<string, object> top || !top.TryGetValue("info", out var infoValue) || infoValue is not Dictionary<string, object> info || infoSpan.End == 0)
            throw new InvalidDataException("This is not a valid torrent file.");
        string hash = Convert.ToHexString(SHA1.HashData(data.AsSpan(infoSpan.Start, infoSpan.End - infoSpan.Start))).ToLowerInvariant();
        string Text(Dictionary<string, object> d, string key) => d.TryGetValue(key + ".utf-8", out var u) && u is byte[] ub ? Encoding.UTF8.GetString(ub) : d.TryGetValue(key, out var v) && v is byte[] b ? Encoding.UTF8.GetString(b) : "";
        string name = FileNames.Sanitize(Text(info, "name"));
        if (name is "download.bin") name = "torrent-" + hash[..8];
        var files = new List<TorrentFile>();
        if (info.TryGetValue("files", out var list) && list is List<object> entries)
        {
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object> file || !file.TryGetValue("length", out var length) || length is not long size || size < 0) throw new InvalidDataException("The torrent's file list is damaged.");
                var segments = (file.TryGetValue("path.utf-8", out var p8) ? p8 : file.TryGetValue("path", out var p) ? p : null) as List<object>;
                if (segments == null || segments.Count == 0) throw new InvalidDataException("The torrent's file list is damaged.");
                // Every part is sanitized: a torrent can never write outside its own folder.
                string path = string.Join('\\', segments.Select(s => s is byte[] b ? FileNames.Sanitize(Encoding.UTF8.GetString(b)) : throw new InvalidDataException("The torrent's file list is damaged.")));
                files.Add(new(files.Count + 1, path, size));
                if (files.Count > 100000) throw new InvalidDataException("The torrent has too many files.");
            }
        }
        else if (info.TryGetValue("length", out var single) && single is long length && length >= 0) files.Add(new(1, name, length));
        else throw new InvalidDataException("The torrent has no files.");
        return new(name, hash, files, files.Sum(f => f.Length));
    }

    /// <summary>Minimal bencode reader with depth and size limits; reports the byte span of each dictionary value at the top level.</summary>
    private static class Bencode
    {
        public static object Read(byte[] data, ref int i, int depth, Action<string, int, int>? topLevelValue = null)
        {
            if (depth > 64 || i >= data.Length) throw new InvalidDataException("This is not a valid torrent file.");
            byte c = data[i];
            if (c == (byte)'i')
            {
                int end = Array.IndexOf(data, (byte)'e', i);
                if (end < 0 || !long.TryParse(Encoding.ASCII.GetString(data, i + 1, end - i - 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value)) throw new InvalidDataException("This is not a valid torrent file.");
                i = end + 1; return value;
            }
            if (c == (byte)'l')
            {
                i++; var list = new List<object>();
                while (i < data.Length && data[i] != (byte)'e') list.Add(Read(data, ref i, depth + 1));
                if (i >= data.Length) throw new InvalidDataException("This is not a valid torrent file.");
                i++; return list;
            }
            if (c == (byte)'d')
            {
                i++; var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                while (i < data.Length && data[i] != (byte)'e')
                {
                    string key = Encoding.UTF8.GetString((byte[])Read(data, ref i, depth + 1));
                    int start = i;
                    dict[key] = Read(data, ref i, depth + 1);
                    topLevelValue?.Invoke(key, start, i);
                }
                if (i >= data.Length) throw new InvalidDataException("This is not a valid torrent file.");
                i++; return dict;
            }
            if (c is >= (byte)'0' and <= (byte)'9')
            {
                int colon = Array.IndexOf(data, (byte)':', i);
                if (colon < 0 || !int.TryParse(Encoding.ASCII.GetString(data, i, colon - i), NumberStyles.None, CultureInfo.InvariantCulture, out int length) || colon + 1 + length > data.Length) throw new InvalidDataException("This is not a valid torrent file.");
                var bytes = data.AsSpan(colon + 1, length).ToArray(); i = colon + 1 + length; return bytes;
            }
            throw new InvalidDataException("This is not a valid torrent file.");
        }
    }
}

/// <summary>Runs the torrent engine. The real one is aria2; tests use a simulated backend.</summary>
public interface ITorrentBackend
{
    Task<string> FetchMetadataAsync(string magnet, string folder, CancellationToken token);
    Task RunAsync(TorrentRun run, Action<TorrentStatus> status, CancellationToken token);
}

/// <summary>
/// aria2 as a child process, answering only on this computer (127.0.0.1) with a random token per start, and closed
/// together with Flux (--stop-with-process). Its .aria2 control files make pause/resume continue where it stopped.
/// </summary>
public sealed class Aria2Backend(string executable) : ITorrentBackend
{
    private static readonly HttpClient rpc = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static List<string> Common() =>
    [
        "--enable-dht=true", "--bt-enable-lpd=true", "--enable-peer-exchange=true", "--bt-tracker=" + string.Join(',', Torrents.PublicTrackers),
        "--bt-tracker-connect-timeout=10", "--bt-tracker-timeout=20", "--follow-torrent=false", "--summary-interval=0", "--console-log-level=warn",
        "--stop-with-process=" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture), "--file-allocation=none", "--auto-file-renaming=false",
    ];
    private static ProcessStartInfo Start(string executable, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return start;
    }

    public async Task<string> FetchMetadataAsync(string magnet, string folder, CancellationToken token)
    {
        string hash = Torrents.MagnetHash(magnet) ?? throw new ArgumentException("Use a magnet link with a BitTorrent info hash.");
        Directory.CreateDirectory(folder);
        var args = Common(); args.AddRange(["--bt-metadata-only=true", "--bt-save-metadata=true", "--seed-time=0", "--dir=" + folder, magnet]);
        using var process = Process.Start(Start(executable, args)) ?? throw new IOException("The torrent engine could not start.");
        using var stop = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);
        token.ThrowIfCancellationRequested();
        string file = Path.Combine(folder, hash + ".torrent");
        if (!File.Exists(file)) throw new IOException("No one shared this torrent's file list. Try again later, or use a .torrent file. " + (await errors).Trim().Split('\n').LastOrDefault());
        return file;
    }

    public async Task RunAsync(TorrentRun run, Action<TorrentStatus> status, CancellationToken token)
    {
        int port; using (var listener = new TcpListener(IPAddress.Loopback, 0)) { listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); }
        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), gid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var args = Common();
        args.AddRange(["--enable-rpc=true", "--rpc-listen-all=false", "--rpc-listen-port=" + port, "--rpc-secret=" + secret, "--gid=" + gid, "--dir=" + run.Directory,
            "--continue=true", "--check-integrity=false", "--allow-overwrite=false", "--auto-save-interval=10", "--seed-time=" + run.SeedMinutes, "--bt-save-metadata=false"]);
        if (run.Files.Count > 0) args.Add("--select-file=" + string.Join(',', run.Files));
        if (run.SpeedLimit > 0) args.Add("--max-overall-download-limit=" + run.SpeedLimit);
        args.Add(run.MetadataFile);
        using var process = Process.Start(Start(executable, args)) ?? throw new IOException("The torrent engine could not start.");
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        async Task<JsonElement?> Call(string method, params object[] parameters)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = "flux", method, @params = new object[] { "token:" + secret }.Concat(parameters).ToArray() });
            using var response = await rpc.PostAsync($"http://127.0.0.1:{port}/jsonrpc", new StringContent(body, Encoding.UTF8, "application/json"), CancellationToken.None);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken.None));
            return json.RootElement.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        try
        {
            var started = Stopwatch.StartNew();
            while (true)
            {
                if (token.IsCancellationRequested) break;
                if (process.HasExited) throw new IOException("The torrent engine stopped: " + (await errors).Trim().Split('\n').LastOrDefault());
                JsonElement? result = null;
                try { result = await Call("aria2.tellStatus", gid, new[] { "status", "totalLength", "completedLength", "downloadSpeed", "connections", "numSeeders", "errorMessage", "seeder" }); }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException) { if (started.Elapsed > TimeSpan.FromSeconds(20)) throw new IOException("The torrent engine did not answer."); }
                if (result is { } r)
                {
                    long Number(string name) => r.TryGetProperty(name, out var v) && long.TryParse(v.GetString(), CultureInfo.InvariantCulture, out long n) ? n : 0;
                    string state = r.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                    bool seeding = r.TryGetProperty("seeder", out var seed) && seed.GetString() == "true" && state == "active";
                    string? error = r.TryGetProperty("errorMessage", out var e) ? e.GetString() : null;
                    status(new(Number("completedLength"), Number("totalLength"), Number("downloadSpeed"), (int)Number("connections"), (int)Number("numSeeders"), seeding ? "seeding" : state, error));
                    if (state is "complete" or "error" or "removed") break;
                }
                try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            try { await Call(token.IsCancellationRequested ? "aria2.forceShutdown" : "aria2.shutdown"); } catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException) { }
            using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await process.WaitForExitAsync(exit.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } }
        }
    }
}

public sealed class TorrentTools(string toolsDirectory, ITorrentBackend? backend = null)
{
    public string Executable => Path.Combine(toolsDirectory, "aria2c.exe");
    public bool Available => backend != null || File.Exists(Executable);
    private ITorrentBackend Backend => backend ?? (File.Exists(Executable) ? new Aria2Backend(Executable) : throw new FileNotFoundException("The torrent engine (aria2c.exe) is missing from the video tools."));

    /// <summary>Reads a magnet link (fetching its file list from peers) or a .torrent file, and caches the metadata.</summary>
    public async Task<TorrentInfo> LoadAsync(string source, CancellationToken token)
    {
        Directory.CreateDirectory(Torrents.CacheDirectory);
        string file;
        if (Torrents.MagnetHash(source) is string hash)
        {
            file = Path.Combine(Torrents.CacheDirectory, hash + ".torrent");
            if (!File.Exists(file))
            {
                string scratch = Path.Combine(Torrents.CacheDirectory, "fetch-" + Guid.NewGuid().ToString("N"));
                try { File.Copy(await Backend.FetchMetadataAsync(source, scratch, token), file, true); }
                finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
        else
        {
            var info0 = Torrents.Parse(await File.ReadAllBytesAsync(source, token));
            file = Path.Combine(Torrents.CacheDirectory, info0.InfoHash + ".torrent");
            if (!string.Equals(Path.GetFullPath(source), file, StringComparison.OrdinalIgnoreCase)) File.Copy(source, file, true);
        }
        var info = Torrents.Parse(await File.ReadAllBytesAsync(file, token));
        if (Torrents.MagnetHash(source) is string expected && expected != info.InfoHash) { File.Delete(file); throw new InvalidDataException("The file list received does not belong to this magnet link."); }
        return info;
    }
    public static string MetadataPath(string hash) => Path.Combine(Torrents.CacheDirectory, hash + ".torrent");

    public async Task DownloadAsync(DownloadJob job, IProgress<DownloadProgress>? progress, CancellationToken token, long globalLimit = 0)
    {
        var torrent = job.Torrent ?? throw new ArgumentException("No torrent selected.");
        job.StartedAt ??= DateTimeOffset.UtcNow; job.CompletedAt = null; job.Error = ""; job.ErrorKind = DownloadErrorKind.None;
        job.State = DownloadState.Connecting; job.RangeSupported = true;
        job.ResumeNote = "Finding people who share this torrent…";
        try
        {
            torrent.Validate(job.Url);
            if (!File.Exists(torrent.MetadataFile)) throw new FileNotFoundException("The torrent's saved file list is missing. Add the torrent again.");
            string folder = Path.GetDirectoryName(job.Destination)!;
            Directory.CreateDirectory(folder);
            long limit = job.SpeedLimitBytes is > 0 ? job.SpeedLimitBytes.Value : 0;
            if (globalLimit > 0) limit = limit > 0 ? Math.Min(limit, globalLimit) : globalLimit;
            TorrentStatus? last = null;
            await Backend.RunAsync(new(torrent.MetadataFile, folder, torrent.Files, limit, torrent.SeedMinutes), status =>
            {
                last = status;
                job.TorrentPeers = status.Connections; job.TorrentSeeders = status.Seeders;
                long total = torrent.SelectedBytes > 0 ? torrent.SelectedBytes : status.Total;
                if (total > 0) job.TotalBytes = total;
                job.MediaDownloadedBytes = Math.Min(status.Completed, total > 0 ? total : long.MaxValue);
                job.State = status.State == "seeding" ? DownloadState.Finishing : status.Connections > 0 || status.Completed > 0 ? DownloadState.Downloading : DownloadState.Connecting;
                job.ResumeNote = status.State == "seeding" ? $"Sharing with others for up to {torrent.SeedMinutes} min" : status.Connections > 0 ? $"{status.Connections} peers · {status.Seeders} seeders" : "Finding people who share this torrent…";
                job.PhaseText = status.State == "seeding" ? "Sharing the finished files" : null;
                progress?.Report(new(job.MediaDownloadedBytes, job.TotalBytes, status.BytesPerSecond, status.Connections));
            }, token);
            if (token.IsCancellationRequested) { job.State = DownloadState.Paused; return; }
            if (last?.State == "error") throw new IOException(last.Error is { Length: > 0 } message ? "Torrent: " + message : "The torrent could not be downloaded.");
            if (last?.State != "complete" && !(last?.State == "seeding")) throw new IOException("The torrent engine stopped before the download finished.");
            if (!File.Exists(job.Destination) && !Directory.Exists(job.Destination)) throw new IOException("The torrent finished but its files were not found.");
            job.MediaDownloadedBytes = job.TotalBytes ?? job.MediaDownloadedBytes;
            job.CompletedAt = DateTimeOffset.UtcNow; job.State = DownloadState.Completed; job.ResumeNote = "";
            progress?.Report(new(job.MediaDownloadedBytes, job.TotalBytes, 0, 0));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { job.State = DownloadState.Paused; }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception or JsonException)
        { job.State = DownloadState.Failed; job.ErrorKind = DownloadErrorKind.Protocol; job.Error = error.Message; }
        finally { job.PhaseText = null; job.TorrentPeers = 0; }
    }
}
