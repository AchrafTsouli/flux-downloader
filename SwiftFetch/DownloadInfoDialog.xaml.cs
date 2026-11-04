using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SwiftFetch.Core;

namespace SwiftFetch;

public sealed record DownloadConfirmation(Uri Uri, string Destination, string Category, bool StartNow, DownloadSession? Session, bool NameIsCustom = false, long? TotalBytes = null, string Queue = "Default", bool RememberFolder = false, string Description = "", VideoSelection? Video = null);

public partial class DownloadInfoDialog : Window
{
    private Uri uri;
    private readonly DownloadSettings settings;
    private DownloadSession? session;
    private readonly CancellationTokenSource cts = new();
    private string currentFolder;
    private string currentFileName;
    private string suggestedFileName;
    private readonly DownloadEngine? probeEngine;
    private readonly VideoSelection? video;

    public Uri DownloadUri => uri;
    public DownloadSession? DownloadSession => session;
    public string DestinationPath { get; private set; } = "";
    public string Category { get; private set; } = "General";
    public bool StartNow { get; private set; } = true;
    public long? ResolvedTotalBytes { get; private set; }
    public bool NameIsCustom => !Path.GetFileName(DestinationPath).Equals(suggestedFileName, StringComparison.OrdinalIgnoreCase);
    public string SelectedQueue => (QueueBox.SelectedItem as DownloadQueueDefinition)?.Name ?? "Default";
    public DownloadConfirmation Confirmation => new(DownloadUri, DestinationPath, Category, StartNow, DownloadSession, NameIsCustom, ResolvedTotalBytes, SelectedQueue, RememberCategoryCheck.IsChecked == true, DescriptionBox.Text.Trim(), video);

    public DownloadInfoDialog(Uri uri, DownloadSettings settings, DownloadSession? session = null, string? suggestedName = null, DownloadEngine? probeEngine = null, VideoSelection? video = null)
    {
        this.uri = uri;
        this.video = video;
        video?.Validate(uri.AbsoluteUri);
        this.settings = settings;
        this.session = session ?? settings.ManualSession(uri);
        this.probeEngine = probeEngine;
        InitializeComponent();
        DownloadDialogChrome.Install(this);
        QueueBox.ItemsSource = settings.Queues; QueueBox.DisplayMemberPath = "DisplayName"; QueueBox.SelectedIndex = 0;
        QueuePanel.Visibility = settings.ShowQueueSelection && settings.Queues.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var category in DownloadCategories.All.Concat(settings.CategoryDirectories.Keys).Concat(settings.CategoryExtensions.Keys).Distinct())
            if (!CategoryBox.Items.OfType<ComboBoxItem>().Any(item => item.Tag as string == category)) CategoryBox.Items.Add(new ComboBoxItem { Content = category, Tag = category });

        UrlBox.Text = uri.AbsoluteUri;

