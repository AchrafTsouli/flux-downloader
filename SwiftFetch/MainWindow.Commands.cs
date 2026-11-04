using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SwiftFetch.Core;
namespace SwiftFetch;
public partial class MainWindow
{
    private readonly HashSet<string> firedSchedules = [];
    private readonly SemaphoreSlim vpnGate = new(1,1);
    private bool vpnConnected;
    private Window? dropWindow;
    private async void RunCompletionAction(QueueCompletionAction action,bool exit,bool force)
    {
        if (closing || performingCompletionAction) return;
        if (action == QueueCompletionAction.None) { if (exit) ExitApplication(); return; }
        performingCompletionAction = true;
        try
        {
            await scheduler.PauseAllAsync();
            if (closing) return;
            if (!SaveHistory()) throw new IOException("Downloads could not be saved; the power action was cancelled.");
            SystemPower.Trigger(action,force:force);
            if (exit) ExitApplication();
        }
        catch (Exception error) { MessageText.Text = "Completion action: " + error.Message; }
        finally { performingCompletionAction = false; CheckBackgroundIdle(); }
    }
    private void ApplyEngineSettings()
    {
        engine.Options.MaximumRetries = settings.MaximumRetries;
        engine.Options.VerifySavedBytesWithoutValidator = settings.VerifySavedBytesWithoutValidator;
        engine.Options.IgnoreLastModifiedChanges = settings.IgnoreLastModifiedChanges;
        engine.Options.PreserveServerDate = settings.PreserveServerDate;
        engine.Options.VirusScanner = settings.VirusScanner;
        engine.Options.VirusScannerArguments = settings.VirusScannerArguments;
        engine.Options.GlobalLimiter.BytesPerSecond = settings.GlobalSpeedLimitBytes;
        if (settings.GlobalSpeedLimitBytes > 0) settings.SavedGlobalSpeedLimitBytes = settings.GlobalSpeedLimitBytes;
        engine.Options.Proxy = settings.ProxyAddress.Length == 0 ? null : new WebProxy(settings.ProxyAddress) { Credentials = settings.ProxyUsername.Length == 0 ? null : new NetworkCredential(settings.ProxyUsername, settings.ProxyPassword) };
    }
    private void ApplyViewSettings()
    {
        CategoryColumn.Width = new GridLength(settings.ShowCategories ? 220 : 0);
        MainToolbar.Visibility = settings.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        ToolbarRow.Height = new GridLength(settings.ShowToolbar ? 72 : 0);
        CategoriesMenu.IsChecked = settings.ShowCategories; ToolbarMenu.IsChecked = settings.ShowToolbar; TrayMenu.IsChecked = settings.ShowTrayIcon; DarkMenu.IsChecked = settings.DarkMode;
        FontFamily = new FontFamily(settings.FontFamily); DownloadGrid.FontFamily = FontFamily; DownloadGrid.FontSize = settings.FontSize;
        foreach (var column in DownloadGrid.Columns) if (settings.ColumnVisibility.TryGetValue(column.Header?.ToString() ?? "", out var visible)) column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ApplyTheme(); ApplyLanguage(); RefreshQueueMenus();
        var known = DownloadCategories.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in AllCategory.Items.OfType<TreeViewItem>().Where(item => item.Tag is string tag && tag.StartsWith("category:") && !known.Contains(tag[9..])).ToArray()) AllCategory.Items.Remove(item);
        foreach (var name in settings.CategoryDirectories.Keys.Concat(settings.CategoryExtensions.Keys).Concat(Downloads.Select(row => row.Job.Category)).Distinct().Where(name => !known.Contains(name))) AllCategory.Items.Add(new TreeViewItem { Header = FolderTreeHeader(name), Tag = "category:" + name, Style = FindResource("TreeEntry") as Style });
    }
    private async void ConfirmUrl(Uri uri, DownloadSession? session = null, bool allowVideo = true)
    {
        session ??= settings.ManualSession(uri);
        if (allowVideo && VideoPages.IsKnownVideoPage(uri.AbsoluteUri) && await TryOpenVideoAsync(uri, session)) return;
        if (!settings.ShowStartDownloadDialog) { var added = Enqueue(uri, session: session); if (settings.ShowProgressOnStart) ShowProgressDialog(added); return; }
        var dialog = new DownloadInfoDialog(uri, settings, session, probeEngine: engine);
        if (!await IndependentDialogs.ShowAsync(dialog, bringToFront: true) || closing) return;
        var row = AddConfirmedDownload(dialog.Confirmation);
        if (dialog.StartNow && settings.ShowProgressOnStart) ShowProgressDialog(row);
    }
    private DownloadRow AddConfirmedDownload(DownloadConfirmation choice, string? requestId = null)
    {
        if (choice.RememberFolder) settings.CategoryDirectories[choice.Category] = Path.GetDirectoryName(choice.Destination)!;
        string destination = FileNames.UniquePath(Path.GetDirectoryName(choice.Destination)!,Path.GetFileName(choice.Destination), path => Downloads.Any(row => row.Job.Destination.Equals(path,StringComparison.OrdinalIgnoreCase)));
        var row = Enqueue(choice.Uri,requestId: requestId,startNow: choice.StartNow,session: choice.Session,explicitDestination: destination,explicitCategory: choice.Category,nameIsCustom: choice.NameIsCustom,knownTotalBytes: choice.TotalBytes,queue: choice.Queue,video: choice.Video);
        row.Job.Description = choice.Description; row.Refresh();
        if (!AllCategory.Items.OfType<TreeViewItem>().Any(item => item.Tag as string == "category:" + choice.Category)) AllCategory.Items.Add(new TreeViewItem { Header = FolderTreeHeader(choice.Category),Tag = "category:" + choice.Category,Style = FindResource("TreeEntry") as Style });
        SaveHistory(); return row;
    }
    private async Task EnsureVpnAsync(CancellationToken token)
    {
        if (!settings.ConnectVpnBeforeDownload || settings.VpnProfile.Length == 0) return;
        await vpnGate.WaitAsync(token);
        try { if (!vpnConnected) { await RasDialAsync(false, token); vpnConnected = true; } }
        finally { vpnGate.Release(); }
    }
    private async Task RasDialAsync(bool disconnect, CancellationToken token)
    {
        var command = new ProcessStartInfo("rasdial.exe") { UseShellExecute = false, CreateNoWindow = true };
        command.ArgumentList.Add(settings.VpnProfile); if (disconnect) command.ArgumentList.Add("/disconnect");
        using var process = Process.Start(command) ?? throw new IOException("Could not start Windows VPN connection.");
        await process.WaitForExitAsync(token); if (process.ExitCode != 0) throw new IOException("Windows VPN returned error " + process.ExitCode + ". Check the profile and its saved credentials.");
    }
    private void CheckQueueSchedules()
    {
        var now = DateTime.Now; string day = now.ToString("yyyy-MM-dd");
        firedSchedules.RemoveWhere(key => !key.StartsWith(day));
        foreach (var queue in settings.Queues.Where(queue => queue.Enabled && (queue.Daily ? queue.Weekdays.Contains(now.DayOfWeek) : queue.ScheduledDate == null || queue.ScheduledDate.Value.Date == now.Date)))
        {
            if (queue.StartTime != null && TimeOnly.TryParse(queue.StartTime, out var start) && now.Hour == start.Hour && now.Minute == start.Minute && firedSchedules.Add(day + queue.Name + "start")) { StartQueue(queue.Name); if (!queue.Daily && queue.StopTime == null) queue.Enabled = false; }
            if (queue.StopTime != null && TimeOnly.TryParse(queue.StopTime, out var stop) && now.Hour == stop.Hour && now.Minute == stop.Minute && firedSchedules.Add(day + queue.Name + "stop")) { StopQueue(queue.Name); if (!queue.Daily) queue.Enabled = false; }
        }
    }
    private void StartQueue(string name)
    {
        runningQueues.Add(name); synchronizationStopped.Remove(name); synchronizationStarted.Add(name);
        scheduler.StartQueue(name);
        foreach (var row in Downloads.Where(row => row.Job.Queue == name && row.CanResume && DownloadQueueMembership.IsUnfinished(row.Job.State)).OrderBy(row => QueuePosition(name,row.Job.Id)).ToArray()) Start(row);
        MessageText.Text = "Started queue: " + QueueDisplayName(name);
    }
    private string? ChooseQueue()
    {
        var dialog = new Window { Title = "Choose queue", Width = 360, Height = 200, Owner = this, Background = new SolidColorBrush(Color.FromRgb(27,27,27)), Foreground = Brushes.White, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; var box = new ComboBox { ItemsSource = settings.Queues, DisplayMemberPath = "DisplayName", SelectedIndex = 0, Margin = new Thickness(0,0,0,16) }; panel.Children.Add(box);
        var ok = new Button { Content = "OK", IsDefault = true }; ok.Click += (_, _) => dialog.DialogResult = true; panel.Children.Add(ok); dialog.Content = panel;
        return dialog.ShowDialog() == true ? (box.SelectedItem as DownloadQueueDefinition)?.Name : null;
    }
    private void StartQueue_Click(object sender, RoutedEventArgs e) => StartQueue(selectedQueueName);
    private void StopQueue_Click(object sender, RoutedEventArgs e) { StopQueue(selectedQueueName); MessageText.Text = "Stopped queue: " + QueueDisplayName(selectedQueueName); }
    private void AssignQueue_Click(object sender, RoutedEventArgs e)
    {
        if (ChooseQueue() is not { } name) return;
        foreach (var row in SelectedRows().Where(row => row.CanRemove && !scheduler.IsRunning(row.Job))) { row.Job.Queue = name; row.Refresh(); } SaveHistory();
    }
    private void Batch_Click(object sender, RoutedEventArgs e) => ShowBatch("");
    private void BatchClipboard_Click(object sender, RoutedEventArgs e) { try { ShowBatch(Clipboard.ContainsText() ? Clipboard.GetText() : ""); } catch (Exception ex) { MessageText.Text = ex.Message; } }
    private void ShowBatch(string initial, string? grabberProject = null)
    {
        var dialog = new TextEntryDialog("Add batch downloads", "One HTTP/HTTPS link per line. Number ranges such as file[01-10].zip are supported. Up to 2,000 links. Files are saved for later; use Start queue when ready.", initial, multiline: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var links = dialog.Value.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries).SelectMany(BatchUrlExpander.Expand).Distinct().Take(2001).ToArray();
            if (links.Length > 2000) throw new ArgumentException("A batch is limited to 2,000 links.");
            var urls = links.Select(text => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri : throw new ArgumentException("Invalid URL: " + text)).ToArray();
            string? queue = settings.ShowBatchQueueSelection ? ChooseQueue() : settings.Queues[0].Name; if (queue == null) return;
            foreach (var uri in urls) { var row = Enqueue(uri, startNow: false, queue: queue); row.Job.GrabberProject = grabberProject; }
            SaveHistory(); MessageText.Text = $"Added {urls.Length} files to {queue}.";
        } catch (Exception error) { MessageText.Text = error.Message; }
    }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "URL lists (*.txt)|*.txt", Title = "Import download links" };
        if (dialog.ShowDialog(this) != true) return;
        try { if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new IOException("URL list is too large."); ShowBatch(File.ReadAllText(dialog.FileName)); } catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "URL lists (*.txt)|*.txt", FileName = "flux-downloads.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllLines(dialog.FileName, VisibleDownloads.Cast<DownloadRow>().Select(row => row.Job.Url)); MessageText.Text = "Exported visible download links."; } catch (Exception ex) { MessageText.Text = ex.Message; }
    }
    private void Redownload_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in SelectedRows().Where(row => !row.CanPause))
            if (row.Job.Video != null) AcceptBrowserRequest(new("download",row.Job.Url,row.FileName,Guid.NewGuid().ToString(),row.Job.Session,row.Job.Video));
            else ConfirmUrl(new Uri(row.Job.Url), row.Job.Session, allowVideo: false);
    }
    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        var rows = VisibleDownloads.Cast<DownloadRow>().ToArray(); if (rows.Length == 0) return;
        int index = Array.IndexOf(rows, DownloadGrid.SelectedItem as DownloadRow);
        DownloadGrid.SelectedItem = rows[(index + 1) % rows.Length]; DownloadGrid.ScrollIntoView(DownloadGrid.SelectedItem);
    }
    private void SpeedLimit_Click(object sender, RoutedEventArgs e) => OpenSettings("Connection");
    private void CustomizeColumns_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu(); foreach (var column in DownloadGrid.Columns) { var item = new MenuItem { Header = column.Header?.ToString(), IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible }; item.Click += (_, _) => { column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed; settings.ColumnVisibility[column.Header?.ToString() ?? ""] = item.IsChecked; SaveHistory(); }; menu.Items.Add(item); } menu.PlacementTarget = DownloadGrid; menu.IsOpen = true;
    }
    private void Arrange_Click(object sender, RoutedEventArgs e)
    {
        string field = (string)((MenuItem)sender).Tag; VisibleDownloads.SortDescriptions.Clear(); VisibleDownloads.SortDescriptions.Add(new(field, System.ComponentModel.ListSortDirection.Ascending));
    }
    private void Toolbar_Click(object sender, RoutedEventArgs e) { settings.ShowToolbar = ((MenuItem)sender).IsChecked; ApplyViewSettings(); SaveHistory(); }
    private void Font_Click(object sender, RoutedEventArgs e) => OpenSettings("View");
    private void DropTarget_Click(object sender, RoutedEventArgs e)
    {
        if (dropWindow != null) { dropWindow.Activate(); return; }
        dropWindow = new Window { Title = "Flux drop target", Width = 240, Height = 110, Topmost = true, AllowDrop = true, Background = new SolidColorBrush(Color.FromRgb(27,27,27)), Foreground = Brushes.White, Content = new TextBlock { Text = "Drop a download link here", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        dropWindow.DragOver += (_, args) => { args.Effects = args.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None; args.Handled = true; };
        dropWindow.Drop += (_, args) => { if (args.Data.GetData(DataFormats.Text) is string text && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0) ConfirmUrl(uri); };
        dropWindow.Closed += (_, _) => { dropWindow = null; CheckBackgroundIdle(); }; dropWindow.Show();
    }
    private async void Grabber_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TextEntryDialog("Site Grabber", "Page URL to collect same-site file links (up to 20 pages, 500 files).") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!Uri.TryCreate(dialog.Value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) { MessageText.Text = "Enter a valid HTTP/HTTPS page address."; return; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); cancel.CancelAfter(TimeSpan.FromSeconds(40));
        MessageText.Text = "Collecting site links…";
        try { var links = await SiteGrabber.CollectAsync(uri, settings.CaptureExtensions, settings.ManualSession(uri), cancel.Token, engine.Options.Proxy); if (links.Count == 0) MessageText.Text = "No matching file links found."; else ShowBatch(string.Join(Environment.NewLine, links), uri.GetLeftPart(UriPartial.Authority)); }
        catch (Exception error) { MessageText.Text = "Site Grabber: " + error.Message; }
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        string topic = (sender as MenuItem)?.Tag as string ?? "Help";
        var text = topic switch
        {
            "Queues" => "Save files with Download later, or add a batch. Assign a queue from the right-click menu. Start/Stop queue controls its files. Configure daily HH:mm schedules in Scheduler. Flux must stay open for scheduled events.",
            "Grabber" => "Site Grabber collects links from up to 20 same-site HTML pages and 500 matching files. Review the batch before adding it. It does not run website scripts or extract protected streams.",
            "Tip" => "Keep full resume verification off for fast resume. Servers must support byte ranges. Download later stores a file without starting it.",
            "Updates" => "This local build has no update service configured. Build and install a newer release with scripts/Build-Release.ps1. Browser extension updates need a reload on the extensions page.",
            "Support" => "To report a problem, include the app version, site name, and exact error. Startup diagnostics: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftFetch", "app-startup.log"),
            "About" => "Flux\nLocal Windows download manager\nSegmented HTTP/HTTPS downloads, persistent resume and Chrome/Brave integration.\nNo registration or trial is required.",
            _ => "Add URL: Ctrl+L. Start confirms a download; Cancel creates no history entry. Pause keeps the saved bytes. Resume requests remaining byte ranges. Use Options for folders, connections, proxy, site logins and speed limits. F2 renames idle files; Ctrl+F searches; F3 finds the next matching row."
        };
        MessageBox.Show(this, text, "Flux — " + topic);
    }
    private void Share_Click(object sender, RoutedEventArgs e) { Clipboard.SetText("Flux — Windows download manager with pause/resume and Chrome/Brave integration."); MessageText.Text = "App description copied. Share it with your installer."; }
}

