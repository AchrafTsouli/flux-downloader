using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using SwiftFetch.Core;
namespace SwiftFetch;
public partial class SettingsWindow : Window
{
    public DownloadSettings Result { get; }
    public int MaximumActive => Result.MaximumActiveDownloads;
    public int MaximumRetries => Result.MaximumRetries;
    public bool VerifySavedBytesWithoutValidator => Result.VerifySavedBytesWithoutValidator;
    private readonly List<Action> commit = [];
    private readonly CheckBox startup = new() { Content = "Launch Flux when Windows starts" };
    private bool initialStartup;
    private readonly TextBox ActiveBox = new(), RetryBox = new();
    private readonly CheckBox VerifyResumeBox = new() { Content = "Full byte verification when the server has no validator" };
    public SettingsWindow(DownloadSettings settings, string? tab = null)
    {
        Result = JsonSerializer.Deserialize<DownloadSettings>(JsonSerializer.Serialize(settings))!;
        InitializeComponent(); RegisterName("VerifyResumeBox", VerifyResumeBox);
        var general = Page("General", "Browser and system integration");
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); initialStartup = key?.GetValue("ApexDownloader") != null; startup.IsChecked = initialStartup; }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException) { startup.IsEnabled = false; startup.ToolTip = "Windows denied access to startup settings: " + error.Message; }
        general.Children.Add(startup);
        Toggle(general, "Monitor clipboard for download links", nameof(Result.MonitorClipboard));
        Toggle(general, "Capture downloads in Chrome / Brave", nameof(Result.BrowserCaptureEnabled));
        Toggle(general, "Show Download with Flux in the browser context menu", nameof(Result.BrowserContextMenuEnabled));
        Toggle(general, "Show download panel on direct HTML video/audio files", nameof(Result.BrowserMediaPanelEnabled));
        Choice(general, "Bypass capture key", nameof(Result.BypassKey), ["Alt", "Control", "Shift", "None"]);
        Choice(general, "Force capture key", nameof(Result.ForceKey), ["Shift", "Control", "Alt", "None"]);
        Note(general, "Reload the extension after installing a new release. Browser policy is refreshed automatically. Allow the extension in private windows in Chrome/Brave settings.");
        var types = Page("File types", "Automatic browser capture");
        Field(types, "File extensions (space separated)", nameof(Result.CaptureExtensions), true);
        Field(types, "Excluded sites (one hostname per line, *.example.com allowed)", nameof(Result.ExcludedSites), true);
        Field(types, "Excluded addresses (one URL prefix per line)", nameof(Result.ExcludedAddresses), true);
        var defaults = new Button { Content = "Restore file type defaults", HorizontalAlignment = HorizontalAlignment.Left };
        defaults.Click += (_, _) => { if (types.Children.OfType<TextBox>().FirstOrDefault() is { } box) box.Text = new DownloadSettings().CaptureExtensions; }; types.Children.Add(defaults);
        var folders = Page("Save to", "Categories, extensions and folders");
        Field(folders, "Default download folder", nameof(Result.DownloadDirectory), folder: true);
        Field(folders, "Temporary parts folder (empty = beside the downloaded file)", nameof(Result.TemporaryDirectory), folder: true);
        Note(folders, "Folder changes apply to new downloads; existing paused files keep their parts.");
        Toggle(folders, "Set file creation and modification dates from the server", nameof(Result.PreserveServerDate));
        var categories = new System.Collections.ObjectModel.ObservableCollection<CategoryEntry>(DownloadCategories.All.Concat(Result.CategoryDirectories.Keys).Concat(Result.CategoryExtensions.Keys).Distinct().Select(name => new CategoryEntry { Name = name, Extensions = Result.CategoryExtensions.GetValueOrDefault(name, ""), Folder = Result.CategoryDirectories.GetValueOrDefault(name, "") }));
        Table(folders, categories, ["Name", "Extensions", "Folder"], () => new CategoryEntry { Name = "New category" });
        commit.Add(() => { Result.CategoryDirectories = categories.Where(c => c.Folder.Length > 0).ToDictionary(c => c.Name, c => Path.GetFullPath(c.Folder), StringComparer.OrdinalIgnoreCase); Result.CategoryExtensions = categories.Where(c => c.Extensions.Length > 0).ToDictionary(c => c.Name, c => c.Extensions, StringComparer.OrdinalIgnoreCase); });
        var downloads = Page("Downloads", "Default download settings");
        Toggle(downloads, "Confirm removal of downloads from the list", nameof(Result.ConfirmDeleteDownloads));
        Toggle(downloads, "Show start download dialogue", nameof(Result.ShowStartDownloadDialog));
        Toggle(downloads, "Show progress window when a download starts", nameof(Result.ShowProgressOnStart));
        Toggle(downloads, "Show queue selection in the start dialogue", nameof(Result.ShowQueueSelection));
        Toggle(downloads, "Ask which queue when adding a batch", nameof(Result.ShowBatchQueueSelection));
        Toggle(downloads, "Show connection details in the progress window", nameof(Result.ProgressDetailsVisible));
        Toggle(downloads, "Show speed limiter tab in the progress window", nameof(Result.ProgressSpeedTabVisible));
        Toggle(downloads, "Add new downloads to the queue without starting", nameof(Result.QueueNewDownloads));
        Toggle(downloads, "Show download complete dialogue", nameof(Result.ShowDownloadCompleteDialog));
        Toggle(downloads, "Ignore modification date changes when resuming (ETag still checked)", nameof(Result.IgnoreLastModifiedChanges));
        VerifyResumeBox.IsChecked = Result.VerifySavedBytesWithoutValidator; downloads.Children.Add(VerifyResumeBox); commit.Add(() => Result.VerifySavedBytesWithoutValidator = VerifyResumeBox.IsChecked == true);
        Note(downloads, "Full verification rereads saved bytes over the Internet. Keep it off for fast resume from the saved position.");
        Choice(downloads, "When a duplicate link is added", nameof(Result.DuplicatePolicy), ["New copy", "Use existing", "Ask"]);
        Field(downloads, "User-Agent for manually added downloads", nameof(Result.ManualUserAgent));
        Field(downloads, "Virus scanner executable (empty = disabled)", nameof(Result.VirusScanner));
        Field(downloads, "Scanner arguments — {file} is replaced by the file path", nameof(Result.VirusScannerArguments));
        Note(downloads, "Use the scanner's command line documentation. Exit code 0 publishes the file; another code stops completion.");
        var connection = Page("Connection", "Connections and bandwidth");
        Choice(connection, "Connections per file", nameof(Result.DefaultConnections), ["1", "2", "4", "8", "16"]);
        Number(connection, "Simultaneous downloads (1–16)", ActiveBox, Result.MaximumActiveDownloads, v => Result.MaximumActiveDownloads = v);
        Number(connection, "Automatic retries (0–10)", RetryBox, Result.MaximumRetries, v => Result.MaximumRetries = v);
        Field(connection, "Global speed limit (bytes/s, 0 = unlimited)", nameof(Result.GlobalSpeedLimitBytes));
        Note(connection, "More connections can improve speed when the server supports byte ranges. A per-file limit is available in its progress window.");
        var proxy = Page("Proxy / SOCKS", "Network proxy");
        Field(proxy, "Proxy URL (empty = direct connection)", nameof(Result.ProxyAddress));
        Field(proxy, "Username", nameof(Result.ProxyUsername)); Secret(proxy, "Password", nameof(Result.ProxyPassword));
        Note(proxy, "Supported schemes: http, https, socks4, socks4a, socks5. Example: socks5://127.0.0.1:1080. New connections use the saved proxy.");
        var logins = Page("Site logins", "Saved HTTP Basic credentials");
        Note(logins, "Credentials are restricted to the exact origin. Browser cookies remain the preferred login for websites. Saved preferences are encrypted for your Windows account.");
        var loginRows = new System.Collections.ObjectModel.ObservableCollection<SiteLogin>(Result.SiteLogins);
        var loginGrid = Table(logins, loginRows, ["Enabled", "Origin", "Username"], () => new SiteLogin());
        var passwordButton = new Button { Content = "Set selected login password", HorizontalAlignment = HorizontalAlignment.Left };
        passwordButton.Click += (_, _) => { if (loginGrid.SelectedItem is SiteLogin login) { var dialog = new TextEntryDialog("Site password", "Password for " + login.Origin, login.Password, true) { Owner = this }; if (dialog.ShowDialog() == true) login.Password = dialog.Value; } }; logins.Children.Add(passwordButton);
        commit.Add(() => Result.SiteLogins = loginRows.ToList());
        var vpn = Page("Dial up / VPN", "Windows VPN connection");
        Field(vpn, "Existing Windows connection profile name", nameof(Result.VpnProfile));
        Toggle(vpn, "Connect before a download starts", nameof(Result.ConnectVpnBeforeDownload));
        Toggle(vpn, "Disconnect when all downloads are idle", nameof(Result.DisconnectVpnWhenIdle));
        Note(vpn, "Uses rasdial and credentials already saved in Windows. Create the connection in Windows settings first.");
        var sounds = Page("Sounds", "Download notifications");
        Toggle(sounds, "Play Windows sound when a download completes", nameof(Result.PlayCompletionSound));
        Toggle(sounds, "Play Windows sound when a download fails", nameof(Result.PlayErrorSound));
        var queues = Page("Scheduler", "Queues and daily schedules");
        var queueRows = new System.Collections.ObjectModel.ObservableCollection<DownloadQueueDefinition>(Result.Queues);
        Table(queues, queueRows, ["Label", "MaximumActive", "Enabled", "StartTime", "StopTime", "Daily"], () => new DownloadQueueDefinition { Name = "Queue " + (queueRows.Count + 1) });
        Note(queues, "Time format: HH:mm. Use Downloads → Scheduler for queue files, order, weekdays, startup and completion actions. Enabled schedules run while Flux is running, including in the tray. Start/Stop queue menus also work immediately. Files saved for later can be assigned to a queue from their context menu.");
        commit.Add(() => { foreach (var queue in queueRows) { queue.StartTime = string.IsNullOrWhiteSpace(queue.StartTime) ? null : queue.StartTime.Trim(); queue.StopTime = string.IsNullOrWhiteSpace(queue.StopTime) ? null : queue.StopTime.Trim(); } Result.Queues = queueRows.ToList(); });
        var appearance = Page("View", "Appearance");
        Toggle(appearance, "Show categories", nameof(Result.ShowCategories)); Toggle(appearance, "Show toolbar", nameof(Result.ShowToolbar));
        Field(appearance, "Font family", nameof(Result.FontFamily)); Field(appearance, "Font size (9–24)", nameof(Result.FontSize));
        Field(appearance, "Project home page URL (empty = Flux on GitHub)", nameof(Result.HomePageUrl));
        Field(appearance, "Update feed URL (empty = official Flux releases on GitHub)", nameof(Result.UpdateFeedUrl));
        Toggle(appearance, "Check the update feed daily", nameof(Result.AutoCheckUpdates));
        Toggle(appearance, "Keep the video downloader (yt-dlp) updated daily from its GitHub releases", nameof(Result.AutoUpdateVideoTools));
        Note(appearance, "Update feed: version.json made by scripts/Sign-Release.ps1 — {\"version\",\"url\",\"sha256\",\"signature\"}. Signed releases install after you confirm; unsigned feeds only open the download page. yt-dlp updates are checked against the release's SHA-256 list and kept in %LOCALAPPDATA%\\SwiftFetch\\tools.");
        if (tab != null) Tabs.SelectedItem = Tabs.Items.OfType<TabItem>().FirstOrDefault(item => (string)item.Header == tab);
    }
    private StackPanel Page(string title, string heading)
    {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        Tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    private static void Note(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0,8,0,12) });
    private void Toggle(Panel panel, string text, string property)
    {
        var p = typeof(DownloadSettings).GetProperty(property)!; var box = new CheckBox { Content = text, IsChecked = (bool)p.GetValue(Result)! }; panel.Children.Add(box); commit.Add(() => p.SetValue(Result, box.IsChecked == true));
    }
    private void Field(Panel panel, string label, string property, bool multiline = false, bool folder = false)
    {
        var p = typeof(DownloadSettings).GetProperty(property)!; panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,4) });
        var box = new TextBox { Text = Convert.ToString(p.GetValue(Result), System.Globalization.CultureInfo.InvariantCulture) ?? "", MinHeight = multiline ? 65 : 30, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap }; panel.Children.Add(box);
        if (folder) { var browse = new Button { Content = "Browse…", Margin = new Thickness(0,4,0,0), HorizontalAlignment = HorizontalAlignment.Right }; browse.Click += (_, _) => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) box.Text = dialog.FolderName; }; panel.Children.Add(browse); }
        commit.Add(() => p.SetValue(Result, Convert.ChangeType(box.Text.Trim(), p.PropertyType, System.Globalization.CultureInfo.InvariantCulture)));
    }
    private void Secret(Panel panel, string label, string property)
    {
        var p = typeof(DownloadSettings).GetProperty(property)!; panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,4) });
        var box = new PasswordBox { Password = (string)p.GetValue(Result)!, Padding = new Thickness(8), Background = System.Windows.Media.Brushes.Black, Foreground = System.Windows.Media.Brushes.White }; panel.Children.Add(box); commit.Add(() => p.SetValue(Result, box.Password));
    }
    private void Choice(Panel panel, string label, string property, string[] items)
    {
        var p = typeof(DownloadSettings).GetProperty(property)!; panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,4) });
        var box = new ComboBox { ItemsSource = items, SelectedItem = Convert.ToString(p.GetValue(Result)), MinHeight = 30 }; panel.Children.Add(box); commit.Add(() => p.SetValue(Result, Convert.ChangeType(box.SelectedItem ?? items[0], p.PropertyType)));
    }
    private void Number(Panel panel, string label, TextBox box, int initial, Action<int> apply)
    { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,8,0,4) }); box.Text = initial.ToString(); panel.Children.Add(box); commit.Add(() => apply(int.Parse(box.Text))); }
    private DataGrid Table<T>(Panel panel, System.Collections.ObjectModel.ObservableCollection<T> rows, string[] columns, Func<T> create)
    {
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, RowHeaderWidth = 0, RowHeight = 28, Height = 210, Background = System.Windows.Media.Brushes.Transparent, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0,8,0,8) };
        foreach (var name in columns) { string header = name switch { "MaximumActive" => "Simultaneous", "StartTime" => "Start at", "StopTime" => "Stop at", "Daily" => "Repeat daily", "Origin" => "Site origin", _ => name }; grid.Columns.Add(typeof(T).GetProperty(name)!.PropertyType == typeof(bool) ? new DataGridCheckBoxColumn { Header = header, Binding = new Binding(name) } : new DataGridTextColumn { Header = header, Binding = new Binding(name), Width = new DataGridLength(1,DataGridLengthUnitType.Star) }); }
        panel.Children.Add(grid); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var add = new Button { Content = "New" }; var delete = new Button { Content = "Remove" };
        add.Click += (_, _) => { var value = create(); rows.Add(value); grid.SelectedItem = value; grid.ScrollIntoView(value); }; delete.Click += (_, _) => { if (grid.SelectedItem is T row) rows.Remove(row); }; buttons.Children.Add(add); buttons.Children.Add(delete); panel.Children.Add(buttons);
        commit.Add(() => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); }); return grid;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { foreach (var save in commit) save(); Result.Validate(); _ = new System.Windows.Media.FontFamily(Result.FontFamily); if (Result.VirusScanner.Length > 0 && !File.Exists(Result.VirusScanner)) throw new ArgumentException("Virus scanner executable does not exist."); CommandArguments.Split(Result.VirusScannerArguments);
            if (startup.IsEnabled && (startup.IsChecked == true) != initialStartup)
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (startup.IsChecked == true) key.SetValue("ApexDownloader", "\"" + (Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory,"Flux.exe")) + "\" --background"); else key.DeleteValue("ApexDownloader", false);
            }
            DialogResult = true;
        } catch (Exception error) { ErrorText.Text = error.Message; }
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this, "Changes take effect after OK. Keep full resume verification off for fast resuming. Browser integration requires the bundled Chrome/Brave extension. Schedules need Flux to stay open. HTTP Basic logins do not replace cookies on websites with browser login.", "Options help");
}
public sealed class CategoryEntry { public string Name { get; set; } = ""; public string Extensions { get; set; } = ""; public string Folder { get; set; } = ""; }
