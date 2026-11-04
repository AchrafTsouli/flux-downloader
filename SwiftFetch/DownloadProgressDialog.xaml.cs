using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SwiftFetch.Core;

namespace SwiftFetch;

public partial class DownloadProgressDialog : Window
{
    private readonly DownloadRow row;
    private readonly DownloadEngine engine;
    private readonly Action<DownloadJob> onPause;
    private readonly Action<DownloadRow> onResume;
    private readonly Action<DownloadJob> onCompleted;
    private readonly DispatcherTimer timer;
    private bool detailsVisible = true;
    private double normalHeight = 460;
    private bool hasTriggeredCompletion = false;
    public Action? GroupInTray { get; set; }
    private bool initializing = true;
    public bool IsHiddenInTray { get; private set; }
    public event Action<QueueCompletionAction,bool,bool>? CompletionActionRequested;

    public sealed class ConnectionViewModel
    {
        public int Number { get; set; }
        public string DownloadedText { get; set; } = "0 B";
        public string InfoText { get; set; } = "Connecting...";
    }

    private readonly ObservableCollection<ConnectionViewModel> connections = [];

    public DownloadProgressDialog(DownloadRow row, DownloadEngine engine, Action<DownloadJob> onPause, Action<DownloadRow> onResume, Action<DownloadJob> onCompleted, DownloadSettings? settings = null)
    {
        this.row = row;
        this.engine = engine;
        this.onPause = onPause;
        this.onResume = onResume;
        this.onCompleted = onCompleted;

        long? initialLimit = row.Job.SpeedLimitBytes;
        InitializeComponent();
        if (settings != null)
        {
            ShowCompleteDialogCheck.IsChecked = row.Job.ShowCompleteDialog ?? settings.ShowDownloadCompleteDialog;
            if (!settings.ProgressDetailsVisible) { detailsVisible = false; DetailsGrid.Visibility = Visibility.Collapsed; DetailsToggleButton.Content = ">> Show details"; MinHeight = 340; Height = 340; }
            if (!settings.ProgressSpeedTabVisible && DetailTabs.Items[1] is TabItem tab) tab.Visibility = Visibility.Collapsed;
        }
        if (initialLimit is > 0) { SpeedLimitValBox.Text = Math.Max(1,initialLimit.Value / 1024).ToString(); UseSpeedLimiterCheck.IsChecked = true; RememberLimiterCheck.IsChecked = true; }
        initializing = false;
        ApplySpeedLimiter();

        ConnectionsGrid.ItemsSource = connections;
        ProgressUrlText.Text = row.Job.Url; ProgressUrlText.ToolTip = row.Job.Url;
        SaveToPathBlock.Text = "Save To: " + row.Job.Destination;

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => UpdateUi();

        Loaded += (_, _) =>
        {
            normalHeight = 520;
            timer.Start();
            UpdateUi();
        };

        Closed += (_, _) => { timer.Stop(); IsHiddenInTray = false; };
    }
    public void HideToTray()
    {
        try { if (GroupInTray != null) GroupInTray(); else Application.Current.Windows.OfType<MainWindow>().FirstOrDefault()?.EnsureTrayForProgress(); IsHiddenInTray = true; Hide(); }
        catch (Exception error) { MessageBox.Show(this,error.Message,"Notification area",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    public void RestoreFromTray()
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); IsHiddenInTray = false;
    }
    private void Tray_Click(object sender,RoutedEventArgs e) => HideToTray();
    private void Minimize_Click(object sender,RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender,RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender,RoutedEventArgs e) => Close();

    private void UpdateUi()
    {
        var job = row.Job;
        if (HandleCompletion()) return;
        // Hidden progress windows still detect completion, but do not rebuild visual trees.
        if (!IsVisible || WindowState == WindowState.Minimized) return;

        // Window Title
        string percentStr = row.PercentText;
        Title = $"{percentStr} {row.FileName}";
        

        // Tab 1: Download status
        StatusVal.Text = row.DisplayStatus;
        FileSizeVal.Text = row.TotalSizeText;
        DownloadedVal.Text = $"{Format(job.Video != null ? job.MediaDownloadedBytes : job.TotalBytes.HasValue ? Math.Min(job.TotalBytes.Value, job.Segments.Sum(s => s.WrittenBytes)) : job.Segments.Sum(s => s.WrittenBytes))} ({percentStr})";
        TransferRateVal.Text = row.SpeedText;
        LimiterRateVal.Text = row.SpeedText;
        TimeLeftVal.Text = row.EtaText;
        ResumeCapabilityVal.Text = job.StartedAt == null ? "Not checked yet" : job.RangeSupported ? "Yes" : "No";
        ResumeCapabilityVal.Foreground = job.StartedAt == null ? (Brush)FindResource("ThemeForegroundAAAAAA") : new SolidColorBrush(job.RangeSupported ? Color.FromRgb(0x88, 0xC0, 0x70) : Color.FromRgb(0xE0, 0x55, 0x55));

        // Overall progress bar
        OverallProgressBar.Value = row.Percent;
        OverallProgressBar.IsIndeterminate = row.Indeterminate;

        // Pause/Resume button text
        PauseButton.Content = row.CanPause ? "Pause" : "Resume";

        // Segment visualizer canvas
        RenderSegments();

        // Update connection rows
        lock (job.SegmentGate)
        {
            if (job.Video != null)
            {
                var tracks = VideoTools.Tracks(job);
                while (connections.Count < tracks.Count) connections.Add(new ConnectionViewModel { Number = connections.Count + 1 });
                while (connections.Count > tracks.Count) connections.RemoveAt(connections.Count - 1);
                for (int i=0;i<tracks.Count;i++)
                {
                    connections[i].DownloadedText = Format(job.MediaTrackBytes.GetValueOrDefault(tracks[i]));
                    connections[i].InfoText = $"Track {tracks[i]} · " + (job.State is DownloadState.Finishing or DownloadState.Verifying ? "Assembling / verifying" : job.MediaActiveTrack == tracks[i] && job.State == DownloadState.Downloading ? "Receiving data…" : "Retained for resume");
                }
            }
            else
            {
            while (connections.Count < job.Segments.Count)
            {
                connections.Add(new ConnectionViewModel { Number = connections.Count + 1 });
            }
            while (connections.Count > job.Segments.Count && connections.Count > 0)
            {
                connections.RemoveAt(connections.Count - 1);
            }

            for (int i = 0; i < job.Segments.Count; i++)
            {
                var seg = job.Segments[i];
                var vm = connections[i];
                vm.Number = i + 1;
                vm.DownloadedText = Format(seg.WrittenBytes);
                vm.InfoText = seg.Active ? (string.IsNullOrEmpty(seg.StatusText) ? "Receiving data..." : seg.StatusText) : (seg.Finished ? "Complete" : "Idle");
            }
            }
        }
        ConnectionsGrid.Items.Refresh();

    }

    private bool HandleCompletion()
    {
        var job = row.Job;
        if (job.State == DownloadState.Completed && !hasTriggeredCompletion)
        {
            hasTriggeredCompletion = true;
            timer.Stop();
            if (ShowCompleteDialogCheck.IsChecked == true)
            {
                onCompleted(job);
            }
            else
            {
                var action = TurnOffCheck.IsChecked != true ? QueueCompletionAction.None : ShutdownActionBox.SelectedIndex switch { 1 => QueueCompletionAction.Sleep, 2 => QueueCompletionAction.Hibernate, _ => QueueCompletionAction.Shutdown };
                if (action != QueueCompletionAction.None || ExitOnCompleteCheck.IsChecked == true) CompletionActionRequested?.Invoke(action,ExitOnCompleteCheck.IsChecked == true,ForceProcessesCheck.IsChecked == true);
            }
            Close();
            return true;
        }
        return false;
    }

    private void RenderSegments()
    {
        double width = SegmentCanvas.ActualWidth;
        double height = SegmentCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        SegmentCanvas.Children.Clear();

        var job = row.Job;
        long total = job.TotalBytes ?? 0;
        if (total <= 0)
        {
            // If unknown size, show indeterminate bar
            var rect = new Rectangle
            {
                Width = width * (row.Percent / 100.0),
                Height = height,
                Fill = new LinearGradientBrush(Color.FromRgb(0x21, 0x88, 0xFF), Color.FromRgb(0x00, 0xC8, 0xFF), 0)
            };
            SegmentCanvas.Children.Add(rect);
            return;
        }

        lock (job.SegmentGate)
        {
            foreach (var seg in job.Segments)
            {
                double segStartX = (seg.Start / (double)total) * width;
                double segWrittenWidth = Math.Max(0, (seg.WrittenBytes / (double)total) * width);

                if (segWrittenWidth > 0)
                {
                    var chunkRect = new Rectangle
                    {
                        Width = segWrittenWidth,
                        Height = height,
                        Fill = new LinearGradientBrush(
                            Color.FromRgb(0x1E, 0x88, 0xE5),
                            Color.FromRgb(0x42, 0xA5, 0xF5),
                            90)
                    };
                    Canvas.SetLeft(chunkRect, segStartX);
                    Canvas.SetTop(chunkRect, 0);
                    SegmentCanvas.Children.Add(chunkRect);
                }

                // Iconic red vertical marker line at connection start position
                var divider = new System.Windows.Shapes.Line
                {
                    X1 = segStartX,
                    Y1 = 0,
                    X2 = segStartX,
                    Y2 = height,
                    Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                    StrokeThickness = 1.2
                };
                SegmentCanvas.Children.Add(divider);
            }
        }
    }

    private void SegmentCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSegments();
    }

