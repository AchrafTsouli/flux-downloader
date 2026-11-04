using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftFetch;
using SwiftFetch.Core;

internal static partial class Program
{
    private static void CheckLibraryInteractions()
    {
        var frame = new DispatcherFrame(); Exception? failure = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            MainWindow? main = null;
            try
            {
                string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","library-interactions-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                main = new MainWindow(directory,directory,new DownloadEngine(new QueueHandler())) { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                main.Preferences.ShowDownloadCompleteDialog = false; main.Show();
                foreach (var state in Enum.GetValues<DownloadState>()) main.Downloads.Add(new(new() { Url = "https://queue.test/" + state + ".zip", Destination = Path.Combine(directory,state + ".zip"), State = state }));
                var sync = main.Preferences.Queues.Single(q => q.Synchronization);
                var syncRow = new DownloadRow(new() { Url = "https://queue.test/sync.zip", Destination = Path.Combine(directory,"sync.zip"), State = DownloadState.Paused, Queue = sync.Name }); main.Downloads.Add(syncRow);
                var all = (TreeViewItem)main.FindName("AllCategory"); var queues = (TreeViewItem)main.FindName("QueuesTree");
                var grid = (DataGrid)main.FindName("DownloadGrid"); var flags = BindingFlags.NonPublic|BindingFlags.Instance;
                object? Invoke(string method,params object[] args) => typeof(MainWindow).GetMethod(method,flags)!.Invoke(main,args);
                Require(main.VisibleDownloads.Cast<DownloadRow>().Count() == main.Downloads.Count && ((TextBlock)main.FindName("LibraryTitle")).Text == "All Downloads","The initial screen is not all downloads.");
                Invoke("HandleQueueClick",queues,1);
                var expected = main.Downloads.Where(r => r.Job.Queue == "Default" && DownloadQueueMembership.IsUnfinished(r.Job.State)).ToArray();
                Require(main.VisibleDownloads.Cast<DownloadRow>().SequenceEqual(expected) && ((TextBlock)main.FindName("LibraryTitle")).Text == "Main download queue","Queues did not display only unfinished main-queue files.");
                bool expanded = queues.IsExpanded; Invoke("HandleQueueClick",queues,2); Require(queues.IsExpanded != expanded,"Double-click did not collapse queues."); Invoke("HandleQueueClick",queues,2); Require(queues.IsExpanded == expanded,"Double-click did not expand queues.");
                var mainItem = queues.Items.OfType<TreeViewItem>().Single(i => (string)i.Tag == "queue:Default"); mainItem.IsSelected = true;
                Require(main.VisibleDownloads.Cast<DownloadRow>().SequenceEqual(expected),"The main queue child did not filter its unfinished files.");
                var syncItem = queues.Items.OfType<TreeViewItem>().Single(i => (string)i.Tag == "queue:" + sync.Name); syncItem.IsSelected = true;
                Require(main.VisibleDownloads.Cast<DownloadRow>().SequenceEqual([syncRow]),"The sync view contains files outside its unfinished queue.");
                Require((string)typeof(MainWindow).GetField("selectedQueueName",flags)!.GetValue(main)! == sync.Name,"Sync queue was not selected for commands.");
                main.Downloads.Remove(syncRow); Invoke("RefreshLibraryView"); Require(main.VisibleDownloads.IsEmpty && main.Downloads.Count > 0,"An empty queue fell back to all history."); main.Downloads.Add(syncRow);
                all.IsSelected = true; Require(main.VisibleDownloads.Cast<DownloadRow>().SequenceEqual(main.Downloads),"All Downloads did not restore completed and failed history."); syncItem.IsSelected = true;
                Invoke("HandleQueueClick",syncItem,2);
                var planner = Application.Current.Windows.OfType<QueueSchedulerWindow>().Single(); planner.Opacity = 0; planner.ShowInTaskbar = false;
                Require(planner.Owner == null && NativeEnabled(main),"Queue settings blocked the main window.");
                var queueList = (ListBox)typeof(QueueSchedulerWindow).GetField("queues",flags)!.GetValue(planner)!;
                Require(((DownloadQueueDefinition)queueList.SelectedItem).Name == sync.Name,"Double-click opened the wrong queue settings.");
                Invoke("HandleQueueClick",mainItem,2); Require(Application.Current.Windows.OfType<QueueSchedulerWindow>().Count() == 1 && ((DownloadQueueDefinition)queueList.SelectedItem).Name == "Default","Double-click did not switch the existing planner to the main queue.");
                var plannerFiles = (DataGrid)typeof(QueueSchedulerWindow).GetField("files",flags)!.GetValue(planner)!;
                Require(plannerFiles.Items.Cast<DownloadRow>().SequenceEqual(expected),"Planner contains failed/completed files."); planner.Close();
                var tree = (TreeView)main.FindName("CategoryTree"); tree.Items.OfType<TreeViewItem>().Single(i => (string)i.Tag == "failed").IsSelected = true;
                Require(main.VisibleDownloads.Cast<DownloadRow>().Count() == 4 && main.VisibleDownloads.Cast<DownloadRow>().All(r => DownloadQueueMembership.IsFailure(r.Job.State)),"Failed filter does not isolate errors.");
                syncItem.IsSelected = true; Require(main.VisibleDownloads.Cast<DownloadRow>().SequenceEqual([syncRow]),"Selecting a queue after an error filter did not show its own unfinished files.");
                Invoke("StartQueue_Click",main,new RoutedEventArgs());
                Require(main.ActiveDownloadCount == 1 && syncRow.Job.State == DownloadState.Connecting && main.Downloads.First(r => r.Job.State == DownloadState.Ready).Job.Queue == "Default","Toolbar did not start only the selected sync queue.");
                Invoke("StopQueue_Click",main,new RoutedEventArgs()); for (int n=0; main.ActiveDownloadCount > 0 && n<100; n++) await Task.Delay(10);
                Require(syncRow.Job.State == DownloadState.Paused,"Toolbar did not stop the selected sync queue.");
                Invoke("StartQueue","Default"); Require(main.Downloads.Where(r => DownloadQueueMembership.IsFailure(r.Job.State)).Count() == 4,"Start queue retried historical failures.");
                Invoke("StopQueue","Default"); for (int n=0; main.ActiveDownloadCount > 0 && n<100; n++) await Task.Delay(10);
                all.IsSelected = true; main.Downloads.Clear();
                for (int index=0; index<3; index++) main.Downloads.Add(new(new() { Url = $"https://queue.test/drag-{index}.zip", Destination = Path.Combine(directory,$"drag-{index}.zip"), State = DownloadState.Paused }));
                main.UpdateLayout(); await Dispatcher.CurrentDispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle); grid.UpdateLayout();
                var marquee = typeof(MainWindow).GetField("marqueeSelection",flags)!.GetValue(main)!;
                void Drag(string method,params object[] args) => marquee.GetType().GetMethod(method,flags)!.Invoke(marquee,method == "Begin" ? [args[0],false] : args);
                Point RowPoint(int index) { grid.ScrollIntoView(grid.Items[index]); grid.UpdateLayout(); var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index); return row.TranslatePoint(new Point(90,row.ActualHeight/2),grid); }
                var top = RowPoint(0); var bottom = RowPoint(2);
                Drag("Begin",top); Drag("Update",bottom); Drag("Finish"); Require(grid.SelectedItems.Count == 3,"Dragging downward did not select rows without Ctrl.");
                Invoke("RefreshLibraryView"); Require(grid.SelectedItems.Count == 3,"Background view refresh discarded the selection.");
                Drag("Begin",bottom); Drag("Update",top); Drag("Finish"); Require(grid.SelectedItems.Count == 3,"Dragging upward lost rows.");
                Drag("Begin",new Point(90,grid.ActualHeight-35)); Drag("Update",top); Drag("Finish"); Require(grid.SelectedItems.Count == 3,"Dragging from empty list space did not select rows.");
                for (int index=3; index<80; index++) main.Downloads.Add(new(new() { Url = $"https://queue.test/drag-{index}.zip", Destination = Path.Combine(directory,$"drag-{index}.zip"), State = DownloadState.Paused }));
                top = RowPoint(0); Drag("Begin",top); Drag("Update",new Point(90,grid.ActualHeight-10)); int initial = grid.SelectedItems.Count;
                await Task.Delay(300); Drag("Finish"); Require(grid.SelectedItems.Count > initial,"Marquee did not extend through automatic scrolling.");
                main.Downloads.Clear();
                DownloadRow Add(string name,DownloadState state) { var row = new DownloadRow(new() { Url = "https://queue.test/" + name, Destination = Path.Combine(directory,name), State = state }); main.Downloads.Add(row); return row; }
                var complete = Add("keep.zip",DownloadState.Completed); File.WriteAllBytes(complete.Job.Destination,[1,2,3]);
                var partial = Add("partial.zip",DownloadState.Paused); File.WriteAllBytes(partial.Job.PartPath(0),[4,5]);
                grid.SelectedItems.Clear(); grid.SelectedItems.Add(complete); grid.SelectedItems.Add(partial);
                _ = Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>
                {
                    var dialog = Application.Current.Windows.OfType<DeleteDownloadsDialog>().Single();
                    Require(!dialog.DeleteCompletedFiles && !dialog.DontAskAgain,"Deletion defaults are unsafe.");
                    dialog.DontAskBox.IsChecked = true; dialog.UpdateLayout(); CaptureVisibleContent(dialog,Path.Combine(directory,"delete-dialog.png")); dialog.DialogResult = true;
                }));
                Invoke("Remove_Click",main,new RoutedEventArgs());
                Require(main.Downloads.Count == 0 && !main.Preferences.ConfirmDeleteDownloads && File.Exists(complete.Job.Destination) && File.Exists(partial.Job.PartPath(0)),"Confirmation opt-out deleted files or was not saved.");
                var immediate = Add("immediate.zip",DownloadState.Completed); File.WriteAllBytes(immediate.Job.Destination,[7]); grid.SelectedItem = immediate;
                Invoke("Remove_Click",main,new RoutedEventArgs()); Require(main.Downloads.Count == 0 && File.Exists(immediate.Job.Destination),"Removal without confirmation did not keep the file.");
                var disk = Add("delete-file.zip",DownloadState.Completed); File.WriteAllBytes(disk.Job.Destination,[8]);
                var kept = Add("keep-part.zip",DownloadState.Paused); File.WriteAllBytes(kept.Job.PartPath(0),[9]);
                Invoke("DeleteConfirmedDownloads",new DownloadRow[] { disk,kept },true);
                Require(!File.Exists(disk.Job.Destination) && File.Exists(kept.Job.PartPath(0)),"Explicit file deletion did not preserve partial downloads.");
                CloseAndDrain(main); main = null;
                Require(!new SqliteDownloadStateStore(Path.Combine(directory,"history.db")).Load().Settings.ConfirmDeleteDownloads,"Confirmation preference did not survive saving.");
                Console.WriteLine("PASS separate all-download history and unfinished queue views, empty queues stay empty, independent planner keeps only unfinished queue files, failed isolation, single/double queue clicks, selected independent planner, mouse marquee in both directions and empty space, auto-scroll, confirmation opt-out and explicit completed-file deletion. Preview: " + directory);
            }
            catch(Exception error) { failure = error; }
            finally { if (main != null) CloseAndDrain(main); frame.Continue = false; }
        }));
        Dispatcher.PushFrame(frame); if (failure != null) throw new Exception("Library interaction tests failed",failure);
    }
}
