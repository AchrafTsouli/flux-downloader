using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using SwiftFetch.Core;

namespace SwiftFetch;

public partial class DownloadCompleteDialog : Window
{
    private readonly DownloadJob job;
    private readonly Action<bool>? onDontShowAgainChanged;

    public DownloadCompleteDialog(DownloadJob job, Action<bool>? onDontShowAgainChanged = null)
    {
        this.job = job;
        this.onDontShowAgainChanged = onDontShowAgainChanged;
        InitializeComponent();
        DownloadDialogChrome.Install(this);

        AddressBox.Text = job.Url;
        SavedAsBox.Text = job.Destination;
        AddressBox.ToolTip = job.Url; SavedAsBox.ToolTip = job.Destination;
        SavedAsBox.CaretIndex = SavedAsBox.Text.Length;
        Loaded += (_,_) => SavedAsBox.ScrollToHorizontalOffset(double.MaxValue);
        ShowFileIcon();

        long bytes = job.TotalBytes ?? (File.Exists(job.Destination) ? new FileInfo(job.Destination).Length : 0);
        DownloadedSizeBlock.Text = $"Downloaded {FormatSize(bytes)} ({bytes:N0} Bytes)";

        DontShowAgainCheck.Checked += (_, _) => onDontShowAgainChanged?.Invoke(true);
        DontShowAgainCheck.Unchecked += (_, _) => onDontShowAgainChanged?.Invoke(false);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(job.Destination))
            {
                Process.Start(new ProcessStartInfo(job.Destination) { UseShellExecute = true });
                Close();
            }
            else
            {
                MessageBox.Show(this, "The downloaded file was moved or deleted from disk:\n" + job.Destination, "File not found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open file: " + ex.Message, "Flux", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(job.Destination))
            {
                var info = new OpenAsInfo { File = Path.GetFullPath(job.Destination), Flags = 0x00000004 /* OAIF_EXEC */ };
                int result = SHOpenWithDialog(new WindowInteropHelper(this).EnsureHandle(), ref info);
                // Cancelling the application picker is a normal user action.
                if (result < 0 && result != unchecked((int)0x800704C7)) Marshal.ThrowExceptionForHR(result);
            }
            else
                MessageBox.Show(this, "The downloaded file was moved or deleted from disk:\n" + job.Destination, "File not found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open dialog: " + ex.Message, "Flux", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public uint Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(job.Destination))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{job.Destination}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(Path.GetDirectoryName(job.Destination)))
            {
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(job.Destination)!) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open folder: " + ex.Message, "Flux", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatSize(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.##} {units[index]}";
    }

    private Point? dragStart;
    private void ShowFileIcon()
    {
        try
        {
            if (!System.IO.File.Exists(job.Destination)) return;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(job.Destination);
            if (icon == null) return;
            FileIconImage.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            FileIconImage.Visibility = Visibility.Visible; FolderArt.Visibility = Visibility.Collapsed;
        }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { /* The folder picture stays. */ }
    }
    private void DragHandle_Down(object sender, System.Windows.Input.MouseButtonEventArgs e) => dragStart = e.GetPosition(this);
    private void DragHandle_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || dragStart is not Point start || !System.IO.File.Exists(job.Destination)) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragStart = null;
        // Copy (never move): the download keeps its file, the drop target gets its own copy.
        var data = new DataObject(DataFormats.FileDrop, new[] { job.Destination });
        DragDrop.DoDragDrop(DragHandle, data, DragDropEffects.Copy);
    }
}
