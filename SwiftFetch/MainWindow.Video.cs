using System.IO;
using System.Windows;
using SwiftFetch.Core;

namespace SwiftFetch;

public partial class MainWindow
{
    private static VideoTools CreateVideoTools() => new(Path.Combine(AppContext.BaseDirectory, "video-tools"), useUpdatedTools: true);

    // A session is valid only for its own origin; video jobs use the canonical page address.
    private static DownloadSession? SessionFor(DownloadSession? session, string url)
    {
        if (session == null) return null;
        try { session.Validate(url); return session; } catch (ArgumentException) { return null; }
    }

    /// <summary>Opens the video picker. Returns false when the caller should download the address as a plain file.</summary>
    private async Task<bool> TryOpenVideoAsync(Uri uri, DownloadSession? session)
    {
        var tools = CreateVideoTools();
        if (!tools.Available) { MessageText.Text = "Video tools are missing; downloading the address as a file."; return false; }
        var picker = new VideoPickerDialog(uri.AbsoluteUri, tools, SessionFor(session, uri.AbsoluteUri));
        if (!await IndependentDialogs.ShowAsync(picker, bringToFront: true) || closing) return true;
        if (picker.FallbackToFile) return false;
        try
        {
            if (picker.Playlist is { } playlist) { AddPlaylist(playlist, picker); return true; }
            if (picker.Catalog is not { } catalog || picker.Selection is not { } selection) return true;
            string page = VideoPages.Canonicalize(uri.AbsoluteUri)!;
            var pageSession = SessionFor(session, page);
            string quality = System.Text.RegularExpressions.Regex.Match(selection.Label, @"\d+p").Value is { Length: > 0 } height ? height : selection.AudioOnly ? "audio" : selection.FormatId;
            string name = FileNames.Sanitize($"{catalog.Title} ({quality}).{selection.Extension}");
            var pageUri = new Uri(page);
            if (settings.ShowStartDownloadDialog)
            {
                var info = new DownloadInfoDialog(pageUri, settings, pageSession, name, engine, selection);
                if (!await IndependentDialogs.ShowAsync(info, bringToFront: true) || closing) return true;
                var row = AddConfirmedDownload(info.Confirmation with { Video = selection });
                if (info.StartNow && settings.ShowProgressOnStart) ShowProgressDialog(row);
            }
            else
            {
                var row = Enqueue(pageUri, name, session: pageSession, video: selection);
                if (settings.ShowProgressOnStart) ShowProgressDialog(row);
            }
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { MessageText.Text = error.Message; }
        return true;
    }

    private void AddPlaylist(VideoPlaylist playlist, VideoPickerDialog picker)
    {
        var preset = picker.Preset;
        string category = preset.AudioOnly ? "Music" : "Video";
        string folder = settings.GetDirectoryForCategory(category);
        if (picker.UseSubfolder) folder = Path.Combine(folder, FileNames.Sanitize(playlist.Title).TrimEnd('.'));
        Directory.CreateDirectory(folder);
        int added = 0;
        foreach (var entry in picker.SelectedEntries)
        {
            try
            {
                string destination = FileNames.UniquePath(folder, entry.Title + "." + preset.Extension, path => Downloads.Any(row => row.Job.Destination.Equals(path, StringComparison.OrdinalIgnoreCase)));
                // Each job owns its selection; yt-dlp resolves the preset per video.
                Enqueue(new Uri(entry.Url), startNow: picker.StartNow, session: settings.ManualSession(new Uri(entry.Url)), explicitDestination: destination, explicitCategory: category, nameIsCustom: false, video: VideoSelection.Preset(preset.PresetHeight, preset.AudioOnly));
                added++;
            }
            catch (Exception error) when (error is IOException or ArgumentException or UriFormatException) { MessageText.Text = entry.Title + ": " + error.Message; }
        }
        MessageText.Text = $"{playlist.Title}: added {added} of {picker.SelectedEntries.Count} videos{(picker.StartNow ? "" : " for later")}.";
    }
}
