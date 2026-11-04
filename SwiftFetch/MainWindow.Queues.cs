using System.Windows;
using System.Windows.Controls;
using SwiftFetch.Core;
namespace SwiftFetch;
public partial class MainWindow
{
    private readonly HashSet<string> runningQueues = [];
    private string selectedQueueName = "Default";
    private readonly Dictionary<string,HashSet<string>> runningQueueJobs = [];
    private readonly Dictionary<string,DateTime> nextSynchronization = [];
    private bool synchronizing;
    private readonly HashSet<string> synchronizationStarted = [];
    private readonly HashSet<string> synchronizationStopped = [];
    
    private void EnsureBuiltInQueues()
    {
        if (!settings.Queues.Any(q => q.Name == "Default")) settings.Queues.Insert(0,new());
        if (!settings.Queues.Any(q => q.Synchronization))
        {
            var existing = settings.Queues.FirstOrDefault(q => q.Name == "Synchronization queue");
            if (existing != null) existing.Synchronization = true;
            else settings.Queues.Add(new() { Name = "Synchronization queue", Synchronization = true });
        }
    }
    private void StopQueue(string name)
    {
        runningQueues.Remove(name); runningQueueJobs.Remove(name); synchronizationStarted.Remove(name); synchronizationStopped.Add(name); scheduler.PauseQueue(name);
    }
    private int QueuePosition(string queue,string id)
    {
        int index = settings.Queues.FirstOrDefault(q => q.Name == queue)?.FileOrder.IndexOf(id) ?? -1;
        return index < 0 ? int.MaxValue : index;
    }
    public void EnsureTrayForProgress() { EnableTray(); if (tray == null) throw new InvalidOperationException("Windows could not create the shared notification icon. The progress window will stay open."); }
    private ContextMenu? trayMenu;
    private void ShowTrayDownloads()
    {
        if (trayMenu != null) trayMenu.IsOpen = false;
        var menu = trayMenu = new ContextMenu();
        void Add(string text,Action action) { var item = new MenuItem { Header = text }; item.Click += (_,_) => action(); menu.Items.Add(item); }
        Add("Open Flux",() => { Show(); WindowState = WindowState.Normal; Activate(); });
        menu.Items.Add(new Separator());
        foreach (var row in Downloads.Where(r => r.CanPause || activeProgressDialogs.ContainsKey(r.Job.Id)).ToArray())
            Add($"{row.PercentText}  {row.FileName}  ·  {row.DisplayStatus}",() => ShowProgressDialog(row));
        if (activeProgressDialogs.Count > 0) Add("Show all progress windows",() => { foreach (var dialog in activeProgressDialogs.Values.ToArray()) dialog.RestoreFromTray(); });
        menu.Items.Add(new Separator());
        Add("Pause all",scheduler.PauseAll);
        Add("Resume all",() => { foreach (var row in Downloads.Where(r => r.CanResume).ToArray()) Start(row); });
        Add("Exit Flux",ExitApplication);
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        ActivateTrayMenu(); menu.IsOpen = true;
    }
    private void ActivateTrayMenu() => SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    
    private string QueueDisplayName(string name) => settings.Queues.FirstOrDefault(q => q.Name == name)?.DisplayName ?? name;
    private void UpdateQueueCommandHints()
    {
        StartQueueToolButton.ToolTip = "Start " + QueueDisplayName(selectedQueueName);
        StopQueueToolButton.ToolTip = "Stop " + QueueDisplayName(selectedQueueName);
    }
    private void SpeedLimitMenu_Opened(object sender,RoutedEventArgs e)
    {
        GlobalLimitOnMenu.IsChecked = settings.GlobalSpeedLimitBytes > 0; GlobalLimitOffMenu.IsChecked = settings.GlobalSpeedLimitBytes == 0;
    }
    private static object QueueTreeHeader(DownloadQueueDefinition queue) => FolderTreeHeader(queue.DisplayName,queue.Synchronization);
    private static object FolderTreeHeader(string name,bool synchronization = false)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M1,4 L6,4 L8,6 L15,6 L15,15 L1,15 Z M2,2 L7,2 L9,4 L16,4"), Fill = synchronization ? System.Windows.Media.Brushes.LightGreen : System.Windows.Media.Brushes.Khaki, Stroke = System.Windows.Media.Brushes.DarkGray, StrokeThickness = .6, Width = 16, Height = 16, Margin = new Thickness(0,0,3,0), VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(icon);
        header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        return header;
    }
    private void RefreshQueueMenus()
    {
        var selectedTag = (CategoryTree.SelectedItem as TreeViewItem)?.Tag as string;
        EnsureBuiltInQueues();
        if (!settings.Queues.Any(q => q.Name == selectedQueueName)) selectedQueueName = "Default";
        QueuesTree.Items.Clear();
        UpdateQueueCommandHints();
        foreach (var queue in settings.Queues) QueuesTree.Items.Add(new TreeViewItem { Header = QueueTreeHeader(queue), Tag = "queue:" + queue.Name, Style = FindResource("TreeEntry") as Style });
        foreach (var item in QueuesTree.Items.OfType<TreeViewItem>()) if (item.Tag as string == selectedTag) item.IsSelected = true;
        StartQueueMenu.Items.Clear(); StopQueueMenu.Items.Clear();
        foreach (var queue in settings.Queues)
        {
            var start = new MenuItem { Header = "Start " + queue.DisplayName }; start.Click += (_,_) => StartQueue(queue.Name); StartQueueMenu.Items.Add(start);
            var stop = new MenuItem { Header = "Stop " + queue.DisplayName }; stop.Click += (_,_) => { StopQueue(queue.Name); }; StopQueueMenu.Items.Add(stop);
        }
    }
    private void QueuesTree_MouseDown(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current != null && current is not TreeViewItem)
            current = current is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        if (current is not TreeViewItem item) return;
        e.Handled = HandleQueueClick(item,e.ClickCount);
    }
    private bool HandleQueueClick(TreeViewItem item,int clickCount)
    {
        if (item == QueuesTree)
        {
            if (clickCount == 2) { QueuesTree.IsExpanded = !QueuesTree.IsExpanded; return true; }
            else if (clickCount == 1) { QueuesTree.IsSelected = true; Category_SelectedItemChanged(this,new(QueuesTree,QueuesTree)); }
        }
        else if (item.Tag is string tag && tag.StartsWith("queue:") && clickCount == 2)
        { item.IsSelected = true; selectedQueueName = tag[6..]; ShowQueueScheduler(selectedQueueName); return true; }
        return false;
    }
    private QueueSchedulerWindow? queueSchedulerWindow;
    private void ShowQueueScheduler(string? name = null)
    {
        if (queueSchedulerWindow != null) { if (name != null) queueSchedulerWindow.SelectQueue(name); queueSchedulerWindow.Show(); queueSchedulerWindow.Activate(); return; }
        var dialog = queueSchedulerWindow = new QueueSchedulerWindow(settings,Downloads,scheduler,
            name => StartQueue(name),name => { StopQueue(name); },
            () => { settings.Validate(); ApplyEngineSettings(); if (!SaveHistory()) throw new System.IO.IOException("Queue settings could not be saved."); RefreshQueueMenus(); scheduler.Pump(); });
        if (name != null) dialog.SelectQueue(name);
        dialog.Closed += (_,_) => queueSchedulerWindow = null; dialog.Show();
    }
    private void CheckQueueCompletion()
    {
        foreach (var name in runningQueues.ToArray())
        {
            var ids = runningQueueJobs.GetValueOrDefault(name);
            var rows = Downloads.Where(r => r.Job.Queue == name && ids?.Contains(r.Job.Id) == true).ToArray();
            if (rows.Any(r => scheduler.IsRunning(r.Job) || r.Job.State == DownloadState.Queued)) continue;
            runningQueues.Remove(name); runningQueueJobs.Remove(name);
            if (rows.Length == 0 || rows.Any(r => r.Job.State != DownloadState.Completed)) continue;
            var queue = settings.Queues.FirstOrDefault(q => q.Name == name); if (queue == null) continue;
            if (queue.OpenFileWhenDone.Length > 0)
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(queue.OpenFileWhenDone) { UseShellExecute = true }); } catch (Exception error) { MessageText.Text = error.Message; }
            if (queue.DisconnectVpnWhenDone) _ = DisconnectVpnAsync();
            if (queue.PowerWhenDone != QueueCompletionAction.None) RunCompletionAction(queue.PowerWhenDone,queue.ExitWhenDone,queue.ForcePowerAction);
            else if (queue.ExitWhenDone) ExitApplication();
        }
    }
    private async void CheckSynchronizationQueues()
    {
        if (synchronizing || closing) return;
        synchronizing = true;
        try
        {
            foreach (var queue in settings.Queues.Where(q => q.Synchronization && !synchronizationStopped.Contains(q.Name) && (synchronizationStarted.Contains(q.Name) || q.Enabled && q.StartTime == null)).ToArray())
            {
                if (nextSynchronization.TryGetValue(queue.Name,out var next) && next > DateTime.Now) continue;
                nextSynchronization[queue.Name] = DateTime.Now.AddMinutes(Math.Clamp(queue.IntervalMinutes,1,1440));
                foreach (var row in Downloads.Where(r => r.Job.Queue == queue.Name && r.Job.State == DownloadState.Completed).ToArray())
                {
                    var job = row.Job;
                    try
                    {
                        var metadata = await engine.ProbeAsync(job,lifetime.Token);
                        bool changed = (job.ETag != null && metadata.ETag != null && job.ETag != metadata.ETag) || (job.LastModified != null && metadata.LastModified != null && job.LastModified != metadata.LastModified) || (job.TotalBytes != null && metadata.TotalBytes != null && job.TotalBytes != metadata.TotalBytes);
                        if (!changed || closing) continue;
                        // Preserve the previous completed file; download the changed version to a unique path.
                        var previous = (job.Destination,job.Segments,job.ETag,job.LastModified,job.TotalBytes,job.CompletedAt,job.StartedAt,job.NameIsCustom);
                        job.Destination = NameDestination(row,job.FileName);
                        job.Segments = []; job.ETag = null; job.LastModified = null; job.TotalBytes = metadata.TotalBytes;
                        job.State = DownloadState.Ready; job.CompletedAt = null; job.StartedAt = null; job.NameIsCustom = true;
                        if (!SaveHistory())
                        {
                            (job.Destination,job.Segments,job.ETag,job.LastModified,job.TotalBytes,job.CompletedAt,job.StartedAt,job.NameIsCustom) = previous;
                            job.State = DownloadState.Completed; row.Refresh(); continue;
                        }
                        Start(row);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception error) { MessageText.Text = "Synchronization: " + error.Message; }
                }
            }
        }
        finally { synchronizing = false; }
    }
    private void GlobalLimitOn_Click(object sender,RoutedEventArgs e)
    {
        settings.GlobalSpeedLimitBytes = Math.Max(1024,settings.SavedGlobalSpeedLimitBytes); ApplyEngineSettings(); SaveHistory();
    }
    private void GlobalLimitOff_Click(object sender,RoutedEventArgs e)
    {
        if (settings.GlobalSpeedLimitBytes > 0) settings.SavedGlobalSpeedLimitBytes = settings.GlobalSpeedLimitBytes;
        settings.GlobalSpeedLimitBytes = 0; ApplyEngineSettings(); SaveHistory();
    }
}

