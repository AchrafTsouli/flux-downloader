using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwiftFetch.Core;

internal static class BoundedHttp
{
    public static async Task<byte[]> ReadAsync(HttpClient client, string url, long maximum, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Updates must be served over HTTPS.");
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("The update is larger than expected.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (await stream.ReadAsync(buffer, token) is int count and > 0)
        {
            if (memory.Length + count > maximum) throw new InvalidDataException("The update is larger than expected.");
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}

/// <summary>
/// Keeps a newer yt-dlp beside the user's data (sites change often; the bundled copy ages).
/// The bundled tools are never modified; a copy is used only after its SHA-256 matches the release checksums and it runs.
/// </summary>
public sealed class YtDlpUpdater(HttpClient client, string? directory = null)
{
    public const string Repository = "https://github.com/yt-dlp/yt-dlp/releases";
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch", "tools");
    public string DirectoryPath { get; } = directory ?? DefaultDirectory;
    public string ExecutablePath => Path.Combine(DirectoryPath, "yt-dlp.exe");
    private string VersionPath => Path.Combine(DirectoryPath, "yt-dlp.version");

    public static Version? ParseVersion(string? text) =>
        text != null && Regex.Match(text.Trim(), @"\A(\d{4})\.(\d{1,2})\.(\d{1,2})(?:\.(\d+))?\z") is { Success: true } m
            ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0) : null;

    /// <summary>The version recorded in a tools folder's sources.json (the installer's manifest).</summary>
    public static Version? BundledVersion(string toolsDirectory)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(toolsDirectory, "sources.json")));
            return ParseVersion(json.RootElement.GetProperty("ytDlp").GetProperty("version").GetString());
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }
    public Version? InstalledVersion
    {
        get { try { return File.Exists(ExecutablePath) ? ParseVersion(File.ReadAllText(VersionPath)) : null; } catch (IOException) { return null; } }
    }
    /// <summary>The updated copy when it is newer than the bundled one; otherwise null.</summary>
    public static string? PreferredExecutable(string toolsDirectory, string? updateDirectory = null)
    {
        var updater = new YtDlpUpdater(new HttpClient(), updateDirectory);
        var installed = updater.InstalledVersion;
        var bundled = BundledVersion(toolsDirectory);
        return installed != null && (bundled == null || installed > bundled) ? updater.ExecutablePath : null;
    }

    public async Task<string> LatestTagAsync(CancellationToken token)
    {
        // /releases/latest redirects to /releases/tag/<version>.
        using var response = await client.GetAsync(Repository + "/latest", HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        string final = response.RequestMessage?.RequestUri?.AbsoluteUri ?? "";
        var match = Regex.Match(final, @"/releases/tag/(\d{4}\.\d{1,2}\.\d{1,2}(?:\.\d+)?)$");
        return match.Success ? match.Groups[1].Value : throw new InvalidDataException("Could not read the latest yt-dlp release.");
    }

    /// <summary>Installs a newer release if one exists. Returns the new version, or null when already current.</summary>
    public async Task<string?> UpdateAsync(string toolsDirectory, CancellationToken token)
    {
        string tag = await LatestTagAsync(token);
        var latest = ParseVersion(tag)!;
        var current = new[] { InstalledVersion, BundledVersion(toolsDirectory) }.Where(v => v != null).DefaultIfEmpty(null).Max();
        if (current != null && latest <= current) return null;
        string sums = Encoding.UTF8.GetString(await BoundedHttp.ReadAsync(client, $"{Repository}/download/{tag}/SHA2-256SUMS", 64 * 1024, token));
        string expected = sums.Split('\n').Select(line => line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == "yt-dlp.exe")?[0].ToLowerInvariant()
            ?? throw new InvalidDataException("The release has no checksum for yt-dlp.exe.");
        if (!Regex.IsMatch(expected, @"\A[0-9a-f]{64}\z")) throw new InvalidDataException("Invalid yt-dlp checksum.");
        byte[] executable = await BoundedHttp.ReadAsync(client, $"{Repository}/download/{tag}/yt-dlp.exe", 200L * 1024 * 1024, token);
        if (BoundedHttp.Sha256(executable) != expected) throw new InvalidDataException("The downloaded yt-dlp does not match its published checksum.");
        Directory.CreateDirectory(DirectoryPath);
        string staging = Path.Combine(DirectoryPath, "yt-dlp." + Guid.NewGuid().ToString("N") + ".tmp.exe");
        try
        {
            await File.WriteAllBytesAsync(staging, executable, token);
            // The copy must actually run and report the version it claims.
            using (var process = Process.Start(new ProcessStartInfo(staging, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })
                ?? throw new IOException("The updated yt-dlp could not start."))
            {
                string output = await process.StandardOutput.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                if (process.ExitCode != 0 || ParseVersion(output) != latest) throw new InvalidDataException("The updated yt-dlp did not report the expected version.");
            }
            File.Move(staging, ExecutablePath, true);
            await File.WriteAllTextAsync(VersionPath, tag, token);
            return tag;
        }
        finally { try { if (File.Exists(staging)) File.Delete(staging); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } }
    }
}

