using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SwiftFetch;
using SwiftFetch.Core;

internal static partial class Program
{
    private static void MeasureIdlePerformance(string label)
    {
        string directory = Path.Combine(Environment.CurrentDirectory,".test-tmp","performance-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var snapshot = new DownloadSnapshot { Settings = new() { DownloadDirectory=directory, ShowTrayIcon=false } };
        for (int i=0;i<1000;i++) snapshot.Downloads.Add(new() {Url="https://example.invalid/"+i+".zip",Destination=Path.Combine(directory,i+".zip"),State=DownloadState.Completed,TotalBytes=1000000});
        new SqliteDownloadStateStore(Path.Combine(directory,"history.db")).Save(snapshot);
        var window = new MainWindow(directory,directory) { ShowInTaskbar=false, ShowActivated=false, Opacity=0 };
        window.Show(); window.UpdateLayout();
        void Pump(int seconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(seconds) };
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        Pump(3);
        var process = Process.GetCurrentProcess();
        object Measure(string mode)
        {
            process.Refresh(); var cpu=process.TotalProcessorTime;long allocation=GC.GetTotalAllocatedBytes();var clock=Stopwatch.StartNew();
            Pump(8);process.Refresh();
            return new {mode,seconds=clock.Elapsed.TotalSeconds,allocatedMB=(GC.GetTotalAllocatedBytes()-allocation)/1048576.0,cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,privateMB=process.PrivateMemorySize64/1048576.0,workingSetMB=process.WorkingSet64/1048576.0,managedMB=GC.GetTotalMemory(false)/1048576.0};
        }
        var visible=Measure("visible-idle");window.Hide();var hidden=Measure("hidden-idle");
        CloseAndDrain(window);
        string output=Path.Combine(Environment.CurrentDirectory,".test-tmp","performance-"+Path.GetFileName(label)+".json");
        File.WriteAllText(output,JsonSerializer.Serialize(new {rows=1000,visible,hidden},new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(output));
    }
}
