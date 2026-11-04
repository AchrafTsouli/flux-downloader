using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SwiftFetch.Core;

namespace SwiftFetch;

/// <summary>Shows a torrent's files (a magnet link's list is fetched from peers first) and lets the user pick them.</summary>
public partial class TorrentDialog : Window
{
    public sealed class Entry(TorrentFile file) : INotifyPropertyChanged
    {
        private bool selected = true;
        public TorrentFile File { get; } = file;
        public bool Selected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public string Display => $"{File.Path}   ({Size(File.Length)})";
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private readonly string source;
    private readonly TorrentTools tools;
    private readonly CancellationTokenSource cancel = new();
    private readonly List<Entry> entries = [];

    public TorrentInfo? Info { get; private set; }
    public string Folder => FolderBox.Text.Trim();
    public bool StartNow => StartNowCheck.IsChecked == true;
    public int SeedMinutes => SeedCheck.IsChecked == true && int.TryParse(SeedMinutesBox.Text, out int minutes) ? Math.Clamp(minutes, 1, 1440) : 0;
    /// <summary>1-based indexes when only some files are chosen; empty when every file is.</summary>
    public List<int> SelectedFiles => entries.All(e => e.Selected) ? [] : [.. entries.Where(e => e.Selected).Select(e => e.File.Index)];
    public long SelectedBytes => entries.Where(e => e.Selected).Sum(e => e.File.Length);

    public TorrentDialog(string source, TorrentTools tools, string folder)
    {
        this.source = source; this.tools = tools;
        InitializeComponent();
        SourceText.Text = source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? "Magnet link · " + (Torrents.MagnetName(source) ?? Torrents.MagnetHash(source)) : source;
        FolderBox.Text = folder;
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => cancel.Cancel();
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private async Task LoadAsync()
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            Info = await tools.LoadAsync(source, timeout.Token);
            TitleText.Text = Info.Name;
            entries.AddRange(Info.Files.Select(file => new Entry(file)));
            FileList.ItemsSource = entries;
            FilesPanel.Visibility = OptionsPanel.Visibility = Visibility.Visible;
            LoadingBar.Visibility = Visibility.Collapsed;
            StatusText.Text = Info.Files.Count == 1 ? "One file." : $"{Info.Files.Count} files. Untick the ones you don't need; only the chosen files are downloaded.";
            UpdateCount();
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { Fail("No one shared this torrent's file list within 3 minutes. Try again later, or use a .torrent file."); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Fail(error.Message); }
    }
    private void Fail(string message) { LoadingBar.Visibility = Visibility.Collapsed; TitleText.Text = "Could not read the torrent"; StatusText.Text = message; }

    private void UpdateCount()
    {
        int count = entries.Count(e => e.Selected);
        CountText.Text = $"{count} of {entries.Count} files · {Size(SelectedBytes)}";
        DownloadButton.IsEnabled = count > 0;
    }
    private void File_Changed(object sender, RoutedEventArgs e) => UpdateCount();
    private void SelectAll_Click(object sender, RoutedEventArgs e) { entries.ForEach(x => x.Selected = true); UpdateCount(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { entries.ForEach(x => x.Selected = false); UpdateCount(); }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { InitialDirectory = Directory.Exists(Folder) ? Folder : null };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }
    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (Info == null || !entries.Any(x => x.Selected)) return;
        if (!Path.IsPathFullyQualified(Folder)) { StatusText.Text = "Choose a folder to save the torrent in."; return; }
        IndependentDialogs.Finish(this, true);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => IndependentDialogs.Finish(this, false);
}