        // 1. Initial filename extraction: prioritize suggestedName
        string initialName = !string.IsNullOrWhiteSpace(suggestedName)
            ? FileNames.Sanitize(suggestedName)
            : FileNames.Sanitize(Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "download"));
        if (string.IsNullOrWhiteSpace(initialName) || !Path.HasExtension(initialName)) initialName = "download.bin";
        currentFileName = initialName;
        suggestedFileName = initialName;

        // 2. Initial category based on file extension
        Category = settings.CategoryForFile(currentFileName);
        currentFolder = settings.GetDirectoryForCategory(Category);

        // Guarantee folder exists on disk (creates Downloads\Category if missing on clean PC)
        try { Directory.CreateDirectory(currentFolder); } catch { }

        SelectCategoryItem(Category);
        UpdateUiFields();

        FileSizeText.Text = "Checking…";
        if (video == null) Loaded += (_, _) => _ = ProbeMetadataAsync();
        else
        {
            ResolvedTotalBytes = video.TotalBytes;
            FileSizeText.Text = video.TotalBytes is long bytes ? (video.Approximate ? "≈ " : "") + FormatSize(bytes) : "Unknown";
            FileSizeText.ToolTip = video.Approximate ? "Estimated combined download size; the final assembled size can differ." : "Combined audio and video download size. Final container size can differ.";
            DescriptionBox.Text = video.Label;
        }
        Closed += (_, _) => cts.Cancel();
    }

    // Overload for compatibility
    public DownloadInfoDialog(Uri uri, string defaultRootFolder, DownloadSession? session = null, string? suggestedName = null)
        : this(uri, new DownloadSettings { DownloadDirectory = defaultRootFolder }, session, suggestedName)
    {
    }

    private void SelectCategoryItem(string category)
    {
        foreach (ComboBoxItem item in CategoryBox.Items)
        {
            if (string.Equals(item.Tag as string, category, StringComparison.OrdinalIgnoreCase))
            {
                CategoryBox.SelectedItem = item;
                break;
            }
        }
        if (CategoryBox.SelectedItem == null && CategoryBox.Items.Count > 0)
            CategoryBox.SelectedIndex = 0;
    }

    private void UpdateUiFields()
    {
        DestinationPath = Path.Combine(currentFolder, currentFileName);
        SaveAsBox.Text = DestinationPath;
        SaveAsBox.ToolTip = DestinationPath;
        SaveAsBox.CaretIndex = SaveAsBox.Text.Length;
        Dispatcher.BeginInvoke(() => SaveAsBox.ScrollToHorizontalOffset(double.MaxValue));
        CategoryFolderBox.Text = currentFolder.TrimEnd('\\') + @"\";
        RememberCategoryCheck.Content = $"Remember this path for \"{Category}\" category";
        UpdateCategoryIcon(Category);
    }

    private void UpdateCategoryIcon(string category)
    {
        string templateKey = category switch
        {
            "Compressed" => "BooksIconTemplate",
            "Video" => "VideoIconTemplate",
            "Programs" => "ProgramsIconTemplate",
            "Music" => "MusicIconTemplate",
            "Documents" => "DocIconTemplate",
            _ => "DocIconTemplate"
        };

        if (TryFindResource(templateKey) is ControlTemplate template)
        {
            FileTypeIconControl.Template = template;
        }
    }

    public async Task ProbeMetadataAsync()
    {
        if (video != null) return;
        string originalPath = SaveAsBox.Text;
        try
        {
            using var owned = probeEngine == null ? new DownloadEngine() : null;
            var probeJob = new DownloadJob { Url = uri.AbsoluteUri, Session = session, Destination = Path.Combine(currentFolder, currentFileName) };
            var metadata = await (probeEngine ?? owned!).ProbeAsync(probeJob, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            uri = new Uri(probeJob.Url);
            session = probeJob.Session;
            UrlBox.Text = uri.AbsoluteUri;
            if (!string.IsNullOrWhiteSpace(metadata.SuggestedFileName) && SaveAsBox.Text == originalPath)
            {
                currentFileName = metadata.SuggestedFileName;
                Category = settings.CategoryForFile(currentFileName);
                currentFolder = settings.GetDirectoryForCategory(Category);
                try { Directory.CreateDirectory(currentFolder); } catch { }
                SelectCategoryItem(Category);
                UpdateUiFields();
            }

            ResolvedTotalBytes = metadata.TotalBytes;
            suggestedFileName = metadata.SuggestedFileName ?? suggestedFileName;
            FileSizeText.Text = metadata.TotalBytes is long total ? FormatSize(total) : "Unknown";
            FileSizeText.ToolTip = metadata.RangeSupported ? "Full file size supplied by the server." : "Full file size; the server did not confirm byte-range support.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ResolvedTotalBytes = null;
            FileSizeText.Text = "Unknown";
            FileSizeText.ToolTip = ex is HttpRequestException http && http.StatusCode != null
                ? $"The server returned HTTP {(int)http.StatusCode}; this is not the file size."
                : ex is DownloadFailureException ? ex.Message : "File information is unavailable. Capture the actual download again from the browser.";
        }
    }

    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            Category = tag;
            currentFolder = settings.GetDirectoryForCategory(Category);
            try { Directory.CreateDirectory(currentFolder); } catch { }
            UpdateUiFields();
        }
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog
        {
            Title = "Choose destination file",
            FileName = currentFileName,
            InitialDirectory = currentFolder
        };
        if (sfd.ShowDialog(this) == true)
        {
            DestinationPath = sfd.FileName;
            currentFolder = Path.GetDirectoryName(DestinationPath) ?? currentFolder;
            currentFileName = Path.GetFileName(DestinationPath);
            try { Directory.CreateDirectory(currentFolder); } catch { }
            UpdateUiFields();
        }
    }

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new RenameWindow("Custom") { Title = "Add new category", Owner = this };
        if (await IndependentDialogs.ShowAsync(dialog) && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            string newCat = FileNames.Sanitize(dialog.FileName);
            if (CategoryBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag as string == newCat) is { } existing) { CategoryBox.SelectedItem = existing; return; }
            var newItem = new ComboBoxItem { Content = newCat, Tag = newCat };
            CategoryBox.Items.Add(newItem);
            CategoryBox.SelectedItem = newItem;
            settings.CategoryDirectories.TryAdd(newCat, settings.GetDirectoryForCategory(newCat));
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        string ext = Path.GetExtension(currentFileName).ToLowerInvariant();
        if (ext == ".zip")
        {
            try
            {
                PreviewButton.IsEnabled = false;
                PreviewButton.Content = "Inspecting...";
                var entries = await RemoteZipInspector.InspectAsync(uri.AbsoluteUri, session, cts.Token, proxy: probeEngine?.Options.Proxy);
                string summary = string.Join("\n", entries.Take(25).Select(entry => (entry.IsDirectory ? "[DIR]  " : "       ") + entry.Name + " (" + (entry.UncompressedSize / 1024) + " KB)"));
                if (entries.Count > 25) summary += $"\n... and {entries.Count - 25} more files.";
                MessageBox.Show(this, $"Remote Archive Contents ({entries.Count} items):\n\n" + summary, "ZIP Preview - " + currentFileName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Remote preview not available: " + ex.Message, "Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                PreviewButton.IsEnabled = true;
                PreviewButton.Content = "Preview";
            }
        }
        else
        {
            MessageBox.Show(this, $"File: {currentFileName}\nSize: {FileSizeText.Text}\nURL: {uri.AbsoluteUri}", "File Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RememberCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Toggle state saved on confirmation
    }

    private void StartDownload_Click(object sender, RoutedEventArgs e) => ApplyAndClose(true);

    private void DownloadLater_Click(object sender, RoutedEventArgs e) => ApplyAndClose(false);

    private void ApplyAndClose(bool start)
    {
        try
        {
        StartNow = start;
        string customPath = SaveAsBox.Text.Trim();
        if (!string.IsNullOrEmpty(customPath))
        {
            DestinationPath = Path.GetFullPath(customPath);
        }
        else
        {
            DestinationPath = Path.Combine(currentFolder, currentFileName);
        }

        string targetDir = Path.GetDirectoryName(DestinationPath)!;
        if (string.IsNullOrWhiteSpace(Path.GetFileName(DestinationPath)) || Path.GetFileName(DestinationPath).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("Choose a valid filename.");
        Directory.CreateDirectory(targetDir);

        if (RememberCategoryCheck.IsChecked == true)
        {
            settings.CategoryDirectories[Category] = targetDir;
        }

        IndependentDialogs.Finish(this,true);
        }
        catch (Exception error) { MessageBox.Show(this,error.Message,"Download destination",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IndependentDialogs.Finish(this,false);
    }

    private static string FormatSize(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.##}  {units[index]}";
    }
}
