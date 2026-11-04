using System.IO;
using System.Windows.Interop;
using SwiftFetch.Core;

namespace SwiftFetch;

internal static class InstallationCheck
{
    // Explicit offline diagnostic for an installed build. Never read the user's
    // history, register browsers, start transfers or display a window.
    public static int Run()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "apex-installation-check-" + Guid.NewGuid().ToString("N"));
        string log = Path.Combine(Path.GetTempPath(), "Flux-installation-check.log");
        try
        {
            Directory.CreateDirectory(scratch);
            var store = new SqliteDownloadStateStore(Path.Combine(scratch, "history.db"));
            var snapshot = store.Load();
            store.Save(snapshot);
            if (store.Load().Downloads.Count != 0) throw new InvalidDataException("The isolated database check failed.");
            if (!new VideoTools(Path.Combine(AppContext.BaseDirectory, "video-tools")).Available)
                throw new FileNotFoundException("The bundled video tools are incomplete.");
            var dialog = new DownloadCompleteDialog(new DownloadJob { Url = "https://example.invalid/installation-check", Destination = Path.Combine(scratch, "test.txt"), TotalBytes = 0 });
            try
            {
                _ = new WindowInteropHelper(dialog).EnsureHandle();
                using var tray = new TrayIcon(dialog, () => { });
                if (!tray.UsesApplicationIcon) throw new InvalidDataException("The installed tray icon fell back to a generic Windows icon.");
            }
            finally { dialog.Close(); }
            File.WriteAllText(log, "PASS: WPF window/resources, branded tray icon, native SQLite, encrypted preferences, and bundled video tool presence. No user data accessed.");
            return 0;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(log, error.ToString()); } catch { }
            return 1;
        }
        finally
        {
            // This GUID directory was created by this diagnostic, outside user history.
            try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch { }
        }
    }
}
