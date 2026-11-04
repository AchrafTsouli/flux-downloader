using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using SwiftFetch.Core;
using SwiftFetch.Bridge;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftFetch;

public partial class MainWindow : Window
{
    public ObservableCollection<DownloadRow> Downloads { get; } = [];
    private readonly DownloadEngine engine;
    private readonly DownloadScheduler scheduler;
    private readonly IDownloadStateStore store;
    private DownloadSettings settings = new();
    private bool persistenceAvailable = true;
    private readonly string historyPath;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool closing;
    private bool canClose;
    private bool exitRequested;
    public void ExitApplication() { exitRequested = true; Close(); }
    private bool performingCompletionAction;
    private readonly CancellationTokenSource lifetime = new();
    private Task repairTask = Task.CompletedTask;
    public ICollectionView VisibleDownloads { get; private set; } = null!;
    public int ActiveDownloadCount => scheduler.ActiveCount;
    private string libraryFilter = "all";
    private readonly DownloadMarqueeSelection marqueeSelection;
    private readonly Dictionary<string, DownloadProgressDialog> activeProgressDialogs = new();
    private readonly Dictionary<string, PendingDownload> pendingDownloads = new();
    private readonly HashSet<string> scheduledConfirmations = [];
    public int PendingDownloadCount => pendingDownloads.Count;
    public Func<PendingDownload, DownloadConfirmation?>? ConfirmationProvider { get; set; }
    public event Action? BackgroundIdle;

    public MainWindow() : this(null, null) { }