/// <summary>A release described by a signed JSON feed: {"version","url","sha256","signature"}.</summary>
public sealed record AppRelease(Version Version, Uri Url, string Sha256, bool Signed);

public static class AppUpdates
{
    /// <summary>ECDSA P-256 public key (SubjectPublicKeyInfo, Base64). Empty until a release key is generated with scripts/New-UpdateKey.ps1.</summary>
    public static string PublicKey { get; set; } = UpdateSigningKey.PublicKey;
    public static bool SigningConfigured => PublicKey.Length > 0;

    public static byte[] SignedPayload(string version, string url, string sha256) => Encoding.UTF8.GetBytes($"apex-update\n{version}\n{url}\n{sha256.ToLowerInvariant()}");

    /// <summary>Parses a feed. A release can be installed automatically only when its signature verifies against the built-in key.</summary>
    public static AppRelease Parse(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Text(string name, string? alternative = null) =>
            root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! :
            alternative != null && root.TryGetProperty(alternative, out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "";
        string versionText = Text("version"), urlText = Text("url", "downloadUrl"), sha = Text("sha256").ToLowerInvariant(), signature = Text("signature");
        if (!Version.TryParse(versionText, out var version)) throw new InvalidDataException("Invalid release version.");
        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0) throw new InvalidDataException("The release link must use HTTPS.");
        bool signed = false;
        if (SigningConfigured && signature.Length > 0)
        {
            if (!Regex.IsMatch(sha, @"\A[0-9a-f]{64}\z")) throw new InvalidDataException("Invalid release checksum.");
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(signature); } catch (FormatException) { throw new InvalidDataException("Invalid release signature."); }
            if (!key.VerifyData(SignedPayload(versionText, urlText, sha), bytes, HashAlgorithmName.SHA256)) throw new InvalidDataException("The release signature is not valid. The update was rejected.");
            signed = true;
        }
        return new(version, url, sha, signed);
    }

    /// <summary>Downloads a signed installer and checks it against the signed checksum before it can run.</summary>
    public static async Task<string> DownloadInstallerAsync(HttpClient client, AppRelease release, CancellationToken token)
    {
        if (!release.Signed) throw new InvalidOperationException("Only signed releases can be installed automatically.");
        byte[] installer = await BoundedHttp.ReadAsync(client, release.Url.AbsoluteUri, 300L * 1024 * 1024, token);
        if (BoundedHttp.Sha256(installer) != release.Sha256) throw new InvalidDataException("The downloaded installer does not match the signed checksum.");
        string folder = Path.Combine(Path.GetTempPath(), "apex-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Flux-Setup-{release.Version}.exe");
        await File.WriteAllBytesAsync(path, installer, token);
        return path;
    }
}
