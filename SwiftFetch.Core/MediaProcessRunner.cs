using System.Diagnostics;
using System.Text;

namespace SwiftFetch.Core;

public sealed record MediaProcessResult(int ExitCode, string Output, string Error);
public interface IMediaProcessRunner
{
    Task<MediaProcessResult> RunAsync(ProcessStartInfo start, Action<string>? line, CancellationToken token);
}
public sealed class MediaProcessRunner : IMediaProcessRunner
{
    public async Task<MediaProcessResult> RunAsync(ProcessStartInfo start, Action<string>? line, CancellationToken token)
    {
        start.UseShellExecute = false; start.CreateNoWindow = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
        using var process = new Process { StartInfo = start };
        token.ThrowIfCancellationRequested();
        if (!process.Start()) throw new IOException("Could not start the video tool.");
        void Stop() { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
        using var cancellation = token.Register(Stop);
        async Task<string> Read(StreamReader reader, int limit, bool notify, bool tail = false)
        {
            var text = new StringBuilder();
            while (await reader.ReadLineAsync() is string value)
            {
                if (notify) line?.Invoke(value);
                if (value.StartsWith("APEXPROGRESS:")) continue;
                if (text.Length + value.Length > limit)
                {
                    if (tail) { text.Clear(); value = value.Length > limit ? value[^limit..] : value; }
                    else { Stop(); throw new InvalidDataException("Video tool output exceeded its size limit."); }
                }
                text.AppendLine(value);
            }
            return text.ToString();
        }
        try
        {
            var output = Read(process.StandardOutput, 16 * 1024 * 1024, true);
            var error = Read(process.StandardError, 128 * 1024, true, true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output,error);
            token.ThrowIfCancellationRequested();
            return new(process.ExitCode, await output, await error);
        }
        finally { Stop(); }
    }
}