    public MainWindow(string? dataDirectory, string? downloadDirectory, DownloadEngine? downloadEngine = null)
    {
        historyPath = Path.Combine(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch"), "history.json");
        store = new SqliteDownloadStateStore(Path.ChangeExtension(historyPath, ".db"), historyPath);
        InitializeComponent();
        marqueeSelection = new(DownloadGrid);
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "app-icon.png");
            if (File.Exists(iconPath))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(iconPath);
                bmp.EndInit();
                bmp.Freeze();
                Icon = bmp;
            }
            else Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/SwiftFetch;component/app-icon.png"));
        }
        catch { }
        VisibleDownloads = CollectionViewSource.GetDefaultView(Downloads);
        DataContext = this;
        try
        {
            var snapshot = store.Load();
            settings = snapshot.Settings;
            if (downloadDirectory != null) settings.DownloadDirectory = Path.GetFullPath(downloadDirectory);
            foreach (var job in snapshot.Downloads)
                {
                    // Old versions created cancelled placeholders before Start.
                    if (job.State == DownloadState.Cancelled && job.BrowserRequestId != null && job.StartedAt == null && job.Segments.Count == 0) continue;
                    if (job.State is DownloadState.Queued or DownloadState.Connecting or DownloadState.Resolving or DownloadState.Downloading or DownloadState.Retrying or DownloadState.Verifying or DownloadState.Finishing) job.State = DownloadState.Paused;
                    Downloads.Add(new(job));
                }
            foreach (var pending in snapshot.PendingDownloads)
                if (!Downloads.Any(row => row.Job.BrowserRequestId == pending.RequestId)) pendingDownloads[pending.RequestId] = pending;
            if (store.RecoveredFromBackup) MessageText.Text = "Download history recovered from its last good backup.";
            DownloadCategories.EnsureAllDirectoriesExist(settings.DownloadDirectory);
        }
        catch (Exception ex) { persistenceAvailable = false; MessageText.Text = "History preserved; downloads are disabled until it can be recovered. " + ex.Message; }
        engine = downloadEngine ?? new(options: new EngineOptions { MaximumRetries = settings.MaximumRetries, VerifySavedBytesWithoutValidator = settings.VerifySavedBytesWithoutValidator });
        EnsureBuiltInQueues();
        ApplyEngineSettings();
        scheduler = new(RunJobAsync, () => settings.MaximumActiveDownloads, name => settings.Queues.FirstOrDefault(queue => queue.Name == name)?.MaximumActive ?? settings.MaximumActiveDownloads);
        scheduler.Changed += job =>
        {
            var row = Downloads.FirstOrDefault(value => value.Job == job);
            row?.Refresh();
            if (libraryFilter != "all" && !marqueeSelection.IsSelecting) RefreshLibraryView();
            if (!scheduler.IsRunning(job)) row?.Stop();
            UpdateSummary();
            SaveHistory();
            CheckBackgroundIdle();
            if (scheduler.ActiveCount == 0 && vpnConnected && settings.DisconnectVpnWhenIdle) _ = DisconnectVpnAsync();
        };
        Downloads.CollectionChanged += (_, _) => UpdateSummary();
        Loaded += (_, _) => { repairTask = RepairOldNamesAsync(); RestorePendingConfirmations(); };
        // Background update checks only for the real installation, never for isolated test instances.
        if (dataDirectory == null)
        {
            var updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            updateTimer.Tick += async (_, _) => await RunScheduledUpdatesAsync();
            Loaded += async (_, _) => { updateTimer.Start(); await Task.Delay(TimeSpan.FromSeconds(20)); if (!closing) await RunScheduledUpdatesAsync(); };
        }
        SourceInitialized += (_, _) => { ApplyCaptionTheme(); Dispatcher.BeginInvoke(() => { if (settings.ShowTrayIcon) EnableTray(); }); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && settings.ShowTrayIcon) { EnableTray(); if (tray != null) Hide(); else WindowState = WindowState.Normal; } };
        Activated += (_, _) => { foreach (var row in Downloads) row.Refresh(); UpdateSummary(); };
        timer.Tick += (_, _) => RefreshActiveDownloads();
        timer.Start();
        Dispatcher.BeginInvoke(() => { foreach (var q in settings.Queues.Where(q => q.StartOnStartup)) StartQueue(q.Name); });
        UpdateSummary();
        ApplyViewSettings();
    }

    private string lastClipboardUrl = "";
    private void CheckClipboard()
    {
        if (!settings.MonitorClipboard || !IsVisible || Application.Current.Windows.Cast<Window>().Any(window => window != this && window.IsVisible)) return;
        try
        {
            if (!Clipboard.ContainsText()) return;
            string text = Clipboard.GetText().Trim();
            if (string.IsNullOrEmpty(text) || text == lastClipboardUrl || text.Length > 2048) return;
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && string.IsNullOrEmpty(uri.UserInfo))
            {
                lastClipboardUrl = text;
                string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                string[] knownExts = [".zip", ".rar", ".7z", ".tar", ".gz", ".iso", ".exe", ".msi", ".bin", ".mp4", ".mkv", ".mp3", ".flac", ".pdf", ".dmg", ".pkg", ".apk"];
                if ((knownExts.Contains(ext) || VideoPages.IsKnownVideoPage(text)) && !Downloads.Any(d => d.Job.Url == text || d.Job.Url == VideoPages.Canonicalize(text)))
                {
                    MessageText.Text = "Clipboard URL detected: " + (VideoPages.IsKnownVideoPage(text) ? VideoPages.SiteName(text) + " video" : Path.GetFileName(uri.AbsolutePath));
                    ConfirmUrl(uri);
                }
            }
        }
        catch { }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose download folder" };
        if (dialog.ShowDialog(this) == true) { settings.DownloadDirectory = dialog.FolderName; SaveHistory(); }
    }

    public DownloadSettings Preferences => settings;
    public void AddManualDownload(string raw, bool startNow = true, string? expectedSha256 = null)
    {
        List<string> urls;
        try { urls = BatchUrlExpander.Expand(raw); }
        catch (ArgumentException error) { MessageText.Text = error.Message; return; }
        if (urls.Count > 1)
        {
            int addedCount = 0;
            foreach (var item in urls)
            {
                if (Uri.TryCreate(item, UriKind.Absolute, out var itemUri) && (itemUri.Scheme == "http" || itemUri.Scheme == "https") && string.IsNullOrEmpty(itemUri.UserInfo))
                {
                    try { Enqueue(itemUri, startNow: startNow); addedCount++; } catch { }
                }
            }
            MessageText.Text = $"Batch added {addedCount} downloads.";
            return;
        }
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !string.IsNullOrEmpty(uri.UserInfo))
        { MessageText.Text = "Enter a valid HTTP/HTTPS link without embedded credentials."; return; }
        try
        {
            Enqueue(uri, startNow: startNow, expectedSha256: expectedSha256);
        }
        catch (Exception ex) { MessageText.Text = "Could not start download: " + ex.Message; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public void SetIntegrationMessage(string message) => Dispatcher.Invoke(() => MessageText.Text = message);

    public BridgeResponse AcceptBrowserRequest(BridgeRequest request, bool showWindow = true)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => AcceptBrowserRequest(request, showWindow));
        string? validation = Protocol.Validate(request);
        if (validation != null) return new(false, validation);
        if (closing || performingCompletionAction) return new(false, "Flux is saving and pausing its downloads. Try again in a moment.");
        if (request.Action == "ping") return new(true);
        if (request.Action == "quit") { Dispatcher.BeginInvoke(new Action(ExitApplication),DispatcherPriority.ContextIdle); return new(true); }
        if (request.Action == "capture-policy") return new(true, Policy: new(settings.BrowserCaptureEnabled, settings.BrowserContextMenuEnabled, settings.CaptureExtensions, settings.ExcludedSites, settings.ExcludedAddresses, settings.BypassKey, settings.ForceKey, settings.BrowserMediaPanelEnabled));
        if (request.Action == "show" && showWindow)
        {
            Dispatcher.Invoke(() =>
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Show();
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero)
                {
                    try { ShowWindow(handle, 9); SetForegroundWindow(handle); }
                    catch { }
                }
            });
        }
        if (request.Action == "show") return new(true);
        if (request.Action == "pause-all")
        {
            Dispatcher.Invoke(() => scheduler.PauseAll());
            return new(true);
        }
        if (request.Action == "resume-all")
        {
            Dispatcher.Invoke(() => { foreach (var r in Downloads.Where(v => v.CanResume)) Start(r); });
            return new(true);
        }
        if (request.Action == "pause")
        {
            Dispatcher.Invoke(() =>
            {
                var row = Downloads.FirstOrDefault(r => r.Job.Id == request.RequestId || r.Job.Url == request.Url);
                if (row != null) scheduler.Pause(row.Job);
            });
            return new(true);
        }
        if (request.Action == "resume")
        {
            Dispatcher.Invoke(() =>
            {
                var row = Downloads.FirstOrDefault(r => r.Job.Id == request.RequestId || r.Job.Url == request.Url);
                if (row != null && row.CanResume) Start(row);
            });
            return new(true);
        }
        var existing = Downloads.FirstOrDefault(row => row.Job.BrowserRequestId == request.RequestId);
        if (existing != null) return new(true, JobId: existing.Job.Id);
        try
        {
            bool confirm = showWindow && settings.ShowStartDownloadDialog;
            if (confirm)
            {
                if (!pendingDownloads.TryGetValue(request.RequestId!, out var pending))
                {
                    pending = new() { RequestId = request.RequestId!, Url = request.Url!, FileName = request.FileName, Session = request.Session, Video = request.Video };
                    pendingDownloads.Add(pending.RequestId, pending);
                    if (!SaveHistory())
                    {
                        pendingDownloads.Remove(pending.RequestId);
                        return new(false, "Could not save the pending download.");
                    }
                }
                QueueConfirmation(pending);
                return new(true);
            }
            var row = Enqueue(new Uri(request.Url!), request.FileName, request.RequestId, session: request.Session, video: request.Video);
            if (showWindow && settings.ShowProgressOnStart) ShowProgressDialog(row);
            return new(true, JobId: row.Job.Id);
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public void RestorePendingConfirmations()
    {
        foreach (var pending in pendingDownloads.Values.ToArray()) QueueConfirmation(pending);
    }
    private void QueueConfirmation(PendingDownload pending)
    {
        if (!scheduledConfirmations.Add(pending.RequestId)) return;
        Dispatcher.BeginInvoke(async () =>
        {
            if (closing) { scheduledConfirmations.Remove(pending.RequestId); return; }
            try
            {
                DownloadConfirmation? choice;
                if (ConfirmationProvider != null) choice = ConfirmationProvider(pending);
                else
                {
                    var dialog = new DownloadInfoDialog(new Uri(pending.Url), settings, pending.Session, pending.FileName, engine, pending.Video) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    choice = await IndependentDialogs.ShowAsync(dialog, bringToFront: true) ? dialog.Confirmation : null;
                }
                CompleteBrowserConfirmation(pending.RequestId, choice);
            }
            catch (Exception ex) { MessageText.Text = "The pending download was preserved: " + ex.Message; }
            finally { scheduledConfirmations.Remove(pending.RequestId); CheckBackgroundIdle(); }
        });
    }
    public bool CompleteBrowserConfirmation(string requestId, DownloadConfirmation? choice)
    {
        if (closing || !pendingDownloads.Remove(requestId, out var pending)) return false;
        try
        {
            if (choice == null)
            {
                if (!SaveHistory()) { pendingDownloads[requestId] = pending; return false; }
                return true;
            }
            var row = AddConfirmedDownload(choice with { Video = pending.Video }, requestId);
            if (choice.StartNow && ConfirmationProvider == null && settings.ShowProgressOnStart) ShowProgressDialog(row);
            return true;
        }
        catch { pendingDownloads[requestId] = pending; throw; }
    }
    private void CheckBackgroundIdle()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!closing && !performingCompletionAction && !IsVisible && scheduler.ActiveCount == 0 && activeProgressDialogs.Count == 0 && scheduledConfirmations.Count == 0 &&
                !Application.Current.Windows.Cast<Window>().Any(window => window != this && window.IsVisible)) BackgroundIdle?.Invoke();
        });
    }

    private DownloadRow Enqueue(Uri uri, string? suggestedName = null, string? requestId = null, bool startNow = true, string? expectedSha256 = null, DownloadSession? session = null, string? explicitDestination = null, string? explicitCategory = null, bool? nameIsCustom = null, long? knownTotalBytes = null, string queue = "Default", VideoSelection? video = null)
    {
        if (!persistenceAvailable) throw new IOException("Download history is unavailable. Recover history.json or its backup before adding downloads.");
        var duplicate = Downloads.FirstOrDefault(row => row.Job.Url == uri.AbsoluteUri && row.Job.Video?.FormatId == video?.FormatId);
        if (duplicate != null && (settings.DuplicatePolicy == "Use existing" || settings.DuplicatePolicy == "Ask" && MessageBox.Show(this, "This link is already in the list. Use its existing entry?", "Duplicate download", MessageBoxButton.YesNo) == MessageBoxResult.Yes)) { if (startNow && duplicate.CanResume) { duplicate.Job.Queue = "Default"; Start(duplicate); } return duplicate; }
        session ??= settings.ManualSession(uri);
        if (!string.IsNullOrEmpty(expectedSha256) && (expectedSha256.Length != 64 || expectedSha256.Any(c => !Uri.IsHexDigit(c)))) throw new ArgumentException("SHA-256 must contain 64 hexadecimal characters, or leave it empty.");
        
        string destination;
        string category;
        string name;
        bool isCustom = false;

        if (!string.IsNullOrWhiteSpace(explicitDestination))
        {
            destination = explicitDestination;
            name = Path.GetFileName(destination);
            category = explicitCategory ?? settings.CategoryForFile(name);
            isCustom = nameIsCustom ?? true;
            string dir = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(dir);
        }
        else
        {
            name = !string.IsNullOrWhiteSpace(suggestedName)
                ? Path.GetFileName(suggestedName.Replace('\\', '/'))
                : Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
            if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
            category = explicitCategory ?? settings.CategoryForFile(name);
            string folder = settings.GetDirectoryForCategory(category);
            Directory.CreateDirectory(folder);
            destination = FileNames.UniquePath(folder, name, path => Downloads.Any(r => r.Job.Destination.Equals(path, StringComparison.OrdinalIgnoreCase)));
        }

        var job = new DownloadJob
        {
            Url = uri.AbsoluteUri,
            Video = video,
            VideoSizeEstimated = video?.Approximate == true,
            Destination = destination,
            BrowserRequestId = requestId,
            Session = session,
            RequestedConnections = settings.DefaultConnections,
            TemporaryDirectory = settings.TemporaryDirectory.Length > 0 ? settings.TemporaryDirectory : null,
            Category = category,
            Queue = startNow ? "Default" : queue,
            NameIsCustom = isCustom,
            TotalBytes = knownTotalBytes ?? video?.TotalBytes,
            ExpectedSha256 = string.IsNullOrWhiteSpace(expectedSha256) ? null : expectedSha256.ToLowerInvariant()
        };
        foreach (var rule in settings.Rules) { if (rule.Matches(uri.AbsoluteUri, name)) { rule.Apply(job); break; } }
        if (startNow || !settings.Queues.Any(q => q.Name == job.Queue)) job.Queue = "Default";
        var row = new DownloadRow(job);
        Downloads.Add(row);
        // Acknowledge only after the download has been saved; no transfer starts on a rejected handoff.
        if (!SaveHistory())
        {
            Downloads.Remove(row);
            throw new IOException("Could not save the download. Check the SwiftFetch history folder permissions.");
        }
        DownloadGrid.SelectedItem = row;
        if (startNow) { row.Job.Queue = "Default"; Start(row); }
        else
        {
            row.Job.State = DownloadState.Ready;
            MessageText.Text = "Saved for later: " + row.FileName;
        }
        return row;
    }

    private async Task RepairOldNamesAsync()
    {
        foreach (var row in Downloads.Where(r => r.Status == "Completed" && File.Exists(r.Job.Destination) && !r.Job.NameIsCustom && FileNames.IsOpaque(r.FileName)).ToArray())
        {
            try
            {
                string? name = await engine.GetSuggestedNameAsync(row.Job, lifetime.Token);
                if (name == null || FileNames.IsOpaque(name) || !File.Exists(row.Job.Destination)) continue;
                string destination = NameDestination(row, name);
                File.Move(row.Job.Destination, destination, false);
                row.Job.Destination = destination;
                row.Refresh();
                SaveHistory();
                MessageText.Text = "Restored filename: " + row.FileName;
            }
            catch (OperationCanceledException) { break; }
            catch { /* Expired links keep their existing files and can be renamed manually. */ }
        }
    }

    private string NameDestination(DownloadRow row, string name) => FileNames.UniquePath(Path.GetDirectoryName(row.Job.Destination)!, name, path => Downloads.Any(r => r != row && r.Job.Destination.Equals(path, StringComparison.OrdinalIgnoreCase)));

    private void Start(DownloadRow row)
    {
        if (closing || performingCompletionAction || !persistenceAvailable || !row.CanResume) return;
        if (!runningQueueJobs.TryGetValue(row.Job.Queue,out var queueJobs)) runningQueueJobs[row.Job.Queue] = queueJobs = [];
        queueJobs.Add(row.Job.Id);
        runningQueues.Add(row.Job.Queue);
        scheduler.StartQueue(row.Job.Queue);
        scheduler.Enqueue(row.Job);
    }
    private async Task RunJobAsync(DownloadJob job, CancellationToken token)
    {
        var row = Downloads.First(value => value.Job == job);
        row.Stop(); // A resumed/retried transfer starts with a fresh speed estimate.
        job.MaximumRetries = settings.Queues.FirstOrDefault(q => q.Name == job.Queue)?.Retries ?? settings.MaximumRetries;
        await EnsureVpnAsync(token);
        var progress = new Progress<DownloadProgress>(p =>
        {
            row.Update(p);
            if (!string.IsNullOrEmpty(job.ResumeNote)) MessageText.Text = job.FileName + ": " + job.ResumeNote;
        });
        if (job.Video != null)
        {
            await CreateVideoTools().DownloadAsync(job,progress,token,
                () => { if (!SaveHistory()) throw new IOException("Could not save the video checkpoint."); },
                settings.GlobalSpeedLimitBytes > 0 ? Math.Max(1,settings.GlobalSpeedLimitBytes / Math.Max(1,scheduler.ActiveCount)) : 0,
                path => Downloads.Any(r => r.Job != job && r.Job.Destination.Equals(path,StringComparison.OrdinalIgnoreCase)));
        }
        else await engine.DownloadAsync(row.Job, progress, token, name =>
        {
            row.Job.Destination = NameDestination(row, name);
            row.Refresh();
            SaveHistory();
        }, () => { if (!SaveHistory()) throw new IOException("Could not save segment checkpoints. Download stopped to preserve recovery state."); });
        row.Stop();
        row.Refresh();
        if (row.Job.ErrorKind != DownloadErrorKind.None) { MessageText.Text = row.FileName + ": " + row.Job.Error; if (settings.PlayErrorSound) System.Media.SystemSounds.Hand.Play(); }
        else if (row.Job.Status == "Completed")
        {
            MessageText.Text = (job.IntegrityVerified ? "SHA-256 verified · " : "Saved ") + row.Job.Destination;
            if (settings.PlayCompletionSound) System.Media.SystemSounds.Asterisk.Play();
            if ((job.ShowCompleteDialog ?? settings.ShowDownloadCompleteDialog) && !activeProgressDialogs.ContainsKey(row.Job.Id))
            {
                Dispatcher.Invoke(() =>
                {
                    var completeDialog = new DownloadCompleteDialog(row.Job, dontShow =>
                    {
                        settings.ShowDownloadCompleteDialog = !dontShow;
                        SaveHistory();
                    });
                    completeDialog.Closed += (_, _) => CheckBackgroundIdle();
                    _ = IndependentDialogs.ShowAsync(completeDialog, bringToFront: true);
                });
            }
        }
        else if (!string.IsNullOrEmpty(job.ResumeNote)) MessageText.Text = job.FileName + ": " + job.ResumeNote;
        SaveHistory();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows().Where(row => row.CanPause)) scheduler.Pause(row.Job);
    }
    private void Resume_Click(object sender, RoutedEventArgs e)
    { foreach (var row in SelectedRows().Where(row => row.CanResume)) Start(row); }
    private void ShowFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row) return;
        try
        {
            if (File.Exists(row.Job.Destination)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Job.Destination}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(row.Job.Destination)!) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private void UpdateSummary()
    {
        if (TotalCount == null) return;
        var visible = VisibleDownloads.Cast<DownloadRow>().ToArray();
        TotalCount.Text = visible.Length.ToString();
        ActiveCount.Text = visible.Count(r => r.Job.State is DownloadState.Connecting or DownloadState.Resolving or DownloadState.Downloading or DownloadState.Retrying or DownloadState.Verifying or DownloadState.Finishing).ToString();
        CompletedCount.Text = visible.Count(r => r.Status == "Completed").ToString();
        UpdateSelectedActions();
        EmptyState.Visibility = VisibleDownloads.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (VisibleDownloads == null) return;
        string query = SearchBox.Text.Trim();
        VisibleDownloads.Filter = item => item is DownloadRow row && (query.Length == 0 || row.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Source.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Job.Category.Contains(query, StringComparison.OrdinalIgnoreCase)) && MatchesLibrary(row);
        UpdateSummary();
    }
    private void RefreshLibraryView()
    {
        var selected = SelectedRows(); var current = DownloadGrid.CurrentCell;
        VisibleDownloads.Refresh();
        if (current.IsValid && VisibleDownloads.Contains(current.Item)) DownloadGrid.CurrentCell = current;
        foreach (var row in selected) if (VisibleDownloads.Contains(row) && !DownloadGrid.SelectedItems.Contains(row)) DownloadGrid.SelectedItems.Add(row);
    }
    private bool MatchesLibrary(DownloadRow row) => libraryFilter switch
    {
        "all" => true,
        "completed" => row.Job.State == DownloadState.Completed,
        "unfinished" => DownloadQueueMembership.IsUnfinished(row.Job.State),
        "failed" => DownloadQueueMembership.IsFailure(row.Job.State),
        "paused" => row.Job.State == DownloadState.Paused,
        "grabber" => row.Job.GrabberProject != null,
        _ => libraryFilter.StartsWith("queue:", StringComparison.Ordinal) ? row.Job.Queue == libraryFilter[6..] && DownloadQueueMembership.IsUnfinished(row.Job.State) : libraryFilter.StartsWith("category:", StringComparison.Ordinal) && row.Job.Category == libraryFilter[9..]
    };
    private void Category_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (VisibleDownloads == null || e.NewValue is not TreeViewItem item || item.Tag is not string tag) return;
        if (tag == "queued" || tag.StartsWith("queue:",StringComparison.Ordinal))
        {
            selectedQueueName = tag == "queued" ? "Default" : tag[6..];
            string queueFilter = "queue:" + selectedQueueName;
            bool changedView = libraryFilter != queueFilter;
            libraryFilter = queueFilter; LibraryTitle.Text = QueueDisplayName(selectedQueueName);
            UpdateQueueCommandHints();
            if (VisibleDownloads.Filter == null) Search_Changed(sender,new TextChangedEventArgs(TextBox.TextChangedEvent,UndoAction.None));
            else if (changedView) RefreshLibraryView();
            UpdateSummary();
            return;
        }
        libraryFilter = tag;
        string headerText = item.Header switch
        {
            StackPanel sp when sp.Children.OfType<TextBlock>().LastOrDefault() is TextBlock tb => tb.Text,
            string s => s,
            _ => item.Header?.ToString() ?? ""
        };
        LibraryTitle.Text = headerText;
        Search_Changed(sender, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
    }
    private void Scheduler_Click(object sender, RoutedEventArgs e) => ShowQueueScheduler(selectedQueueName);
    private void CloseCategories_Click(object sender, RoutedEventArgs e)
    {
        settings.ShowCategories = CategoryColumn.Width.Value == 0;
        CategoriesMenu.IsChecked = settings.ShowCategories;
        CategoryColumn.Width = settings.ShowCategories ? new GridLength(220) : new GridLength(0); SaveHistory();
    }
    private async void ShowAdd_Click(object sender, RoutedEventArgs e)
    {
        var addDialog = new AddUrlDialog();
        if (!await IndependentDialogs.ShowAsync(addDialog) || closing) return;

        string raw = addDialog.Url.Trim();
        List<string> urls;
        try { urls = BatchUrlExpander.Expand(raw); }
        catch (ArgumentException error) { MessageText.Text = error.Message; return; }
        if (urls.Count > 1)
        {
            int addedCount = 0;
            foreach (var item in urls)
            {
                if (Uri.TryCreate(item, UriKind.Absolute, out var itemUri) && (itemUri.Scheme == "http" || itemUri.Scheme == "https") && string.IsNullOrEmpty(itemUri.UserInfo))
                {
                    try { Enqueue(itemUri, startNow: false); addedCount++; } catch { }
                }
            }
            MessageText.Text = $"Batch added {addedCount} downloads.";
            return;
        }

        DownloadSession? session = null;
        if (!string.IsNullOrEmpty(addDialog.Username) || !string.IsNullOrEmpty(addDialog.Password))
        {
            try
            {
                string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{(addDialog.Username ?? "")}:{(addDialog.Password ?? "")}"));
                session = new DownloadSession
                {
                    Origin = new Uri(raw).GetLeftPart(UriPartial.Authority) + "/",
                    Authorization = "Basic " + auth
                };
            }
            catch { }
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            MessageBox.Show(this, "Enter a valid HTTP or HTTPS address to download.", "Flux", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if ((addDialog.TreatAsVideo || VideoPages.IsKnownVideoPage(uri.AbsoluteUri)) && await TryOpenVideoAsync(uri, session ?? settings.ManualSession(uri))) return;

        if (settings.ShowStartDownloadDialog)
        {
            var infoDialog = new DownloadInfoDialog(uri, settings, session, probeEngine: engine);
            if (!await IndependentDialogs.ShowAsync(infoDialog, bringToFront: true) || closing) return;

            try
            {
                var row = AddConfirmedDownload(infoDialog.Confirmation);
                if (infoDialog.StartNow && settings.ShowProgressOnStart)
                {
                    ShowProgressDialog(row);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start download: " + ex.Message, "Flux", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            try
            {
                var row = Enqueue(uri, startNow: true, session: session);
                if (settings.ShowProgressOnStart) ShowProgressDialog(row);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start download: " + ex.Message, "Flux", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void ShowProgressDialog(DownloadRow row)
    {
        if (activeProgressDialogs.TryGetValue(row.Job.Id, out var existing))
        {
            existing.RestoreFromTray();
            return;
        }

        var dlg = new DownloadProgressDialog(
            row,
            engine,
            job => scheduler.Pause(job),
            r => Start(r),
            completedJob =>
            {
                if (completedJob.ShowCompleteDialog ?? settings.ShowDownloadCompleteDialog)
                {
                    var compDialog = new DownloadCompleteDialog(completedJob, dontShow =>
                    {
                        settings.ShowDownloadCompleteDialog = !dontShow;
                        SaveHistory();
                    });
                    compDialog.Closed += (_, _) => CheckBackgroundIdle();
                    _ = IndependentDialogs.ShowAsync(compDialog, bringToFront: true);
                }
            }, settings);

        activeProgressDialogs[row.Job.Id] = dlg;
        dlg.CompletionActionRequested += RunCompletionAction;
        dlg.GroupInTray = EnsureTrayForProgress;
        dlg.Closed += (_, _) => { activeProgressDialogs.Remove(row.Job.Id); CheckBackgroundIdle(); };
        dlg.Show();
    }

    private void ShowProgress_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is DownloadRow row) ShowProgressDialog(row);
    }
    private void FocusSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Focus();
    private void ToggleDetails_Click(object sender, RoutedEventArgs e) => DetailsPanel.Visibility = ((MenuItem)sender).IsChecked ? Visibility.Visible : Visibility.Collapsed;
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();
    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row) return;
        try { Clipboard.SetText(row.Job.Url); MessageText.Text = "Download link copied."; }
        catch (Exception ex) { MessageText.Text = "Could not copy link: " + ex.Message; }
    }

    private async void InspectZip_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row) return;
        try
        {
            MessageText.Text = "Inspecting remote archive structure...";
            var entries = await RemoteZipInspector.InspectAsync(row.Job.Url, row.Job.Session, lifetime.Token, proxy: engine.Options.Proxy);
            string summary = string.Join("\n", entries.Take(25).Select(entry => (entry.IsDirectory ? "[DIR]  " : "       ") + entry.Name + " (" + (entry.UncompressedSize / 1024) + " KB)"));
            if (entries.Count > 25) summary += $"\n... and {entries.Count - 25} more files.";
            MessageBox.Show(this, $"Remote ZIP Contents ({entries.Count} items):\n\n" + summary, "ZIP Archive Preview - " + row.FileName, MessageBoxButton.OK, MessageBoxImage.Information);
            MessageText.Text = $"ZIP inspected: {entries.Count} entries found.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not inspect remote archive:\n\n" + ex.Message, "ZIP Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            MessageText.Text = "ZIP inspection failed: " + ex.Message;
        }
    }

    private async void Checksum_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row || !File.Exists(row.Job.Destination)) return;
        try
        {
            MessageText.Text = "Computing SHA-256 checksum for " + row.FileName + " ...";
            string sha256 = await FileIntegrity.ComputeHashAsync(row.Job.Destination, "SHA-256", lifetime.Token);
            string md5 = await FileIntegrity.ComputeHashAsync(row.Job.Destination, "MD5", lifetime.Token);
            Clipboard.SetText(sha256);
            MessageBox.Show(this, $"File: {row.FileName}\n\nSHA-256: {sha256}\nMD5:     {md5}\n\n(SHA-256 copied to clipboard)", "File Integrity Checksum", MessageBoxButton.OK, MessageBoxImage.Information);
            MessageText.Text = "SHA-256: " + sha256[..16] + "… (copied to clipboard)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not compute checksum:\n\n" + ex.Message, "Checksum Error", MessageBoxButton.OK, MessageBoxImage.Error);
            MessageText.Text = "Checksum error: " + ex.Message;
        }
    }
    private static DataGridRow? ClickedRow(object? source)
    {
        var current = source as DependencyObject;
        while (current != null && current is not DataGridRow)
            current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as DataGridRow;
    }
    private void Download_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (ClickedRow(e.OriginalSource) is not { } row) { DownloadGrid.SelectedItem = null; e.Handled = true; return; }
        if (!DownloadGrid.SelectedItems.Contains(row.Item)) DownloadGrid.SelectedItem = row.Item;
        row.Focus(); e.Handled = true;
    }
    private void Download_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClickedRow(e.OriginalSource)?.Item is not DownloadRow row) return;
        if (row.CanOpen) OpenFile_Click(sender, e);
        else ShowProgressDialog(row);
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.L || e.Key == Key.N)) { ShowAdd_Click(sender, e); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F) { SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.F3) { FindNext_Click(sender, e); e.Handled = true; }
        else if (DownloadGrid.IsKeyboardFocusWithin && e.Key == Key.F2 && DownloadGrid.SelectedItem is DownloadRow { CanRename: true }) { Rename_Click(sender, e); e.Handled = true; }
        else if (DownloadGrid.IsKeyboardFocusWithin && e.Key == Key.Delete) { Remove_Click(sender, e); e.Handled = true; }
    }
    private DownloadRow[] SelectedRows() => DownloadGrid.SelectedItems.OfType<DownloadRow>().ToArray();
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ConfirmRemove(SelectedRows().Where(row => row.CanRemove).ToArray());
    }
    private void RemoveCompleted_Click(object sender, RoutedEventArgs e) => ConfirmRemove(Downloads.Where(row => row.Job.State == DownloadState.Completed).ToArray());
    private void ConfirmRemove(DownloadRow[] rows)
    {
        if (rows.Length == 0) return;
        if (!settings.ConfirmDeleteDownloads) { DeleteConfirmedDownloads(rows,false); return; }
        var dialog = new DeleteDownloadsDialog(rows.Length) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (dialog.DontAskAgain)
        {
            settings.ConfirmDeleteDownloads = false;
            if (!SaveHistory()) { settings.ConfirmDeleteDownloads = true; return; }
        }
        DeleteConfirmedDownloads(rows,dialog.DeleteCompletedFiles);
    }
    private void DeleteConfirmedDownloads(DownloadRow[] rows,bool deleteCompletedFiles)
    {
        if (!RemoveFromHistory(rows)) return;
        if (!deleteCompletedFiles) return;
        var failed = new List<string>();
        foreach (var row in rows.Where(row => row.Job.State == DownloadState.Completed))
            try { File.Delete(row.Job.Destination); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed.Add(row.FileName + ": " + error.Message); }
        MessageText.Text = failed.Count == 0 ? "Removed selected list entries and their completed files. Partial downloads were kept." : "List entries removed; some files could not be deleted: " + string.Join("; ",failed);
    }

    public bool RemoveFromHistory(IEnumerable<DownloadRow> rows)
    {
        if (closing || !persistenceAvailable) return false;
        var removed = rows.Distinct().Select(row => (Row: row, Index: Downloads.IndexOf(row))).OrderBy(value => value.Index).ToArray();
        if (removed.Length == 0 || removed.Any(value => value.Index < 0 || !value.Row.CanRemove || scheduler.IsRunning(value.Row.Job))) return false;
        foreach (var entry in removed) Downloads.Remove(entry.Row);
        if (!SaveHistory())
        {
            foreach (var entry in removed) Downloads.Insert(entry.Index, entry.Row);
            return false;
        }
        UpdateSummary();
        MessageText.Text = $"Removed {removed.Length} history entries. Files on disk were kept.";
        return true;
    }
    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row || row.Status != "Completed") return;
        try { Process.Start(new ProcessStartInfo(row.Job.Destination) { UseShellExecute = true }); }
        catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row || scheduler.IsRunning(row.Job) || row.Job.State == DownloadState.Queued) { MessageText.Text = "Pause the download before renaming it."; return; }
        var dialog = new RenameWindow(row.FileName) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string name = FileNames.Sanitize(dialog.FileName);
            if (name.Equals(row.FileName, StringComparison.OrdinalIgnoreCase)) return;
            string destination = NameDestination(row, name);
            var moves = new List<(string Old, string New)>();
            if (row.Status == "Completed") moves.Add((row.Job.Destination, destination));
            else if (string.IsNullOrEmpty(row.Job.TemporaryDirectory)) for (int i = 0; i < row.Job.Segments.Count; i++) if (File.Exists(row.Job.PartPath(i))) moves.Add((row.Job.PartPath(i), destination + $".{row.Job.Id}.{i}.part"));
            var moved = new List<(string Old, string New)>();
            try { foreach (var move in moves) { File.Move(move.Old, move.New, false); moved.Add(move); } }
            catch { foreach (var move in moved.AsEnumerable().Reverse()) File.Move(move.New, move.Old, false); throw; }
            row.Job.Destination = destination;
            row.Job.NameIsCustom = true;
            row.Refresh();
            SaveHistory();
            MessageText.Text = "Renamed to " + row.FileName;
        }
        catch (Exception ex) { MessageText.Text = "Could not rename file: " + ex.Message; }
    }
    private bool SaveHistory()
    {
        if (!persistenceAvailable) return false;
        try
        {
            store.Save(new() { Settings = settings, Downloads = Downloads.Select(r => r.Job).ToList(), PendingDownloads = pendingDownloads.Values.ToList() });
            return true;
        }
        catch (Exception ex) { MessageText.Text = "Could not save download history: " + ex.Message; return false; }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (canClose) return;
        e.Cancel = true;
        if (!exitRequested) { EnableTray(); if (tray != null) Hide(); else { Show(); WindowState = WindowState.Normal; } return; }
        if (closing) return;
        closing = true;
        IsEnabled = false;
        timer.Stop();
        lifetime.Cancel();
        MessageText.Text = "Pausing downloads and saving history…";
        await scheduler.ShutdownAsync();
        await repairTask;
        SaveHistory();
        if (persistenceAvailable && store is SqliteDownloadStateStore database) database.CreateBackup();
        engine.Dispose();
        if (trayMenu != null) trayMenu.IsOpen = false;
        queueSchedulerWindow?.Close();
        foreach (var dialog in activeProgressDialogs.Values.ToArray()) dialog.Close();
        tray?.Dispose(); tray = null; dropWindow?.Close();
        canClose = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    private void PauseAll_Click(object sender, RoutedEventArgs e) => scheduler.PauseAll();
    private void ResumeAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in Downloads.Where(value => value.CanResume).ToArray()) Start(row);
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }
    private void OpenSettings(string? tab = null)
    {
        var dialog = new SettingsWindow(settings, tab) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        foreach (var property in typeof(DownloadSettings).GetProperties().Where(p => p.CanWrite)) property.SetValue(settings,property.GetValue(dialog.Result));
        ApplyEngineSettings(); ApplyViewSettings();
        SaveHistory();
        scheduler.Pump();
        MessageText.Text = "Settings saved. New queue slots: " + settings.MaximumActiveDownloads;
    }
    private void Session_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadGrid.SelectedItem is not DownloadRow row || row.CanPause || row.Job.State == DownloadState.Completed) return;
        var dialog = new SessionWindow(row.Job) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var previous = row.Job.Session;
        row.Job.Session = dialog.Session;
        if (!SaveHistory()) { row.Job.Session = previous; return; }
        Start(row);
    }
}

