using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwiftFetch;
using SwiftFetch.Core;
using SwiftFetch.Bridge;

internal static partial class Program
{
    // Load the real resources without starting the production app or leaving fixture windows alive.
    private sealed class SmokeApplication : Application, IDisposable
    {
        protected override void OnStartup(StartupEventArgs e) { }
        public void LoadStyles()
        {
            System.Xml.Linq.XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var document = System.Xml.Linq.XDocument.Load(Path.Combine(Environment.CurrentDirectory,"SwiftFetch","App.xaml"));
            var resources = document.Root!.Element(ui + "Application.Resources")!;
            var dictionary = new System.Xml.Linq.XElement(ui + "ResourceDictionary",
                new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x","http://schemas.microsoft.com/winfx/2006/xaml"),
                resources.Elements().Select(element => new System.Xml.Linq.XElement(element)));
            Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        }
        public void Dispose() => Shutdown();
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--icons") { GenerateApexIcons(); return; }
        using var app = new SmokeApplication();
        app.LoadStyles();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (args.FirstOrDefault() == "--rate-tests")
        {
            var job = new DownloadJob { Url = "https://example.invalid/file", Destination = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"rate-test.bin"), State = DownloadState.Downloading };
            var rateRow = new DownloadRow(job);
            rateRow.Update(new(100,1000,100,1));
            if (rateRow.SpeedText != "100 B/s" || rateRow.EtaText != "00:00:09") throw new Exception("Initial speed/ETA incorrect.");
            rateRow.Update(new(120,1000,1200,1));
            if (rateRow.SpeedText == "1.2 KB/s") throw new Exception("Display exposed the raw burst.");
            rateRow.Stop(); if (rateRow.SpeedText != "—" || rateRow.EtaText != "—") throw new Exception("Stopped speed/ETA retained.");
            rateRow.Update(new(200,1000,200,1)); if (rateRow.SpeedText != "200 B/s") throw new Exception("Resumed speed inherited old samples.");
            job.State = DownloadState.Paused; if (rateRow.SpeedText != "—" || rateRow.EtaText != "—") throw new Exception("Paused transfer still shows speed/ETA.");
            Console.WriteLine("PASS download row smooths bursts, resets on resume, and clears speed/ETA when paused");
            return;
        }
        if (args.FirstOrDefault() == "--performance-tests") { MeasureIdlePerformance(args.ElementAtOrDefault(1) ?? "baseline"); return; }
        if (args.FirstOrDefault() == "--caption-tests") { CheckCaptionTheme(); return; }
        if (args.FirstOrDefault() == "--video-tests") { CheckVideoHandoff(); return; }
        if (args.FirstOrDefault() == "--metadata-tests") { CheckMetadataDialogs(); return; }
        if (args.FirstOrDefault() == "--capture-tests") { CheckCaptureWorkflow(); return; }
        if (args.FirstOrDefault() == "--dialogs") { RenderDialogs(); return; }
        if (args.FirstOrDefault() == "--integration") { CheckIntegration(); CheckLibrary(); return; }
        if (args.FirstOrDefault() == "--options-tests") { CheckOptions(); return; }
        if (args.FirstOrDefault() == "--menu-tests") { CheckMenu(); return; }
        if (args.FirstOrDefault() == "--library-interaction-tests") { CheckLibraryInteractions(); return; }
        if (args.FirstOrDefault() == "--queue-tests") { CheckTrayAndQueues(); return; }
        if (args.FirstOrDefault() == "--window-tests") { CheckIndependentWindows(); return; }
        var window = new MainWindow(Path.Combine(Environment.CurrentDirectory, ".test-tmp", "preview"), Path.Combine(Environment.CurrentDirectory, "Downloads"));
        if (args.Contains("--light"))
        {
            window.Preferences.DarkMode = false;
            typeof(MainWindow).GetMethod("ApplyTheme", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null);
        }
        window.Downloads.Clear();
        var row = new DownloadRow(new DownloadJob
        {
            Destination = Path.Combine(Path.GetTempPath(), "Ubuntu-desktop.iso"),
            Status = "Downloading",
            Url = "https://releases.ubuntu.com/desktop.iso",
            TotalBytes = 3_000_000_000,
            Segments = [new(), new(), new(), new()]
        });
        window.Downloads.Add(row);
        row.Update(new(1_240_000_000, 3_000_000_000, 24_000_000, 4));
        string sampleFile = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "preview", "relinker-v0.1.1.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(sampleFile)!);
        File.WriteAllBytes(sampleFile, []);
        window.Downloads.Add(new(new() { Destination = sampleFile, Url = "https://github.com/releases/download/app.exe", Status = "Completed", TotalBytes = 4_194_304 }));
        window.Downloads.Add(new(new() { Destination = Path.Combine(Path.GetTempPath(), "Design-resources-2026.zip"), Url = "https://cdn.example.com/resources.zip", Status = "Paused", TotalBytes = 128_000_000 }));
        var paused = window.Downloads.Last(); paused.Update(new(43_520_000, 128_000_000, 0, 0));
        foreach (var name in new[] { "Project-assets.zip", "Annual-report.pdf", "Setup-x64.msi", "Reference-photos.zip", "Studio-recording.flac", "Presentation.pptx", "Icons-and-fonts.7z", "Release-notes.txt", "Desktop-wallpapers.zip", "Archive-backup.tar", "Product-guide.pdf", "Windows-tools.zip" })
        {
            string previewFile = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "preview", name);
            File.WriteAllBytes(previewFile, []);
            window.Downloads.Add(new(new() { Destination = previewFile, Url = "https://downloads.example.com/" + name, Status = "Completed", TotalBytes = 10_485_760 + window.Downloads.Count * 2_048_576 }));
        }
        // Use the real window's layout so star-sized columns match the desktop app.
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.Show();
        ((System.Windows.Controls.DataGrid)window.FindName("DownloadGrid")).SelectedItem = row;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1280, 780, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background ?? new SolidColorBrush(Color.FromRgb(16, 20, 29)), null, new Rect(0, 0, 1280, 780));
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None }, null, new Rect(0, 0, 1280, 780));
        }
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.GetFullPath(args.FirstOrDefault(value => !value.StartsWith("--")) ?? "ui-preview.png");
        using var output = File.Create(path);
        png.Save(output);
        Console.WriteLine("UI rendered successfully: " + path);
        // No dispatcher loop, so the production history-save timer never runs.
    }

    private static void CheckTrayAndQueues()
    {
        var frame = new DispatcherFrame(); Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            MainWindow? main = null;
            try
            {
                string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","queues-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                main = new MainWindow(directory,directory,new DownloadEngine(new QueueHandler())) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                main.Preferences.MaximumActiveDownloads = 1; main.Preferences.Queues[0].MaximumActive = 1; main.Preferences.QueueNewDownloads = true; main.Preferences.ShowDownloadCompleteDialog = false;
                main.EnableBackgroundTray(); Require(!main.IsVisible,"Background startup displayed the manager.");
                main.AddManualDownload("https://queue.test/one.zip"); main.AddManualDownload("https://queue.test/two.zip"); main.AddManualDownload("https://queue.test/three.zip");
                Require(main.Downloads.All(r => r.Job.Queue == "Default") && main.ActiveDownloadCount == 1 && main.Downloads.Count(r => r.Job.State == DownloadState.Queued) == 2,"Started downloads did not join and run in the main queue.");
                main.Show(); main.Close(); Require(!main.IsVisible && main.IsEnabled && main.ActiveDownloadCount == 1,"Closing the manager stopped downloads instead of hiding it.");
                main.ShowProgressDialog(main.Downloads[0]); main.ShowProgressDialog(main.Downloads[1]);
                var progress = Application.Current.Windows.OfType<DownloadProgressDialog>().ToArray(); Require(progress.Length == 2,"Multiple progress windows missing.");
                foreach (var dialog in progress) { dialog.Opacity = 0; dialog.HideToTray(); }
                var iconType = typeof(MainWindow).Assembly.GetType("SwiftFetch.TrayIcon")!;
                Require((int)iconType.GetProperty("ActiveIconCount",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)! == 1,"Progress windows created extra tray icons.");
                SendMessage(new System.Windows.Interop.WindowInteropHelper(main).Handle,0x8001,new IntPtr(1),new IntPtr(0x205));
                var menu = (System.Windows.Controls.ContextMenu)typeof(MainWindow).GetField("trayMenu",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(main)!;
                Require(menu.IsOpen && menu.Items.OfType<System.Windows.Controls.MenuItem>().Count(item => item.Header.ToString()!.Contains(".zip")) == 3,"Grouped tray menu did not show every queued download.");
                menu.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => (string)item.Header == "Show all progress windows").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent)); menu.IsOpen = false;
                Require(progress.All(p => p.IsVisible && !p.IsHiddenInTray),"Grouped restore failed.");
                main.Show(); var grid = (System.Windows.Controls.DataGrid)main.FindName("DownloadGrid"); grid.SelectedItems.Clear(); foreach (var row in main.Downloads) grid.SelectedItems.Add(row);
                Require(grid.SelectionMode == System.Windows.Controls.DataGridSelectionMode.Extended && grid.SelectedItems.Count == 3,"Multiple selection failed.");
                typeof(MainWindow).GetMethod("Pause_Click",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(main,[main,new RoutedEventArgs()]);
                for (int n = 0; main.ActiveDownloadCount > 0 && n < 100; n++) await Task.Delay(10);
                Require(main.Downloads.All(r => r.Job.State == DownloadState.Paused),"Bulk pause left a selected file running.");
                typeof(MainWindow).GetMethod("Scheduler_Click",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(main,[main,new RoutedEventArgs()]);
                var planner = Application.Current.Windows.OfType<QueueSchedulerWindow>().Single(); planner.Opacity = 0; planner.ShowInTaskbar = false; planner.UpdateLayout(); Require(NativeEnabled(main) && planner.Owner == null,"Queue scheduler blocked the manager.");
                CaptureVisibleContent(planner,Path.Combine(directory,"scheduler.png"));
                var plannerTabs = ((System.Windows.Controls.Grid)((System.Windows.Controls.DockPanel)planner.Content).Children.Cast<UIElement>().OfType<System.Windows.Controls.Grid>().Single()).Children.OfType<System.Windows.Controls.TabControl>().Single();
                for (int index = 1; index < 3; index++) { plannerTabs.SelectedIndex = index; planner.UpdateLayout(); CaptureVisibleContent(planner,Path.Combine(directory,$"scheduler-{index}.png")); }
                plannerTabs.SelectedIndex = 0;
                var flags = System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                ((System.Windows.Controls.TextBox)typeof(QueueSchedulerWindow).GetField("concurrent",flags)!.GetValue(planner)!).Text = "2";
                ((System.Windows.Controls.TextBox)typeof(QueueSchedulerWindow).GetField("startTime",flags)!.GetValue(planner)!).Text = "07:30";
                ((System.Windows.Controls.CheckBox)typeof(QueueSchedulerWindow).GetField("daily",flags)!.GetValue(planner)!).IsChecked = true;
                typeof(QueueSchedulerWindow).GetMethod("Apply",flags)!.Invoke(planner,[]);
                Require(main.Preferences.Queues[0].MaximumActive == 2 && main.Preferences.Queues[0].StartTime == "07:30","Scheduler did not apply its controls."); planner.Close();
                var mainQueue = main.Preferences.Queues[0]; mainQueue.Enabled = true; mainQueue.Daily = false; mainQueue.ScheduledDate = DateTime.Today.AddDays(1); mainQueue.StartTime = DateTime.Now.ToString("HH:mm");
                typeof(MainWindow).GetMethod("CheckQueueSchedules",flags)!.Invoke(main,[]);
                Require(main.ActiveDownloadCount == 0,"One-time schedule started on the wrong date.");
                mainQueue.ScheduledDate = DateTime.Today;
                typeof(MainWindow).GetMethod("CheckQueueSchedules",flags)!.Invoke(main,[]);
                Require(main.ActiveDownloadCount == 1,"One-time schedule did not start on its configured date.");
                typeof(MainWindow).GetMethod("StopQueue",flags)!.Invoke(main,["Default"]);
                for (int n = 0; main.ActiveDownloadCount > 0 && n < 100; n++) await Task.Delay(10);
                Require(main.Downloads.All(r => r.Job.State == DownloadState.Paused),"Stopping the scheduled queue left files running.");
                typeof(MainWindow).GetMethod("Resume_Click",flags)!.Invoke(main,[main,new RoutedEventArgs()]);
                Require(main.ActiveDownloadCount == 1 && main.Downloads.Count(r => r.Job.State == DownloadState.Queued) == 2,"Bulk resume did not retain the global limit.");
                var syncQueue = main.Preferences.Queues.First(q => q.Synchronization); syncQueue.Enabled = true;
                string previousFile = Path.Combine(directory,"sync.zip"); File.WriteAllBytes(previousFile,[1,2,3]);
                var syncRow = new DownloadRow(new() { Queue = syncQueue.Name, Url = "https://queue.test/sync.zip", Destination = previousFile, State = DownloadState.Completed, TotalBytes = 3, ETag = "\"v1\"" }); main.Downloads.Add(syncRow);
                typeof(MainWindow).GetMethod("CheckSynchronizationQueues",flags)!.Invoke(main,[]);
                for (int n = 0; syncRow.Job.State == DownloadState.Completed && n < 100; n++) await Task.Delay(10);
                Require(syncRow.Job.State == DownloadState.Queued && syncRow.Job.Destination != previousFile && File.ReadAllBytes(previousFile).SequenceEqual(new byte[] {1,2,3}),"Synchronization failed to queue the changed version while preserving the old file.");
                CloseAndDrain(main); main = null;
                Require((int)iconType.GetProperty("ActiveIconCount",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)! == 0,"Exit left an orphan tray icon.");
                Console.WriteLine("PASS hidden tray startup, close-to-tray with active transfer, one icon for multiple progress windows, real tray menu restore, bulk pause/resume, main queue assignment, independent scheduler and saved controls. Preview: " + directory);
            }
            catch (Exception error) { failure = error; }
            finally { if (main != null) CloseAndDrain(main); frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame); if (failure != null) throw new Exception("Queue tests failed",failure);
    }
    private sealed class QueueHandler : System.Net.Http.HttpMessageHandler
    {
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)
        {
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (range?.From == 0 && range.To == 0)
            {
                var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = new System.Net.Http.ByteArrayContent([42]) };
                response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0,0,100000); return response;
            }
            await Task.Delay(Timeout.Infinite,token); throw new OperationCanceledException(token);
        }
    }

    private static void CheckIndependentWindows()
    {
        var frame = new DispatcherFrame(); Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","windows-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                var engine = new DownloadEngine(new MetadataHandler("size"));
                var main = new MainWindow(directory,directory,engine) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false }; main.Show();
                var session = new DownloadSession { Origin = "https://example.test/", UserAgent = "TestBrowser/1", Cookies = [new() { Name = "sid", Value = "test", Domain = "example.test" }] };
                var request = new BridgeRequest("download","https://example.test/file.zip","file.zip",Guid.NewGuid().ToString(),session);
                main.Activate();
                bool foregroundAllowed = GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(main).Handle;
                main.Hide();
                Require(main.AcceptBrowserRequest(request).Ok,"Live confirmation was rejected.");
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => {},DispatcherPriority.ContextIdle);
                var info = Application.Current.Windows.OfType<DownloadInfoDialog>().Single(); info.Opacity = 0;
                Require(!main.IsVisible && info.IsVisible,"Browser confirmation did not appear while the manager stayed hidden.");
                if (foregroundAllowed)
                    Require(GetForegroundWindow() == new System.Windows.Interop.WindowInteropHelper(info).Handle,"Browser confirmation did not inherit the foreground permission.");
                else Console.WriteLine("SKIP foreground assertion: Windows did not grant the isolated test foreground rights.");
                Require(info.Topmost || info.IsActive,"Start dialogue was not raised on arrival.");
                await Task.Delay(900);
                Require(!info.Topmost,"Start dialogue stayed permanently above other applications.");
                main.Show();
                Require(main.IsEnabled && NativeEnabled(main) && info.Owner == null && main.Downloads.Count == 0,"Confirmation blocked the manager or created history before Start.");
                var other = new AddUrlDialog() { Opacity = 0, ShowActivated = false }; var otherTask = IndependentDialogs.ShowAsync(other);
                Require(NativeEnabled(main) && NativeEnabled(info),"A second independent window blocked another window."); other.Close(); Require(!await otherTask,"Closing Add URL accepted a download.");
                ((System.Windows.Controls.TextBox)info.FindName("SaveAsBox")).Text = Path.Combine(directory,"custom filename.zip");
                ((System.Windows.Controls.TextBox)info.FindName("DescriptionBox")).Text = "Design resources";
                typeof(DownloadInfoDialog).GetMethod("DownloadLater_Click",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(info,[info,new RoutedEventArgs()]);
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => {},DispatcherPriority.ContextIdle);
                Require(main.PendingDownloadCount == 0 && main.Downloads.Count == 1 && main.Downloads[0].Job.NameIsCustom && main.Downloads[0].Job.Description == "Design resources","Modeless confirmation lost its filename or description.");
                var row = main.Downloads[0]; row.Job.State = DownloadState.Paused; row.Job.SpeedLimitBytes = 512000;
                row.Job.StartedAt = DateTimeOffset.UtcNow; row.Job.RangeSupported = true;
                long total = row.Job.TotalBytes!.Value, chunk = total / 16;
                row.Job.Segments = Enumerable.Range(0,16).Select(i => new DownloadSegment { Start = i * chunk,End = i == 15 ? total - 1 : (i + 1) * chunk - 1,WrittenBytes = (long)(chunk * .34),StatusText = "Idle" }).ToList();
                row.Update(new((long)(total * .34),total,0,0));
                var progress = new DownloadProgressDialog(row,engine,_ => {},_ => {},_ => {}) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false }; progress.Show();
                Require(progress.FindName("TrayButton") != null && main.IsEnabled,"Progress caption button is missing or the manager is blocked.");
                Require(row.Job.SpeedLimitBytes == 512000 && ((System.Windows.Controls.CheckBox)progress.FindName("UseSpeedLimiterCheck")).IsChecked == true,"Opening progress lost its saved speed limit.");
                foreach (int tab in new[] {0,1,2})
                {
                    ((System.Windows.Controls.TabControl)progress.FindName("DetailTabs")).SelectedIndex = tab; progress.UpdateLayout();
                    await Dispatcher.CurrentDispatcher.InvokeAsync(() => {},DispatcherPriority.ContextIdle); progress.UpdateLayout();
                    var tabs = (FrameworkElement)progress.FindName("DetailTabs"); Require(tabs.ActualHeight > 100,"Progress tab collapsed.");
                    string path = Path.Combine(directory,$"progress-{tab}.png"); CaptureVisibleContent(progress,path);
                }
                progress.GroupInTray = main.EnableBackgroundTray;
                progress.HideToTray(); Require(progress.IsHiddenInTray && !progress.IsVisible && row.Job.State == DownloadState.Paused,"Tray hiding changed the transfer state.");
                progress.RestoreFromTray();
                Require(progress.IsVisible && !progress.IsHiddenInTray,"Clicking the tray icon did not restore progress."); progress.Close();
                main.Hide();
                var complete = new DownloadCompleteDialog(row.Job) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                var completeTask = IndependentDialogs.ShowAsync(complete, bringToFront: true); complete.UpdateLayout();
                Require(complete.IsVisible && !main.IsVisible && complete.ShowInTaskbar && complete.ShowActivated,"Finish dialogue failed to appear independently while the manager stayed hidden.");
                Require(complete.Topmost || complete.IsActive,"Finish dialogue was not raised on arrival.");
                await Task.Delay(900);
                Require(!complete.Topmost,"Finish dialogue stayed permanently above other applications.");
                Require(NativeEnabled(main) && complete.Owner == null,"Finish window blocked the manager."); CaptureVisibleContent(complete,Path.Combine(directory,"complete.png")); complete.Close();
                Require(!await completeTask,"Closing a finish notification accepted a download."); main.Show();
                var freshInfo = new DownloadInfoDialog(new Uri(request.Url!),main.Preferences,session,"file.zip",engine) { Opacity = 0,ShowActivated = false }; var cancelled = IndependentDialogs.ShowAsync(freshInfo); freshInfo.UpdateLayout(); CaptureVisibleContent(freshInfo,Path.Combine(directory,"start.png")); freshInfo.Close(); Require(!await cancelled,"Closing start window confirmed a download.");
                var completionRow = new DownloadRow(new() { Url = request.Url!,Destination = Path.Combine(directory,"done.zip"),State = DownloadState.Completed,TotalBytes = 1 });
                bool requested = false;
                var actionWindow = new DownloadProgressDialog(completionRow,engine,_ => {},_ => {},_ => {}) { Opacity = 0, ShowActivated = false };
                ((System.Windows.Controls.CheckBox)actionWindow.FindName("ShowCompleteDialogCheck")).IsChecked = false;
                ((System.Windows.Controls.CheckBox)actionWindow.FindName("TurnOffCheck")).IsChecked = true;
                ((System.Windows.Controls.ComboBox)actionWindow.FindName("ShutdownActionBox")).SelectedIndex = 1;
                actionWindow.CompletionActionRequested += (action,exit,force) => requested = action == QueueCompletionAction.Sleep;
                actionWindow.Show(); Require(requested,"Selected completion action was not dispatched."); // No system power action is executed in this test.
                main.ShowProgressDialog(row);
                var cached = Application.Current.Windows.OfType<DownloadProgressDialog>().Single(); cached.Opacity = 0;
                bool idle = false; main.BackgroundIdle += () => idle = true; main.Hide(); cached.HideToTray();
                typeof(MainWindow).GetMethod("CheckBackgroundIdle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(main,[]);
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => {},DispatcherPriority.ContextIdle);
                Require(!idle && cached.IsHiddenInTray,"Hidden paused progress was treated as an idle application.");
                main.ShowProgressDialog(row); Require(cached.IsVisible && !cached.IsHiddenInTray && Application.Current.Windows.OfType<DownloadProgressDialog>().Count() == 1,"Reopening progress created another window instead of restoring it."); cached.Close();
                Require(main.AcceptBrowserRequest(new("quit")).Ok,"Hidden instance rejected graceful quit.");
                CloseAndDrain(main); Console.WriteLine("PASS independent start/progress/finish, concurrent windows, tray hide/restore, hidden-instance lifetime, custom names/descriptions and completion dispatch. Previews: " + directory);
            }
            catch(Exception error) { failure = error; }
            finally { frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame); if (failure != null) throw new Exception("Window tests failed",failure);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,uint message,IntPtr w,IntPtr l);
    private static bool NativeEnabled(Window window) => IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle);
    private static void CaptureVisibleContent(Window window,string path)
    {
        window.UpdateLayout(); var content = (FrameworkElement)window.Content;
        int width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right), height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var draw = visual.RenderOpen()) { draw.DrawRectangle(window.Background,null,new Rect(0,0,width,height)); draw.DrawRectangle(new VisualBrush(content) { ViewboxUnits = BrushMappingMode.Absolute,Viewbox = new Rect(0,0,width,height),Stretch = Stretch.None,AlignmentX = AlignmentX.Left,AlignmentY = AlignmentY.Top },null,new Rect(0,0,width,height)); } bitmap.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
    private static void CheckMenu()
    {
        string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","menu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var window = new MainWindow(directory,directory) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 }; window.Show(); window.UpdateLayout();
        var grid = (System.Windows.Controls.DataGrid)window.FindName("DownloadGrid");
        var paused = new DownloadRow(new() { Url = "https://example.invalid/file.zip", Destination = Path.Combine(directory,"file.zip"), State = DownloadState.Paused }); window.Downloads.Add(paused); grid.SelectedItem = paused;
        var menu = grid.ContextMenu; menu.PlacementTarget = grid; menu.Opacity = 0; menu.IsOpen = true; Dispatcher.CurrentDispatcher.Invoke(() => {},DispatcherPriority.ContextIdle); menu.UpdateLayout();
        var resume = menu.Items.OfType<System.Windows.Controls.MenuItem>().First(); var stop = menu.Items.OfType<System.Windows.Controls.MenuItem>().Skip(1).First();
        Require(resume.IsEnabled && !stop.IsEnabled, "Menu enablement did not follow the selected row.");
        menu.Opacity = 1; menu.Measure(new Size(400,600)); menu.Arrange(new Rect(menu.DesiredSize)); menu.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32); bitmap.Render(menu);
        string path = Path.Combine(directory,"context-menu.png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        menu.Opacity = 0; menu.IsOpen = false; grid.SelectedItem = null; menu.IsOpen = true; Dispatcher.CurrentDispatcher.Invoke(() => {},DispatcherPriority.ContextIdle); Require(!menu.IsOpen,"Menu opened without a selected file.");
        CloseAndDrain(window); Console.WriteLine("PASS context menu selected-row actions, disabled state and empty-area protection. Preview: " + path);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static void CheckCaptionTheme()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "caption-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        new SqliteDownloadStateStore(Path.Combine(directory,"history.db")).Save(new() { Settings = new() { DarkMode = true, ShowTrayIcon = false } });
        var main = new MainWindow(directory, directory) { ShowActivated = false, ShowInTaskbar = false };
        main.Show();
        var handle = new System.Windows.Interop.WindowInteropHelper(main).Handle;
        void Check(int attribute, int expected)
        {
            int result = DwmGetWindowAttribute(handle, attribute, out int actual, sizeof(int));
            Require(result == 0 && actual == expected,
                $"Caption attribute {attribute}: expected {expected:X}, got {actual:X}, HRESULT {result:X}.");
        }
        Check(20, 1);
        Require(main.WindowStyle == WindowStyle.SingleBorderWindow && main.ResizeMode == ResizeMode.CanResize, "Native caption controls were replaced.");
        main.Preferences.DarkMode = false;
        typeof(MainWindow).GetMethod("ApplyTheme", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(main, null);
        Check(20, 0);
        main.Preferences.DarkMode = true;
        typeof(MainWindow).GetMethod("ApplyTheme", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(main, null);
        Check(20, 1);
        CloseAndDrain(main);
        Console.WriteLine("PASS native dark caption on first show, light/dark switching, and native window controls.");
    }

    private static void CheckVideoHandoff()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","video-ui-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new SqliteDownloadStateStore(Path.Combine(directory,"history.db"));
        store.Save(new() { Settings = new() { DownloadDirectory=directory, ShowTrayIcon=false } });
        var main = new MainWindow(directory,directory);
        var selection = new VideoSelection { FormatId="137+140",Extension="mp4",Label="MP4 · 1080p · video + audio",TotalBytes=120000,Approximate=true };
        var request = new BridgeRequest("download","https://www.youtube.com/watch?v=jNQXAC9IVRw","Example (1080p).mp4",Guid.NewGuid().ToString(),Video:selection);
        main.ConfirmationProvider = pending =>
        {
            Require(main.Downloads.Count==0 && pending.Video?.FormatId==selection.FormatId,"Video was lost or inserted before Start.");
            return new(new Uri(pending.Url),Path.Combine(directory,pending.FileName!),"Video",false,pending.Session,TotalBytes:pending.Video!.TotalBytes);
        };
        Require(main.AcceptBrowserRequest(request).Ok && main.Downloads.Count==0,"Video handoff created a premature row.");
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ContextIdle);
        Require(main.Downloads.Single().Job.Video?.FormatId==selection.FormatId && main.Downloads[0].Job.State==DownloadState.Ready,"Deferred video plan was not retained.");
        Require(main.Downloads[0].TotalSizeText.StartsWith("≈"),"Estimated video size was presented as exact.");
        var dialog = new DownloadInfoDialog(new Uri(request.Url!),main.Preferences,suggestedName:request.FileName,video:selection);
        Require(((System.Windows.Controls.TextBlock)dialog.FindName("FileSizeText")).Text.StartsWith("≈"),"Video confirmation did not use video metadata.");
        dialog.Close();
        CloseAndDrain(main);
        Require(store.Load().Downloads.Single().Video?.FormatId==selection.FormatId,"Video plan was not persisted.");
        Console.WriteLine("PASS video confirmation, no premature library entry, Download Later, estimated size and persisted format plan.");
    }

    private static void CheckOptions()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "options-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var preferences = new DownloadSettings { DownloadDirectory = directory };
        foreach (var title in new[] { "General", "File types", "Save to", "Downloads", "Connection", "Proxy / SOCKS", "Site logins", "Dial up / VPN", "Sounds", "Scheduler", "View" })
        {
            var options = new SettingsWindow(preferences,title);
            var tabs = (System.Windows.Controls.TabControl)options.FindName("Tabs");
            Require(tabs.Items.Count == 11 && (string)((System.Windows.Controls.TabItem)tabs.SelectedItem).Header == title, "Missing options page: " + title);
            string path = Path.Combine(directory,title.Replace('/','-') + ".png"); RenderWindow(options,720,680,path);
        }
        var save = new SettingsWindow(preferences,"Downloads") { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        save.Loaded += (_, _) => { ((System.Windows.Controls.CheckBox)save.FindName("VerifyResumeBox")).IsChecked = true; typeof(SettingsWindow).GetMethod("Save_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(save,[save,new RoutedEventArgs()]); };
        Require(save.ShowDialog() == true && save.Result.VerifySavedBytesWithoutValidator, "Options save failed.");
        preferences.DarkMode = false; preferences.Language = "fr"; preferences.ShowToolbar = false; preferences.ShowCategories = false;
        new SqliteDownloadStateStore(Path.Combine(directory,"history.db")).Save(new() { Settings = preferences });
        var main = new MainWindow(directory,null); Require(main.FindName("AddPanel") == null && main.FindName("UrlBox") == null, "Inline add form is still present.");
        main.ShowActivated = false; main.ShowInTaskbar = false; main.Opacity = 0; main.Show(); main.UpdateLayout();
        Require(((FrameworkElement)main.FindName("MainToolbar")).Visibility == Visibility.Collapsed, "Saved toolbar preference was ignored.");
        Require(((System.Windows.Controls.RowDefinition)main.FindName("ToolbarRow")).Height.Value == 0 && ((System.Windows.Controls.ColumnDefinition)main.FindName("CategoryColumn")).ActualWidth == 0, "Hidden panels left empty space.");
        Require(((System.Windows.Controls.Menu)main.FindName("MainMenu")).Items.OfType<System.Windows.Controls.MenuItem>().First().Header as string == "_Tâches", "Main menu was not translated.");
        var brush = (SolidColorBrush)Application.Current.Resources["ThemeBackground1B1B1B"]; Require(brush.Color.R > 200, "Light theme was not applied.");
        Require(((System.Windows.Controls.ContextMenu)((System.Windows.Controls.DataGrid)main.FindName("DownloadGrid")).ContextMenu).Items.OfType<System.Windows.Controls.MenuItem>().First().Header as string == "Reprendre", "French menu was not applied.");
        RenderWindow(main,1280,780,Path.Combine(directory,"main-light-fr.png"));
        Console.WriteLine("PASS 11 settings tabs, options save, no inline form, persistent view preferences, light theme and French menus. Previews: " + directory);
    }

    private static void CheckIntegration()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "ui-bridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new MainWindow(directory, directory);
        var request = new BridgeRequest("download", "https://example.invalid/file", "../CON.zip", Guid.NewGuid().ToString());
        var result = window.AcceptBrowserRequest(request, false);
        Require(result.Ok && window.Downloads.Count == 1, "Browser job was not accepted.");
        Require(window.Downloads[0].FileName == "_CON.zip", "Browser filename was not sanitized.");
        Require(window.Downloads[0].Job.Destination.StartsWith(directory), "Browser filename escaped its folder.");
        var duplicate = window.AcceptBrowserRequest(request, false);
        Require(duplicate.JobId == result.JobId && window.Downloads.Count == 1, "Duplicate browser request created another job.");
        Require(!window.AcceptBrowserRequest(request with { Url = "file:///C:/x" }, false).Ok, "Invalid URL was accepted.");
        Require(File.Exists(Path.Combine(directory, "history.db")), "Accepted job was not persisted.");
        CloseAndDrain(window);
        var store = new SqliteDownloadStateStore(Path.Combine(directory, "history.db"));
        var snapshot = store.Load();
        Require(!snapshot.Settings.VerifySavedBytesWithoutValidator, "Fast resume must be the default.");
        snapshot.Settings.VerifySavedBytesWithoutValidator = true;
        snapshot.Settings.MaximumActiveDownloads = 2;
        snapshot.Settings.MaximumRetries = 1;
        snapshot.Settings.DefaultConnections = 8;
        snapshot.Settings.DownloadDirectory = Path.Combine(directory, "chosen-folder");
        store.Save(snapshot);
        var reopened = new MainWindow(directory, null);
        var resumeSettings = new SettingsWindow(store.Load().Settings);
        Require(((System.Windows.Controls.CheckBox)resumeSettings.FindName("VerifyResumeBox")).IsChecked == true, "Full-check preference was not restored in Settings.");
        resumeSettings.Close();
        Require(reopened.AcceptBrowserRequest(request, false).JobId == result.JobId && reopened.Downloads.Count == 1, "Request deduplication failed after restart.");
        Require(reopened.Preferences.DownloadDirectory == snapshot.Settings.DownloadDirectory, "Folder preference was not restored.");
        Require(reopened.Preferences.DefaultConnections == 8, "Connection preference was not restored.");
        for (int i = 0; i < 3; i++)
            Require(reopened.AcceptBrowserRequest(new("download", $"https://example.invalid/queue-{i}.zip", RequestId: Guid.NewGuid().ToString()), false).Ok, "Browser queue handoff was rejected.");
        Require(reopened.ActiveDownloadCount == 2 && reopened.Downloads.Count(row => row.Job.State == DownloadState.Queued) == 1, "App did not enforce its saved concurrency limit.");
        reopened.AddManualDownload("https://example.invalid/later.zip", startNow: false);
        Require(reopened.Downloads.Count == 5 && reopened.Downloads.Last().Job.State == DownloadState.Ready, "Download later did not save an idle task.");
        reopened.AddManualDownload("https://example.invalid/hash.zip", startNow: false, expectedSha256: "bad-checksum");
        Require(reopened.Downloads.Count == 5, "Invalid checksum created a task.");
        CloseAndDrain(reopened);
        var saved = store.Load();
        Require(saved.Downloads.Count == 5 && saved.Downloads.Count(job => job.State == DownloadState.Ready) == 1 && saved.Downloads.Count(job => job.State == DownloadState.Paused) == 4, "Shutdown lost queue or deferred tasks.");
        Directory.Delete(directory, true);
        Console.WriteLine("PASS app handoff, filenames, restart deduplication, saved preferences, queue limit, download later, checksum input, and shutdown recovery.");
    }

    private static void CloseAndDrain(MainWindow window)
    {
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        window.ExitApplication();
        if (frame.Continue) Dispatcher.PushFrame(frame);
    }
    private static void CheckLibrary()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var window = new MainWindow(directory, directory) { ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        var archive = new DownloadRow(new() { Url = "https://example.invalid/assets.zip", Destination = Path.Combine(directory, "assets.zip"), State = DownloadState.Paused, TotalBytes = 400, Segments = [new() { Start = 0, End = 399, CommittedBytes = 100 }] });
        File.WriteAllBytes(archive.Job.PartPath(0), new byte[100]); archive.Refresh();
        var document = new DownloadRow(new() { Url = "https://example.invalid/report.pdf", Destination = Path.Combine(directory, "report.pdf"), State = DownloadState.Completed, TotalBytes = 1 });
        File.WriteAllBytes(document.Job.Destination, [42]);
        var music = new DownloadRow(new() { Url = "https://example.invalid/music.mp3", Destination = Path.Combine(directory, "music.mp3"), State = DownloadState.Ready });
        var active = new DownloadRow(new() { Url = "https://example.invalid/app.exe", Destination = Path.Combine(directory, "app.exe"), State = DownloadState.Downloading });
        foreach (var entry in new[] { archive, document, music, active }) window.Downloads.Add(entry);
        window.Show();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var tree = (System.Windows.Controls.TreeView)window.FindName("CategoryTree");
        var all = (System.Windows.Controls.TreeViewItem)window.FindName("AllCategory");
        var compressed = all.Items.Cast<System.Windows.Controls.TreeViewItem>().Single(item => (string)item.Tag == "category:Compressed");
        compressed.IsSelected = true;
        Require(window.VisibleDownloads.Cast<DownloadRow>().SequenceEqual([archive]), "Category filter did not select archives.");
        var search = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
        search.Text = "report";
        Require(window.VisibleDownloads.IsEmpty, "Search did not combine with category selection.");
        search.Clear(); all.IsSelected = true;
        Require(window.VisibleDownloads.Cast<DownloadRow>().Count() == 4, "All-downloads filter did not reset.");
        foreach (var pair in new[] { ("paused", archive), ("completed", document) })
        {
            tree.Items.Cast<System.Windows.Controls.TreeViewItem>().Single(item => (string)item.Tag == pair.Item1).IsSelected = true;
            Require(window.VisibleDownloads.Cast<DownloadRow>().SequenceEqual([pair.Item2]), "State filter selected wrong jobs: " + pair.Item1);
        }
        all.IsSelected = true;
        var grid = (System.Windows.Controls.DataGrid)window.FindName("DownloadGrid");
        Require(grid.RowHeight == 20 && grid.Columns.Count == 9 && grid.CanUserReorderColumns, "Compact table layout was not applied.");
        Require(window.FindName("AddUrlButton") is System.Windows.Controls.Button, "Add URL toolbar action is missing.");
        Require(!window.RemoveFromHistory([active]) && window.Downloads.Count == 4, "An active task was removed.");
        Require(window.RemoveFromHistory([document, archive]), "Paused/completed history could not be removed.");
        Require(File.Exists(document.Job.Destination) && File.Exists(archive.Job.PartPath(0)), "History removal deleted files on disk.");
        var store = new SqliteDownloadStateStore(Path.Combine(directory, "history.db"));
        Require(store.Load().Downloads.Count == 2, "History removal was not persisted.");
        CloseAndDrain(window);
        Directory.Delete(directory, true);
        Console.WriteLine("PASS compact table, toolbar, categories, combined search, state filters, active-task protection and history-only removal.");
    }
    private static void Require(bool valid, string error) { if (!valid) throw new Exception(error); }

    private static void CheckCaptureWorkflow()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "capture-" + Guid.NewGuid().ToString("N"));
        var window = new MainWindow(directory, directory);
        var store = new SqliteDownloadStateStore(Path.Combine(directory, "history.db"));
        int dialogs = 0;
        window.ConfirmationProvider = pending =>
        {
            dialogs++;
            Require(!window.IsVisible && window.Downloads.Count == 0, "Opening a confirmation revealed the manager or inserted history.");
            Require(store.Load().Downloads.Count == 0 && store.Load().PendingDownloads.Count == 1, "Pending handoff was not saved separately.");
            return null;
        };
        var request = new BridgeRequest("download", "https://example.invalid/file.zip", "file.zip", Guid.NewGuid().ToString());
        Require(window.AcceptBrowserRequest(request).Ok && window.PendingDownloadCount == 1 && window.Downloads.Count == 0, "Capture inserted a download before Start.");
        Require(window.AcceptBrowserRequest(request).Ok, "Repeated pending request was rejected.");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Require(dialogs == 1 && window.PendingDownloadCount == 0 && window.Downloads.Count == 0 && store.Load().Downloads.Count == 0, "Cancel or repeated capture created a placeholder.");
        window.ConfirmationProvider = pending => new(new Uri(pending.Url), Path.Combine(directory, "file.zip"), "Compressed", false, pending.Session);
        Require(window.AcceptBrowserRequest(request with { RequestId = Guid.NewGuid().ToString() }).Ok, "Download Later capture failed.");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Require(!window.IsVisible && window.Downloads.Count == 1 && window.Downloads[0].Job.State == DownloadState.Ready && store.Load().PendingDownloads.Count == 0, "Explicit Download Later was not saved without showing the manager.");
        var recovery = request with { RequestId = Guid.NewGuid().ToString() };
        Require(window.AcceptBrowserRequest(recovery).Ok, "Recovery capture failed.");
        CloseAndDrain(window); // Close before its queued confirmation is presented.
        Require(store.Load().PendingDownloads.Count == 1, "Closing the app lost an unconfirmed browser request.");
        var reopened = new MainWindow(directory, directory);
        reopened.ConfirmationProvider = _ => null;
        reopened.RestorePendingConfirmations();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Require(!reopened.IsVisible && reopened.PendingDownloadCount == 0 && reopened.Downloads.Count == 1, "Recovery displayed unstarted downloads in history.");
        reopened.ConfirmationProvider = pending => new(new Uri(pending.Url), Path.Combine(directory, "started.zip"), "Compressed", true, pending.Session);
        Require(reopened.AcceptBrowserRequest(request with { RequestId = Guid.NewGuid().ToString() }).Ok, "Start capture failed.");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        Require(!reopened.IsVisible && reopened.Downloads.Count == 2 && reopened.PendingDownloadCount == 0, "Start did not create exactly one history entry while keeping the manager hidden.");
        CloseAndDrain(reopened);
        var saved = store.Load();
        saved.Downloads.Add(new() { Url = request.Url!, Destination = Path.Combine(directory, "old-placeholder.zip"), BrowserRequestId = Guid.NewGuid().ToString(), State = DownloadState.Cancelled });
        saved.Downloads.Add(new() { Url = request.Url!, Destination = Path.Combine(directory, "really-started.zip"), BrowserRequestId = Guid.NewGuid().ToString(), State = DownloadState.Cancelled, StartedAt = DateTimeOffset.UtcNow });
        store.Save(saved);
        var migrated = new MainWindow(directory, directory);
        Require(!migrated.Downloads.Any(row => row.FileName == "old-placeholder.zip") && migrated.Downloads.Any(row => row.FileName == "really-started.zip"), "Legacy cleanup hid a real download or retained an unstarted placeholder.");
        CloseAndDrain(migrated);
        Console.WriteLine("PASS hidden manager, no history before Start, Cancel, pending deduplication, Download Later, recovery and Start.");
    }

    private static void CheckMetadataDialogs()
    {
        var frame = new DispatcherFrame();
        Exception? error = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                string directory = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "metadata-" + Guid.NewGuid().ToString("N"));
                foreach (string scenario in new[] { "size", "unknown", "html", "forbidden" })
                {
                    using var engine = new DownloadEngine(new MetadataHandler(scenario));
                    var settings = new DownloadSettings { DownloadDirectory = directory };
                    var session = new DownloadSession { Origin = "https://example.test", UserAgent = "TestBrowser/1", Cookies = [new() { Name = "sid", Value = "test", Domain = "example.test" }] };
                    var dialog = new DownloadInfoDialog(new Uri("https://example.test/file.zip"), settings, session, "file.zip", engine);
                    await dialog.ProbeMetadataAsync();
                    var text = ((System.Windows.Controls.TextBlock)dialog.FindName("FileSizeText")).Text;
                    if (scenario == "size") Require(dialog.ResolvedTotalBytes == 1_500_000_000 && text.Contains("GB"), "Start dialog displayed the one-byte probe size.");
                    else Require(dialog.ResolvedTotalBytes == null && text == "Unknown", "Start dialog displayed an error page or range-piece size: " + scenario);
                    dialog.Close();
                    Console.WriteLine("PASS start-dialog metadata: " + scenario);
                }
            }
            catch (Exception ex) { error = ex; }
            finally { frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame);
        if (error != null) throw new Exception("Metadata dialog checks failed.", error);
    }

    private sealed class MetadataHandler(string scenario) : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            Require(request.Headers.UserAgent.ToString() == "TestBrowser/1" && request.Headers.GetValues("Cookie").Single() == "sid=test", "Metadata probe lost the browser session.");
            var status = scenario == "forbidden" ? System.Net.HttpStatusCode.Forbidden : scenario == "html" ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.PartialContent;
            var response = new System.Net.Http.HttpResponseMessage(status) { Content = new System.Net.Http.ByteArrayContent([42]) };
            if (scenario is "size" or "unknown") response.Content.Headers.ContentRange = scenario == "size" ? new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0, 1_500_000_000) : new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0);
            if (scenario is "html" or "forbidden") response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        }
    }

    private static void GenerateApexIcons()
    {
        string root = Environment.CurrentDirectory;
        var p256 = RenderIconPng(256);
        var p128 = RenderIconPng(128);
        var p48 = RenderIconPng(48);
        var p32 = RenderIconPng(32);
        var p16 = RenderIconPng(16);

        string sfDir = Path.Combine(root, "SwiftFetch");
        string distDir = Path.Combine(root, "dist");
        string extDir = Path.Combine(root, "BrowserExtension");
        string distExtDir = Path.Combine(root, "dist", "BrowserExtension");

        Directory.CreateDirectory(sfDir);
        Directory.CreateDirectory(distDir);
        Directory.CreateDirectory(extDir);
        Directory.CreateDirectory(distExtDir);

        static void TryWrite(string path, byte[] bytes) { try { File.WriteAllBytes(path, bytes); } catch { } }
        TryWrite(Path.Combine(sfDir, "app-icon.png"), p256);
        TryWrite(Path.Combine(distDir, "app-icon.png"), p256);

        try { WriteIco(Path.Combine(sfDir, "app.ico"), (256, p256), (48, p48), (32, p32), (16, p16)); } catch { }
        try { WriteIco(Path.Combine(distDir, "app.ico"), (256, p256), (48, p48), (32, p32), (16, p16)); } catch { }

        TryWrite(Path.Combine(extDir, "icon-16.png"), p16);
        TryWrite(Path.Combine(extDir, "icon-32.png"), p32);
        TryWrite(Path.Combine(extDir, "icon-48.png"), p48);
        TryWrite(Path.Combine(extDir, "icon-128.png"), p128);

        TryWrite(Path.Combine(distExtDir, "icon-16.png"), p16);
        TryWrite(Path.Combine(distExtDir, "icon-32.png"), p32);
        TryWrite(Path.Combine(distExtDir, "icon-48.png"), p48);
        TryWrite(Path.Combine(distExtDir, "icon-128.png"), p128);
    }

    private static byte[] RenderIconPng(int size)
    {
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            double scale = size / 256.0;
            var bgBrush = new SolidColorBrush(Color.FromRgb(22, 27, 34));
            var bgPen = new Pen(new SolidColorBrush(Color.FromRgb(46, 60, 75)), 4 * scale);
            ctx.DrawRoundedRectangle(bgBrush, bgPen, new Rect(8 * scale, 8 * scale, 240 * scale, 240 * scale), 48 * scale, 48 * scale);

            var barBrush = new SolidColorBrush(Color.FromRgb(62, 194, 143));
            double[] barY = [62, 106, 150, 194];
            foreach (double y in barY)
            {
                ctx.DrawRoundedRectangle(barBrush, null, new Rect(36 * scale, y * scale, 184 * scale, 14 * scale), 7 * scale, 7 * scale);
            }

            var boltGeometry = new StreamGeometry();
            using (var gc = boltGeometry.Open())
            {
                gc.BeginFigure(new Point(145 * scale, 28 * scale), true, true);
                gc.LineTo(new Point(82 * scale, 124 * scale), true, false);
                gc.LineTo(new Point(125 * scale, 124 * scale), true, false);
                gc.LineTo(new Point(92 * scale, 228 * scale), true, false);
                gc.LineTo(new Point(176 * scale, 110 * scale), true, false);
                gc.LineTo(new Point(135 * scale, 110 * scale), true, false);
            }
            boltGeometry.Freeze();

            var boltBrush = new SolidColorBrush(Color.FromRgb(85, 255, 178));
            var boltPen = new Pen(new SolidColorBrush(Color.FromRgb(18, 22, 28)), 5 * scale)
            {
                LineJoin = PenLineJoin.Round
            };
            ctx.DrawGeometry(boltBrush, boltPen, boltGeometry);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void WriteIco(string path, params (int Size, byte[] Png)[] images)
    {
        using var fs = File.Create(path);
        using var writer = new BinaryWriter(fs);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)images.Length);

        int offset = 6 + images.Length * 16;
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write((int)png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }
    }

    private static void RenderDialogs()
    {
        string outDir = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "dialogs");
        Directory.CreateDirectory(outDir);

        // 1. Add URL Dialog
        var addUrl = new AddUrlDialog("https://assets.ign.com/videos/zencoder/2023/01/12/1920/guide.mp4");
        RenderWindow(addUrl, 520, 175, Path.Combine(outDir, "1-add-url-dialog.png"));

        // 2. Download Info Dialog
        var info = new DownloadInfoDialog(new Uri("https://www.internetdownloadmanager.com/languages/idm_al.zip"), new DownloadSettings());
        RenderWindow(info, 580, 290, Path.Combine(outDir, "2-download-info-dialog.png"));

        // 3. Download Progress Dialog
        var job = new DownloadJob
        {
            Url = "https://assets.ign.com/videos/zencoder/2023/01/12/1920/5c09bbb2-guide.mp4",
            Destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Video", "Chapter 13- Nothing Left - A Plague Tale- Requiem Guide.mp4"),
            TotalBytes = 199_890_000,
            RangeSupported = true,
            State = DownloadState.Downloading
        };
        long chunk = 199_890_000 / 6;
        for (int i = 0; i < 6; i++)
        {
            var seg = new DownloadSegment
            {
                Start = i * chunk,
                End = (i + 1) * chunk - 1,
                WrittenBytes = (long)(chunk * (0.3 + i * 0.1)),
                Active = true,
                StatusText = (i % 2 == 0) ? "Receiving data..." : "Connecting..."
            };
            job.Segments.Add(seg);
        }
        var row = new DownloadRow(job);
        row.Update(new DownloadProgress(64_238_000, 199_890_000, 31_966_000, 6));
        var progress = new DownloadProgressDialog(row, new DownloadEngine(), _ => { }, _ => { }, _ => { });
        RenderWindow(progress, 600, 460, Path.Combine(outDir, "3-download-progress-dialog.png"));

        // 4. Download Complete Dialog
        job.State = DownloadState.Completed;
        var complete = new DownloadCompleteDialog(job);
        RenderWindow(complete, 540, 255, Path.Combine(outDir, "4-download-complete-dialog.png"));

        Console.WriteLine("ALL DIALOGS RENDERED to " + outDir);
    }

    private static void RenderWindow(Window window, int width, int height, string outputPath)
    {
        window.Width = width;
        window.Height = height;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0;
        window.Show();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content) { Stretch = Stretch.None }, null, new Rect(0, 0, width, height));
        }
        bitmap.Render(drawing);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(outputPath);
        png.Save(output);
        if (window is MainWindow main) CloseAndDrain(main); else window.Close();
        Console.WriteLine("Rendered: " + outputPath);
    }
}


