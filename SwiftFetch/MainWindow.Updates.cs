using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using SwiftFetch.Core;
namespace SwiftFetch;
public partial class MainWindow
{
    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);
    private static readonly HttpClient updateClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private bool checkingUpdates;

    private void HomePage_Click(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(settings.EffectiveHomePage,UriKind.Absolute,out var url) && url.Scheme is "https" or "http" && url.UserInfo.Length == 0) { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); return; }
        MessageBox.Show(this,$"Flux {CurrentVersion} is a local project. Configure its home page in Options → View when you publish it. The project documentation is in the docs folder.","Flux project information");
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(interactive: true);
        await UpdateVideoToolsAsync(interactive: true);
    }

    /// <summary>Called at startup and from a timer: at most one check per day for each kind of update.</summary>
    private async Task RunScheduledUpdatesAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (settings.AutoUpdateVideoTools && (settings.LastVideoToolsCheck == null || now - settings.LastVideoToolsCheck > TimeSpan.FromHours(24))) await UpdateVideoToolsAsync(interactive: false);
        if (settings.AutoCheckUpdates && (settings.LastUpdateCheck == null || now - settings.LastUpdateCheck > TimeSpan.FromHours(24))) await CheckForUpdatesAsync(interactive: false);
    }

    private async Task UpdateVideoToolsAsync(bool interactive)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromMinutes(10));
            string? installed = await new YtDlpUpdater(updateClient).UpdateAsync(Path.Combine(AppContext.BaseDirectory, "video-tools"), timeout.Token);
            settings.LastVideoToolsCheck = DateTimeOffset.UtcNow; SaveHistory();
            if (installed != null) MessageText.Text = "Video downloader updated to yt-dlp " + installed + ".";
            else if (interactive) MessageText.Text = "The video downloader (yt-dlp) is up to date.";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { if (interactive) MessageText.Text = "Video downloader update: " + error.Message; }
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        if (checkingUpdates) return;
        checkingUpdates = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var release = AppUpdates.Parse(await BoundedFeedAsync(timeout.Token));
            settings.LastUpdateCheck = DateTimeOffset.UtcNow; SaveHistory();
            if (release.Version <= CurrentVersion) { if (interactive) MessageText.Text = $"Flux {CurrentVersion} is up to date."; return; }
            if (!release.Signed)
            {
                if (MessageBox.Show(this,$"Flux {release.Version} is available. This feed is not signed for automatic installation. Open the download page?\n{release.Url.AbsoluteUri}","Update available",MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(release.Url.AbsoluteUri) { UseShellExecute = true });
                return;
            }
            if (MessageBox.Show(this,$"Flux {release.Version} is available (you have {CurrentVersion}).\n\nDownload and install it now? Active downloads are paused and resume after the update.","Update available",MessageBoxButton.YesNo,MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            MessageText.Text = $"Downloading Flux {release.Version}…";
            using var download = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); download.CancelAfter(TimeSpan.FromMinutes(10));
            string installer = await AppUpdates.DownloadInstallerAsync(updateClient, release, download.Token);
            await scheduler.PauseAllAsync();
            if (!SaveHistory()) throw new IOException("Downloads could not be saved; the update was cancelled.");
            // The installer refuses to run while Flux holds its mutex, so start it shortly after this process exits.
            // It relaunches the updated app when it finishes (see [Run] in installer/ApexDownloader.iss).
            Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c ping -n 4 127.0.0.1 >nul & \"{installer}\" /SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = false, CreateNoWindow = true });
            ExitApplication();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        { if (interactive) MessageText.Text = "Update check: " + error.Message; }
        finally { checkingUpdates = false; }
    }

    private async Task<byte[]> BoundedFeedAsync(CancellationToken token)
    {
        using var response = await updateClient.GetAsync(settings.EffectiveUpdateFeed,HttpCompletionOption.ResponseHeadersRead,token); response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream(); byte[] buffer = new byte[8192];
        while (await stream.ReadAsync(buffer,token) is int count and > 0) { if (memory.Length + count > 65536) throw new InvalidDataException("Update feed is too large."); memory.Write(buffer,0,count); }
        return memory.ToArray();
    }
}
