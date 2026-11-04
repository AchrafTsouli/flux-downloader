using System.IO;
using SwiftFetch.Core;

namespace SwiftFetch;

public partial class MainWindow
{
    private void RefreshActiveDownloads()
    {
        var active = scheduler.ActiveCount == 0 ? [] : Downloads.Where(row => scheduler.IsRunning(row.Job)).ToArray();
        foreach (var row in active) row.Refresh();
        if (active.Length > 0)
        {
            if (!marqueeSelection.IsSelecting && (libraryFilter != "all" || SearchBox.Text.Length > 0)) RefreshLibraryView();
            UpdateSummary();
            if (persistenceAvailable)
                try
                {
                    // State changes and user actions still save complete snapshots atomically.
                    // Periodic byte checkpoints only serialize the running jobs.
                    if (store is SqliteDownloadStateStore sqlite) sqlite.SaveCheckpoints(active.Select(row => row.Job));
                    else SaveHistory();
                }
                catch (Exception error) { MessageText.Text = "Could not save download checkpoints: " + error.Message; }
        }
        CheckClipboard(); CheckQueueSchedules(); CheckQueueCompletion(); CheckSynchronizationQueues();
        tray?.UpdateTip($"Flux · {scheduler.ActiveCount} active · {Downloads.Count(row => row.Job.State == DownloadState.Queued)} waiting");
    }
}
