using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using SwiftFetch.Core;

namespace SwiftFetch.Bridge;

public sealed record BridgeRequest(string Action, string? Url = null, string? FileName = null, string? RequestId = null, DownloadSession? Session = null, VideoSelection? Video = null);
public sealed record BridgeResponse(bool Ok, string? Error = null, string? JobId = null, BrowserCapturePolicy? Policy = null, VideoCatalog? Video = null);
public sealed record BrowserCapturePolicy(bool Enabled, bool ContextMenu, string Extensions, string ExcludedSites, string ExcludedAddresses, string BypassKey, string ForceKey, bool MediaPanel = false);

public static class Protocol
{
    public const string HostName = "com.swiftfetch.bridge";
    public const int MaxMessageBytes = 65536;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string InstanceName => GetInstanceName(AppContext.BaseDirectory);
    public static string GetInstanceName(string directory)
    {
        using var user = WindowsIdentity.GetCurrent();
        string identity = user.User!.Value + "|" + Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        return "SwiftFetch-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }

    public static string? Validate(BridgeRequest request) => request.Action switch
    {
        "ping" or "show" or "quit" or "pause-all" or "resume-all" or "capture-policy" => null,
        "pause" or "resume" => null,
        // Any ordinary web page may be searched (yt-dlp site extractors and generic player detection);
        // the extension only asks for unknown pages when the user clicks "Find available qualities".
        "video-formats" when VideoPages.Canonicalize(request.Url) == null => "Use an HTTP/HTTPS video page or stream address without embedded credentials.",
        "video-formats" => ValidateSession(request),
        "download" when !Guid.TryParse(request.RequestId, out _) => "A valid request ID is required.",
        "download" when Torrents.MagnetHash(request.Url) != null => request.Video == null && request.Session == null ? null : "Magnet links carry no video selection or session.",
        "download" when request.Url?.Length > 16384 || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) => "Only HTTP/HTTPS file links without embedded credentials are supported.",
        "download" when request.FileName?.Length > 1024 => "The filename is too long.",
        "download" => ValidateSession(request),
        _ => "Unsupported bridge action."
    };
    private static string? ValidateSession(BridgeRequest request)
    {
        try { request.Session?.Validate(request.Url!); request.Video?.Validate(request.Url!); return null; }
        catch (ArgumentException) { return "Invalid download session."; }
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token = default)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0) return default;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes) throw new InvalidDataException("Invalid native message size.");
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body, Json) ?? throw new InvalidDataException("Empty native message.");
    }

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (body.Length > MaxMessageBytes) throw new InvalidDataException("Native message is too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }
}