    private void DetailsToggle_Click(object sender, RoutedEventArgs e)
    {
        detailsVisible = !detailsVisible;
        if (detailsVisible)
        {
            DetailsGrid.Visibility = Visibility.Visible;
            MinHeight = 420;
            DetailsToggleButton.Content = "<< Hide details";
            Height = normalHeight;
        }
        else
        {
            normalHeight = Height;
            DetailsGrid.Visibility = Visibility.Collapsed;
            DetailsToggleButton.Content = ">> Show details";
            MinHeight = 340; Height = 340;
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (row.CanPause)
        {
            if (RememberLimiterCheck.IsChecked != true) { UseSpeedLimiterCheck.IsChecked = false; row.Job.SpeedLimitBytes = null; }
            onPause(row.Job);
        }
        else if (row.CanResume)
        {
            onResume(row);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (row.CanPause) onPause(row.Job);
        Close();
    }

    private void SpeedLimiter_Changed(object sender, RoutedEventArgs e)
    {
        ApplySpeedLimiter();
    }

    private void SpeedLimitValBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySpeedLimiter();
    }

    private void ApplySpeedLimiter()
    {
        if (initializing) return;
        if (UseSpeedLimiterCheck.IsChecked == true && int.TryParse(SpeedLimitValBox.Text.Trim(), out int kbps) && kbps > 0)
        {
            row.Job.SpeedLimitBytes = kbps * 1024L;
        }
        else
        {
            if (UseSpeedLimiterCheck.IsChecked == true) { SpeedLimitValBox.ToolTip = "Enter a positive speed in KBytes/sec."; return; }
            row.Job.SpeedLimitBytes = null;
        }
    }

    private void CompleteOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (TurnOffCheck == null || ShutdownActionBox == null || ExitOnCompleteCheck == null || ForceProcessesCheck == null) return;
        bool showComplete = ShowCompleteDialogCheck?.IsChecked == true;
        if (!initializing) row.Job.ShowCompleteDialog = showComplete;
        TurnOffCheck.IsEnabled = !showComplete;
        ShutdownActionBox.IsEnabled = !showComplete;
        ExitOnCompleteCheck.IsEnabled = !showComplete;
        ForceProcessesCheck.IsEnabled = !showComplete && ShutdownActionBox.SelectedIndex == 0;
    }

    private void HideTab_Click(object sender, RoutedEventArgs e)
    {
        if (DetailTabs.SelectedItem is TabItem selected && DetailTabs.SelectedIndex > 0) selected.Visibility = Visibility.Collapsed;
        DetailTabs.SelectedIndex = 0;
    }

    private static string Format(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.###} {units[index]}";
    }
}

