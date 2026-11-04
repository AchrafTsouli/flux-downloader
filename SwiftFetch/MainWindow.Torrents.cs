using System.IO;
using System.Windows;
using Microsoft.Win32;
using SwiftFetch.Core;

namespace SwiftFetch;

public partial class MainWindow
{
    private static TorrentTools CreateTorrentTools() => new(Path.Combine(AppContext.BaseDirectory, "video-tools"));
    public static bool IsTorrentSource(string text) =>
        Torrents.MagnetHash(text) != null || text.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(text);

    /// <summary>Opens the torrent window for a magnet link or a .torrent file, then adds the chosen files as one download.</summary>
    private async Task OpenTorrentAsync(string source)
    {
        var tools = CreateTorrentTools();
        if (!tools.Available) { MessageText.Text = "The torrent engine is missing from this installation. Reinstall Flux to restore it."; return; }
        var dialog = new TorrentDialog(source, tools, Path.Combine(settings.DownloadDirectory, "Torrents"));
        if (!await IndependentDialogs.ShowAsync(dialog, bringToFront: true) || closing || dialog.Info is not { } info) return;
        try
        {
            Directory.CreateDirectory(dialog.Folder);
            var selection = new TorrentSelection
            {
                InfoHash = info.InfoHash, Name = info.Name, MetadataFile = TorrentTools.MetadataPath(info.InfoHash),
                Files = dialog.SelectedFiles, SelectedBytes = dialog.SelectedBytes, SeedMinutes = dialog.SeedMinutes
            };
            string destination = Path.Combine(dialog.Folder, info.Name);
            var row = Enqueue(new Uri(Torrents.Magnet(info.InfoHash, info.Name)), startNow: dialog.StartNow, explicitDestination: destination, explicitCategory: "General",
                nameIsCustom: false, knownTotalBytes: selection.SelectedBytes, torrent: selection);
            if (dialog.StartNow && settings.ShowProgressOnStart) ShowProgressDialog(row);
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or UriFormatException) { MessageText.Text = "Torrent: " + error.Message; }
    }

    private async void OpenTorrentFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Torrent files (*.torrent)|*.torrent", Title = "Open torrent file" };
        if (dialog.ShowDialog(this) == true) await OpenTorrentAsync(dialog.FileName);
    }
}
