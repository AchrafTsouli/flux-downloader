using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SwiftFetch.Core;

namespace SwiftFetch;

/// <summary>Lists a video page's qualities, or a playlist's entries with one quality preset for all.</summary>
public partial class VideoPickerDialog : Window
{
    public sealed class Entry(VideoPlaylistEntry item) : INotifyPropertyChanged
    {
        private bool selected = true;
        public VideoPlaylistEntry Item { get; } = item;
        public bool Selected { get => selected; set { selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
        public string Display => Item.DurationSeconds is double seconds
            ? $"{Item.Title}  ({TimeSpan.FromSeconds(Math.Round(seconds)):g})" : Item.Title;
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private sealed record FormatItem(VideoFormat Format)
    {
        public override string ToString() => Format.Label + "   " + (Format.TotalBytes is long bytes ? (Format.Approximate ? "≈ " : "") + Size(bytes) : "size unknown");
    }
    private sealed record PresetItem(string Name, int? Height, bool Audio) { public override string ToString() => Name; }

    private readonly string page;
    private readonly VideoTools tools;
    private readonly DownloadSession? session;
    private readonly CancellationTokenSource cancel = new();
    private readonly List<Entry> entries = [];

    public bool FallbackToFile { get; private set; }
    public VideoCatalog? Catalog { get; private set; }
    public VideoPlaylist? Playlist { get; private set; }
    public VideoSelection? Selection { get; private set; }
    public IReadOnlyList<VideoPlaylistEntry> SelectedEntries => entries.Where(entry => entry.Selected).Select(entry => entry.Item).ToArray();
    public VideoSelection Preset => PresetBox.SelectedItem is PresetItem preset ? VideoSelection.Preset(preset.Height, preset.Audio) : VideoSelection.Preset(null, false);
    public bool UseSubfolder => SubfolderCheck.IsChecked == true;
    public bool StartNow => StartNowCheck.IsChecked == true;

    public VideoPickerDialog(string page, VideoTools tools, DownloadSession? session)
    {
        this.page = page; this.tools = tools; this.session = session;
        InitializeComponent();
        SourceText.Text = VideoPages.SiteName(page) + " · " + page;
        PresetBox.ItemsSource = new[] { new PresetItem("Best available", null, false) }
            .Concat(VideoSelection.PresetHeights.Select(height => new PresetItem($"Up to {height}p", height, false)))
            .Append(new PresetItem("Audio only (M4A)", null, true)).ToArray();
        PresetBox.SelectedIndex = 3; // 1080p keeps playlists reasonable in size.
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => cancel.Cancel();
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / (double)(1L << 20):0.0} MB";

    private async Task LoadAsync()
    {
        try
        {
            var probe = await tools.ProbeAsync(page, session, cancel.Token);
            if (probe.Playlist is { } playlist)
            {
                Playlist = playlist;
                TitleText.Text = playlist.Title;
                entries.AddRange(playlist.Entries.Select(item => new Entry(item)));
                EntryList.ItemsSource = entries;
                PlaylistPanel.Visibility = PlaylistOptions.Visibility = Visibility.Visible;
                StatusText.Text = $"{playlist.Entries.Count} videos found. Choose the videos and one quality; each video downloads separately.";
                UpdateCount();
            }
            else if (probe.Catalog is { } catalog)
            {
                Catalog = catalog;
                TitleText.Text = catalog.Title;
                FormatList.ItemsSource = catalog.Formats.Select(format => new FormatItem(format)).ToArray();
                FormatList.SelectedIndex = 0;
                FormatList.Visibility = Visibility.Visible;
                StatusText.Text = "Choose a quality. Separate audio and video tracks are joined automatically.";
                DownloadButton.IsEnabled = true;
                FormatList.Focus();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        {
            TitleText.Text = "No video found";
            StatusText.Text = error.Message;
        }
    }

    private void UpdateCount()
    {
        int count = entries.Count(entry => entry.Selected);
        CountText.Text = $"{count} of {entries.Count} selected";
        DownloadButton.IsEnabled = count > 0;
    }
    private void Entry_Changed(object sender, RoutedEventArgs e) => UpdateCount();
    private void SelectAll_Click(object sender, RoutedEventArgs e) { entries.ForEach(entry => entry.Selected = true); UpdateCount(); }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { entries.ForEach(entry => entry.Selected = false); UpdateCount(); }
    private void FormatList_DoubleClick(object sender, MouseButtonEventArgs e) { if (FormatList.SelectedItem != null) Download_Click(sender, e); }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (Catalog != null)
        {
            if (FormatList.SelectedItem is not FormatItem item) return;
            Selection = item.Format.ToSelection();
        }
        else if (Playlist == null || !entries.Any(entry => entry.Selected)) return;
        IndependentDialogs.Finish(this, true);
    }
    private void File_Click(object sender, RoutedEventArgs e) { FallbackToFile = true; IndependentDialogs.Finish(this, true); }
    private void Cancel_Click(object sender, RoutedEventArgs e) => IndependentDialogs.Finish(this, false);
}