public sealed class DownloadRow : INotifyPropertyChanged
{
    public DownloadJob Job { get; }
    private DownloadProgress latest = new(0, null, 0, 0);
    private readonly TransferRateDisplay transferRate = new();
    public DownloadRow(DownloadJob job) { Job = job; if (Job.Category == "Other") Job.Category = DownloadCategories.ForFile(Job.FileName); Refresh(); }
    public string FileName => Job.FileName;
    public string Source => (Uri.TryCreate(Job.Url, UriKind.Absolute, out var uri) ? uri.Host : "Direct download") + " · " + Job.Category;
    public string SourceDomain => Uri.TryCreate(Job.Url, UriKind.Absolute, out var uri) ? uri.Host : "Direct download";
    public string AddedText => Job.CreatedAt?.ToLocalTime().ToString("dd MMM HH:mm") ?? "—";
    public string TotalSizeText => Job.TotalBytes is long total ? (Job.VideoSizeEstimated ? "≈ " : "") + Format(total) : "Unknown";
    public string StatusDetail => !string.IsNullOrEmpty(Job.Error) ? Job.Error : !string.IsNullOrEmpty(Job.ResumeNote) ? Job.ResumeNote : Job.Category + " · " + SizeText + " · " + Job.RequestedConnections + " requested connections";
    public string FileType => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension ? extension : "FILE";
    public string QueueText => DownloadQueueMembership.IsUnfinished(Job.State) && Job.Queue != "Unassigned" ? "▣" : "";
    public string DescriptionText => string.IsNullOrWhiteSpace(Job.Description) ? SourceDomain : Job.Description;
    public string LastTryText => Job.CreatedAt?.ToLocalTime().ToString("MMM dd HH:mm") ?? "—";
    public string IconType => FileType switch
    {
        "EXE" or "MSI" or "BAT" or "CMD" => "exe",
        "PDF" => "pdf",
        "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" or "BZ2" => "zip",
        "ISO" or "IMG" or "BIN" => "iso",
        "MP4" or "MKV" or "AVI" or "MOV" or "WMV" or "WEBM" => "video",
        "MP3" or "FLAC" or "WAV" or "AAC" or "OGG" or "M4A" => "audio",
        "PNG" or "JPG" or "JPEG" or "GIF" or "WEBP" or "BMP" => "image",
        "DOC" or "DOCX" or "TXT" or "RTF" => "doc",
        "PPT" or "PPTX" => "ppt",
        "XLS" or "XLSX" or "CSV" => "xls",
        _ => Job.Category switch
        {
            "Programs" => "exe",
            "Archives" => "zip",
            "Video" => "video",
            "Music" => "audio",
            "Images" => "image",
            _ => "file"
        }
    };
    public string PercentText => Job.Status == "Completed" ? "100%" : latest.Total is > 0 ? $"{Percent:0}%" : "…";
    public string DisplayStatus => Job.Status == "Completed" && !File.Exists(Job.Destination) ? "File missing" : Job.State == DownloadState.Completed && Job.IntegrityVerified ? "Verified" : Job.State == DownloadState.Retrying ? "Retry " + Job.RetriesPerformed : Job.Status == "Completed" ? "Complete" : Job.Status;
    public string StatusColor => Theme(DisplayStatus switch { "Complete" or "Completed" or "Verified" => "StatusGood", "Failed" or "AuthRequired" or "ExpiredUrl" or "NoDiskSpace" => "StatusBad", "Paused" or "File missing" or "Queued" => "StatusWarn", _ => "Accent" });
    // Status colors follow the palette and the dark/light mode (set in MainWindow.ApplyTheme).
    private static string Theme(string key) => (Application.Current?.TryFindResource(key) as System.Windows.Media.SolidColorBrush)?.Color.ToString() ?? "#38BDF8";
    public string Status => Job.Status;
    public bool CanPause => Job.State is DownloadState.Queued or DownloadState.Connecting or DownloadState.Resolving or DownloadState.Downloading or DownloadState.Retrying or DownloadState.Verifying or DownloadState.Finishing;
    public bool CanResume => Job.State is DownloadState.Paused or DownloadState.Failed or DownloadState.Ready or DownloadState.AuthRequired or DownloadState.ExpiredUrl or DownloadState.NoDiskSpace;
    public bool CanOpen => Job.Status == "Completed" && File.Exists(Job.Destination);
    public bool CanRename => !CanPause && DisplayStatus != "File missing";
    public bool CanEditSession => CanResume;
    public bool CanRemove => !CanPause;
    public bool CanShowFolder => true;
    public int Connections => Job.Video != null ? (Job.State == DownloadState.Downloading ? 1 : 0) : Job.Segments.Count;
    public double Percent => latest.Total is > 0 ? Math.Clamp(100.0 * latest.Bytes / latest.Total.Value, 0, 100) : Job.Status == "Completed" ? 100 : 0;
    public bool Indeterminate => latest.Total == null && Job.Status is "Connecting" or "Downloading";
    public string SizeText => Format(latest.Bytes) + (latest.Total is long total ? " / " + Format(total) : " / unknown size");
    private double DisplayRate => Job.State == DownloadState.Downloading ? transferRate.BytesPerSecond : 0;
    public string SpeedText { get { double rate = DisplayRate; return rate > 0 ? Format(rate) + "/s" : "—"; } }
    public string EtaText { get { double rate = DisplayRate; return latest.Total is long total && rate > 0 ? TimeSpan.FromSeconds(Math.Clamp((total - latest.Bytes) / rate, 0, 31536000)).ToString(@"hh\:mm\:ss") : "—"; } }
    public void Update(DownloadProgress p) { latest = p; if (Job.State == DownloadState.Downloading) transferRate.Update(p.BytesPerSecond); else transferRate.Reset(); Notify(); }
    public void Stop() { transferRate.Reset(); latest = latest with { BytesPerSecond = 0 }; Notify(); }
    public void Refresh()
    {
        long bytes = Job.Status == "Completed" ? Job.TotalBytes ?? (File.Exists(Job.Destination) ? new FileInfo(Job.Destination).Length : 0) : Job.Video != null ? Job.MediaDownloadedBytes : Enumerable.Range(0, Job.Segments.Count).Sum(i => File.Exists(Job.PartPath(i)) ? new FileInfo(Job.PartPath(i)).Length : 0);
        latest = latest with { Bytes = bytes, Total = Job.TotalBytes };
        Notify();
    }
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public event PropertyChangedEventHandler? PropertyChanged;
    private static string Format(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.#} {units[index]}";
    }
}

