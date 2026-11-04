using System.Text.Json;
using SwiftFetch.Core;

namespace SwiftFetch.Bridge;

public static class NativeBridgeHost
{
    // Shared by the development host and the compact app's native mode.
    // Chrome owns these handles: stdout must contain framed protocol messages only.
    public static async Task RunAsync(string[] args)
    {
        using var installerLock = new Mutex(false, @"Local\ApexDownloader.Running");
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        try
        {
            string manifest = Path.Combine(AppContext.BaseDirectory, Protocol.HostName + ".json");
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
            string? origin = args.FirstOrDefault();
            if (origin == null || !config.RootElement.GetProperty("allowed_origins").EnumerateArray().Any(item => item.GetString() == origin))
                throw new InvalidOperationException("This extension is not registered for Flux.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(115));
            var request = await Protocol.ReadAsync<BridgeRequest>(input, timeout.Token);
            if (request == null) return;
            string? error = Protocol.Validate(request);
            BridgeResponse response;
            if (error != null) response = new(false, error);
            else if (request.Action == "ping") response = File.Exists(AppConnection.ExecutablePath) ? new(true) : new(false, "The Flux application is missing.");
            else if (request.Action == "video-formats")
            {
                var catalog = await new VideoTools(Path.Combine(AppContext.BaseDirectory,"video-tools"),useUpdatedTools:true).GetFormatsAsync(request.Url!,request.Session,timeout.Token);
                response = new(true, Video: catalog);
            }
            else if (request.Action == "capture-policy")
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch");
                var preferences = SqliteDownloadStateStore.ReadPreferences(Path.Combine(folder, "history.db"));
                response = new(true, Policy: new(preferences.BrowserCaptureEnabled, preferences.BrowserContextMenuEnabled, preferences.CaptureExtensions, preferences.ExcludedSites, preferences.ExcludedAddresses, preferences.BypassKey, preferences.ForceKey, preferences.BrowserMediaPanelEnabled));
            }
            else response = await AppConnection.SendAsync(request, true, timeout.Token);
            await Protocol.WriteAsync(output, response, timeout.Token);
        }
        catch (Exception ex)
        {
            try { await Protocol.WriteAsync(output, new BridgeResponse(false, ex is OperationCanceledException ? "Flux did not respond in time." : ex.Message)); }
            catch { /* Browser disconnected. */ }
        }
    }
}
