using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SwiftFetch.Core;

public sealed record RemoteZipEntry(string Name, long UncompressedSize, long CompressedSize, bool IsDirectory);

public static class RemoteZipInspector
{
    public static async Task<List<RemoteZipEntry>> InspectAsync(string url, DownloadSession? session = null, CancellationToken token = default, HttpMessageHandler? handler = null, System.Net.IWebProxy? proxy = null)
    {
        using var client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None, Proxy = new SettingsProxy(() => proxy) }) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");

        // 1. Fetch file length with HEAD
        using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
        session?.Apply(headReq);
        using var headResp = await client.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, token);
        headResp.EnsureSuccessStatusCode();
        long? contentLength = headResp.Content.Headers.ContentLength;
        if (contentLength is null || contentLength < 22)
        {
            throw new InvalidOperationException("Could not determine archive length or file is too small.");
        }

        long fileLen = contentLength.Value;
        // 2. Fetch the trailing 65536 bytes (or entire file if smaller)
        long tailBytes = Math.Min(fileLen, 65557);
        long tailStart = fileLen - tailBytes;

        using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
        rangeReq.Headers.Range = new RangeHeaderValue(tailStart, fileLen - 1);
        session?.Apply(rangeReq);
        using var rangeResp = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, token);
        if (rangeResp.StatusCode != HttpStatusCode.PartialContent || rangeResp.Content.Headers.ContentRange?.From != tailStart || rangeResp.Content.Headers.ContentRange?.To != fileLen - 1 || rangeResp.Content.Headers.ContentRange?.Length != fileLen)
        {
            throw new InvalidOperationException("Server does not support HTTP Range requests for ZIP preview.");
        }

        byte[] tailBuffer = await ReadRangeAsync(rangeResp, (int)tailBytes, token);

        // 3. Find End of Central Directory signature: 0x06054b50
        int eocdIndex = -1;
        for (int i = tailBuffer.Length - 22; i >= 0; i--)
        {
            if (tailBuffer[i] == 0x50 && tailBuffer[i + 1] == 0x4B && tailBuffer[i + 2] == 0x05 && tailBuffer[i + 3] == 0x06 && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tailBuffer.AsSpan(i + 20,2)) == tailBuffer.Length)
            {
                eocdIndex = i;
                break;
            }
        }

        if (eocdIndex < 0)
        {
            throw new InvalidDataException("End of Central Directory record not found. Not a standard ZIP archive.");
        }

        int cdEntries = BinaryPrimitives.ReadUInt16LittleEndian(tailBuffer.AsSpan(eocdIndex + 10, 2));
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tailBuffer.AsSpan(eocdIndex + 12, 4));
        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tailBuffer.AsSpan(eocdIndex + 16, 4));
        if (cdEntries == ushort.MaxValue || cdSize == uint.MaxValue || cdOffset == uint.MaxValue) throw new InvalidDataException("ZIP64 preview is not supported.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(tailBuffer.AsSpan(eocdIndex+4,2)) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(tailBuffer.AsSpan(eocdIndex+6,2)) != 0) throw new InvalidDataException("Multi-volume ZIP preview is not supported.");
        if (cdSize > 4_000_000 || cdOffset + cdSize > tailStart + eocdIndex) throw new InvalidDataException("ZIP central directory is too large or invalid.");

        byte[] cdBuffer;
        if (cdOffset >= tailStart && cdOffset + cdSize <= fileLen)
        {
            // Already present inside tail buffer!
            int relativeOffset = (int)(cdOffset - tailStart);
            cdBuffer = tailBuffer.AsSpan(relativeOffset, (int)cdSize).ToArray();
        }
        else
        {
            // Fetch central directory bytes directly
            using var cdReq = new HttpRequestMessage(HttpMethod.Get, url);
            cdReq.Headers.Range = new RangeHeaderValue(cdOffset, cdOffset + cdSize - 1);
            session?.Apply(cdReq);
            using var cdResp = await client.SendAsync(cdReq, HttpCompletionOption.ResponseHeadersRead, token);
            if (cdResp.StatusCode != HttpStatusCode.PartialContent || cdResp.Content.Headers.ContentRange?.From != cdOffset || cdResp.Content.Headers.ContentRange?.To != cdOffset + cdSize - 1 || cdResp.Content.Headers.ContentRange?.Length != fileLen) throw new InvalidDataException("Server refused the central directory range.");
            cdBuffer = await ReadRangeAsync(cdResp, (int)cdSize, token);
        }

        // 4. Parse Central Directory file headers (signature: 0x02014b50)
        var entries = new List<RemoteZipEntry>();
        int ptr = 0;
        while (ptr + 46 <= cdBuffer.Length)
        {
            if (cdBuffer[ptr] != 0x50 || cdBuffer[ptr + 1] != 0x4B || cdBuffer[ptr + 2] != 0x01 || cdBuffer[ptr + 3] != 0x02)
                break;

            long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(cdBuffer.AsSpan(ptr + 20, 4));
            long uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(cdBuffer.AsSpan(ptr + 24, 4));
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(cdBuffer.AsSpan(ptr + 28, 2));
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(cdBuffer.AsSpan(ptr + 30, 2));
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(cdBuffer.AsSpan(ptr + 32, 2));

            if (ptr + 46 + nameLen + extraLen + commentLen > cdBuffer.Length) throw new InvalidDataException("Truncated ZIP directory entry.");
            string name = Encoding.UTF8.GetString(cdBuffer, ptr + 46, nameLen);
            bool isDir = name.EndsWith('/') || name.EndsWith('\\');
            entries.Add(new(name, uncompressedSize, compressedSize, isDir));

            ptr += 46 + nameLen + extraLen + commentLen;
        }

        if (entries.Count != cdEntries) throw new InvalidDataException("ZIP entry count does not match its directory.");
        return entries;
    }
    private static async Task<byte[]> ReadRangeAsync(HttpResponseMessage response, int length, CancellationToken token)
    {
        if (response.Content.Headers.ContentEncoding.Count > 0) throw new InvalidDataException("Compressed range response is not supported.");
        await using var stream = await response.Content.ReadAsStreamAsync(token); byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes,token);
        byte[] extra = new byte[1]; if (await stream.ReadAsync(extra,token) != 0) throw new InvalidDataException("ZIP range response is larger than requested."); return bytes;
    }
}
