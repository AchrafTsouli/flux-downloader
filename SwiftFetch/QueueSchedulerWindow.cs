using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SwiftFetch.Core;
namespace SwiftFetch;
public sealed class QueueSchedulerWindow : Window
{
    private readonly DownloadSettings settings;
    private readonly ObservableCollection<DownloadRow> downloads;
    private readonly DownloadScheduler scheduler;
    private readonly Action<string> start,stop;
    private readonly Action save;
    private readonly List<DownloadQueueDefinition> drafts;
    private readonly ListBox queues = new() { DisplayMemberPath = "DisplayName" };
    private readonly DataGrid files = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended, CanUserAddRows = false, MinHeight = 280 };
    private readonly StackPanel schedule = new() { Margin = new Thickness(16) };
    private readonly CheckBox enabled = new() { Content = "Enable schedule / periodic synchronization" },startup = new() { Content = "Start this queue when Flux starts" },daily = new() { Content = "Daily (selected days below)" },exit = new() { Content = "Exit Flux when the queue completes successfully" };
    private readonly TextBox queueName = new();
    private readonly TextBox startTime = new(),stopTime = new(),concurrent = new(),retries = new(),interval = new(),openFile = new(),globalSlots = new(),globalSpeed = new();
    private readonly DatePicker date = new();
    private readonly ComboBox power = new() { ItemsSource = new[] { QueueCompletionAction.None, QueueCompletionAction.Sleep, QueueCompletionAction.Hibernate, QueueCompletionAction.Shutdown } };
    private readonly CheckBox force = new() { Content = "Force applications to close for shutdown" },disconnect = new() { Content = "Disconnect the VPN connected by Flux when done" };
    private readonly Dictionary<DayOfWeek,CheckBox> weekdays = [];
    private readonly DownloadMarqueeSelection marqueeSelection;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DownloadQueueDefinition? selected;
    public QueueSchedulerWindow(DownloadSettings settings,ObservableCollection<DownloadRow> downloads,DownloadScheduler scheduler,Action<string> start,Action<string> stop,Action save)
    {
        marqueeSelection = new(files);
        this.settings = settings; this.downloads = downloads; this.scheduler = scheduler; this.start = start; this.stop = stop; this.save = save;
        drafts = JsonSerializer.Deserialize<List<DownloadQueueDefinition>>(JsonSerializer.Serialize(settings.Queues))!;
        Title = "Scheduler and queues — Flux"; Width = 900; Height = 760; MinWidth = 760; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty,"ThemeBackground1B1B1B"); SetResourceReference(ForegroundProperty,"ThemeForegroundD4D4D4");
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,14,0,0) }; DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
        Button AddButton(Panel panel,string text,Action action) { var button = new Button { Content = text, MinWidth = 95, Margin = new Thickness(4), Padding = new Thickness(10,5,10,5) }; button.Click += (_,_) => { try { action(); } catch (Exception error) { MessageBox.Show(this,error.Message,"Queue settings",MessageBoxButton.OK,MessageBoxImage.Warning); } }; panel.Children.Add(button); return button; }
        AddButton(footer,"Start now",() => { Apply(); if (selected != null) start(selected.Name); });
        AddButton(footer,"Stop",() => { if (selected != null) stop(selected.Name); });
        AddButton(footer,"Apply",Apply); AddButton(footer,"Close",Close);
        var body = new Grid(); body.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); body.ColumnDefinitions.Add(new() { Width = new GridLength(1,GridUnitType.Star) }); root.Children.Add(body);
        var left = new DockPanel { Margin = new Thickness(0,0,16,0) }; body.Children.Add(left);
        var buttons = new StackPanel(); DockPanel.SetDock(buttons,Dock.Bottom); left.Children.Add(buttons);
        AddButton(buttons,"New queue",() => { var q = new DownloadQueueDefinition { Name = "Queue " + (drafts.Count + 1) }; while (drafts.Any(x => x.Name == q.Name)) q.Name += " new"; drafts.Add(q); queues.Items.Refresh(); queues.SelectedItem = q; });
        AddButton(buttons,"Delete queue",() => { if (selected == null || selected.Name == "Default" || selected.Synchronization) throw new InvalidOperationException("Built-in queues cannot be deleted."); if (downloads.Any(r => r.Job.Queue == selected.Name && (scheduler.IsRunning(r.Job) || r.Job.State == DownloadState.Queued))) throw new InvalidOperationException("Stop this queue before deleting it."); var name = selected.Name; drafts.Remove(selected); foreach (var row in downloads.Where(r => r.Job.Queue == name)) { row.Job.Queue = "Default"; row.Refresh(); } queues.Items.Refresh(); queues.SelectedIndex = 0; Apply(); });
        queues.ItemsSource = drafts; left.Children.Add(queues);
        queues.SetResourceReference(BackgroundProperty,"ThemeBackground171717"); queues.SetResourceReference(ForegroundProperty,"ThemeForegroundDDDDDD"); queues.SetResourceReference(BorderBrushProperty,"ThemeBorderBrush383838");
        var tabs = new TabControl(); tabs.SetResourceReference(BackgroundProperty,"ThemeBackground1B1B1B"); tabs.SetResourceReference(ForegroundProperty,"ThemeForegroundDDDDDD"); tabs.SetResourceReference(BorderBrushProperty,"ThemeBorderBrush383838"); Grid.SetColumn(tabs,1); body.Children.Add(tabs);
        tabs.Items.Add(new TabItem { Header = "Schedule", Content = new ScrollViewer { Content = schedule, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        void Field(string label,Control field) { var panel = new DockPanel { Margin = new Thickness(0,6,0,6) }; var text = new TextBlock { Text = label, Width = 215, VerticalAlignment = VerticalAlignment.Center }; panel.Children.Add(text); field.MinHeight = 27; panel.Children.Add(field); schedule.Children.Add(panel); }
        Field("Queue name",queueName); schedule.Children.Add(enabled); schedule.Children.Add(startup);
        Field("Start time (HH:mm, blank = off)",startTime); Field("Stop time (HH:mm, blank = off)",stopTime);
        schedule.Children.Add(daily); Field("One-time date",date);
        var days = new WrapPanel(); schedule.Children.Add(days); foreach (var day in Enum.GetValues<DayOfWeek>()) { var box = new CheckBox { Content = day.ToString()[..3], Margin = new Thickness(4) }; weekdays[day] = box; days.Children.Add(box); }
        Field("Retries per file (0–10)",retries); Field("Sync interval (minutes)",interval);
        schedule.Children.Add(new TextBlock { Text = "Synchronization checks remote size/date/ETag and saves changed files as new copies.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,8) });
        Field("Open file when queue finishes",openFile); schedule.Children.Add(disconnect); schedule.Children.Add(exit); Field("Computer action when done",power); schedule.Children.Add(force);
        files.SetResourceReference(BackgroundProperty,"ThemeBackground1B1B1B"); files.SetResourceReference(ForegroundProperty,"ThemeForegroundDDDDDD"); files.SetResourceReference(BorderBrushProperty,"ThemeBorderBrush383838");
        files.SetResourceReference(DataGrid.RowBackgroundProperty,"ThemeBackground222222"); files.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"ThemeBackground262626"); files.HeadersVisibility = DataGridHeadersVisibility.Column; files.RowHeight = 30;
        var filePanel = new DockPanel { Margin = new Thickness(12) };
        var slotPanel = new DockPanel { Margin = new Thickness(0,0,0,10) }; DockPanel.SetDock(slotPanel,Dock.Top); filePanel.Children.Add(slotPanel); slotPanel.Children.Add(new TextBlock { Text = "Files downloading at the same time (1–16)", Width = 330, VerticalAlignment = VerticalAlignment.Center }); slotPanel.Children.Add(concurrent);
        var orderButtons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(orderButtons,Dock.Bottom); filePanel.Children.Add(orderButtons);
        AddButton(orderButtons,"Move up",() => Move(-1)); AddButton(orderButtons,"Move down",() => Move(1)); AddButton(orderButtons,"Remove from queue",() => { foreach (var row in files.SelectedItems.OfType<DownloadRow>().ToArray()) { if (scheduler.IsRunning(row.Job)) throw new InvalidOperationException("Pause the file first."); scheduler.Pause(row.Job); row.Job.Queue = selected?.Name == "Default" ? "Unassigned" : "Default"; row.Refresh(); } save(); RefreshFiles(); });
        files.Columns.Add(new DataGridTextColumn { Header = "File name", Binding = new Binding("FileName"), MinWidth = 240, Width = new DataGridLength(1,DataGridLengthUnitType.Star) });
        files.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new Binding("TotalSizeText"), Width = 95 }); files.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("DisplayStatus"), Width = 95 }); files.Columns.Add(new DataGridTextColumn { Header = "Progress", Binding = new Binding("PercentText"), Width = 70 });
        filePanel.Children.Add(files); tabs.Items.Add(new TabItem { Header = "Files in the queue", Content = filePanel });
        var limits = new StackPanel { Margin = new Thickness(20) }; limits.Children.Add(new TextBlock { Text = "Maximum simultaneous downloads across all queues (1–16)" }); globalSlots.Text = settings.MaximumActiveDownloads.ToString(); limits.Children.Add(globalSlots); limits.Children.Add(new TextBlock { Text = "Global speed limit in KiB/s (0 = unlimited)", Margin = new Thickness(0,16,0,4) }); globalSpeed.Text = (settings.GlobalSpeedLimitBytes / 1024).ToString(); limits.Children.Add(globalSpeed); tabs.Items.Add(new TabItem { Header = "Download limits", Content = limits });
        queues.SelectionChanged += (_,_) => { if (selected != null) { try { ReadFields(); } catch (Exception error) { MessageBox.Show(this,error.Message); } } selected = queues.SelectedItem as DownloadQueueDefinition; LoadFields(); };
        daily.Checked += (_,_) => UpdateScheduleFields(); daily.Unchecked += (_,_) => UpdateScheduleFields(); power.SelectionChanged += (_,_) => UpdateScheduleFields();
        queues.SelectedIndex = 0; timer.Tick += (_,_) => { foreach (var row in downloads) row.Refresh(); RefreshFiles(); }; timer.Start(); Closed += (_,_) => timer.Stop();
    }
    private static int Number(TextBox field,int min,int max) => int.TryParse(field.Text,out var n) && n >= min && n <= max ? n : throw new ArgumentException($"Enter a number from {min} to {max}.");
    private void ReadFields()
    {
        if (selected == null) return;
        string? Time(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : TimeOnly.TryParse(box.Text,out var time) ? time.ToString("HH:mm") : throw new ArgumentException("Enter time as HH:mm.");
        var first = Time(startTime); var last = Time(stopTime); int slots = Number(concurrent,1,16), retry = Number(retries,0,10), repeat = Number(interval,1,1440);
        if (string.IsNullOrWhiteSpace(queueName.Text)) throw new ArgumentException("Enter a queue name.");
        selected.Label = queueName.Text.Trim();
        selected.StartTime = first; selected.StopTime = last; selected.MaximumActive = slots; selected.Retries = retry; selected.IntervalMinutes = repeat;
        selected.Enabled = enabled.IsChecked == true; selected.StartOnStartup = startup.IsChecked == true; selected.Daily = daily.IsChecked == true; selected.ScheduledDate = date.SelectedDate;
        selected.Weekdays = weekdays.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList(); selected.OpenFileWhenDone = openFile.Text.Trim(); selected.ExitWhenDone = exit.IsChecked == true; selected.PowerWhenDone = power.SelectedItem is QueueCompletionAction action ? action : QueueCompletionAction.None; selected.ForcePowerAction = force.IsChecked == true; selected.DisconnectVpnWhenDone = disconnect.IsChecked == true;
    }
    private void UpdateScheduleFields()
    {
        date.IsEnabled = daily.IsChecked != true;
        foreach (var day in weekdays.Values) day.IsEnabled = daily.IsChecked == true;
        force.IsEnabled = power.SelectedItem is QueueCompletionAction.Shutdown;
    }
    public void SelectQueue(string name) { queues.SelectedItem = drafts.FirstOrDefault(q => q.Name == name) ?? drafts.FirstOrDefault(); }
    private void LoadFields()
    {
        if (selected == null) return;
        queueName.Text = selected.DisplayName;
        enabled.IsChecked = selected.Enabled; startup.IsChecked = selected.StartOnStartup; daily.IsChecked = selected.Daily; date.SelectedDate = selected.ScheduledDate;
        startTime.Text = selected.StartTime ?? ""; stopTime.Text = selected.StopTime ?? ""; concurrent.Text = selected.MaximumActive.ToString(); retries.Text = (selected.Retries ?? settings.MaximumRetries).ToString(); interval.Text = selected.IntervalMinutes.ToString(); interval.IsEnabled = selected.Synchronization;
        foreach (var pair in weekdays) pair.Value.IsChecked = selected.Weekdays.Contains(pair.Key);
        openFile.Text = selected.OpenFileWhenDone; exit.IsChecked = selected.ExitWhenDone; power.SelectedItem = selected.PowerWhenDone; force.IsChecked = selected.ForcePowerAction; disconnect.IsChecked = selected.DisconnectVpnWhenDone; UpdateScheduleFields(); RefreshFiles();
    }
    private void RefreshFiles()
    {
        if (selected == null) return;
        var rows = downloads.Where(r => r.Job.Queue == selected.Name && DownloadQueueMembership.IsUnfinished(r.Job.State)).OrderBy(r => { int i = selected.FileOrder.IndexOf(r.Job.Id); return i < 0 ? int.MaxValue : i; }).ToArray();
        selected.FileOrder = rows.Select(r => r.Job.Id).ToList();
        if (files.ItemsSource is DownloadRow[] current && current.SequenceEqual(rows)) return;
        var selection = files.SelectedItems.OfType<DownloadRow>().ToArray(); files.ItemsSource = rows;
        foreach (var row in selection.Where(rows.Contains)) files.SelectedItems.Add(row);
    }
    private void Move(int direction)
    {
        if (selected == null || files.SelectedItem is not DownloadRow row) return;
        RefreshOrder(); int index = selected.FileOrder.IndexOf(row.Job.Id), other = index + direction; if (index < 0 || other < 0 || other >= selected.FileOrder.Count) return;
        (selected.FileOrder[index],selected.FileOrder[other]) = (selected.FileOrder[other],selected.FileOrder[index]); RefreshFiles(); files.SelectedItem = row;
    }
    private void RefreshOrder() { if (selected != null) foreach (var row in downloads.Where(r => r.Job.Queue == selected.Name)) if (!selected.FileOrder.Contains(row.Job.Id)) selected.FileOrder.Add(row.Job.Id); }
    private void Apply()
    {
        ReadFields(); int slots = Number(globalSlots,1,16), speed = Number(globalSpeed,0,int.MaxValue / 1024);
        var old = settings.Queues; settings.Queues = JsonSerializer.Deserialize<List<DownloadQueueDefinition>>(JsonSerializer.Serialize(drafts))!;
        try { settings.Validate(); } catch { settings.Queues = old; throw; }
        settings.MaximumActiveDownloads = slots; settings.GlobalSpeedLimitBytes = (long)speed * 1024;
        foreach (var q in settings.Queues) scheduler.ReorderQueue(q.Name,q.FileOrder); save(); queues.Items.Refresh();
    }
}
