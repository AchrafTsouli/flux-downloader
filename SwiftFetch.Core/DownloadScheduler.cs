namespace SwiftFetch.Core;

/// <summary>Owns the bounded FIFO download queue. GUI callers use their dispatcher context.</summary>
public sealed class DownloadScheduler(Func<DownloadJob, CancellationToken, Task> run, Func<int> maximumActive, Func<string, int>? queueLimit = null)
{
    private readonly object gate = new();
    private readonly Queue<DownloadJob> pending = new();
    private readonly Dictionary<string, (CancellationTokenSource Cancellation, Task Task)> active = [];
    private bool stopping;
    private readonly HashSet<string> pausedQueues = new(StringComparer.OrdinalIgnoreCase);
    public event Action<DownloadJob>? Changed;
    public bool IsRunning(DownloadJob job) { lock (gate) return active.ContainsKey(job.Id); }
    public int ActiveCount { get { lock (gate) return active.Count; } }

    public void Enqueue(DownloadJob job)
    {
        lock (gate)
        {
            if (stopping || job.State == DownloadState.Completed || active.ContainsKey(job.Id) || pending.Any(value => value.Id == job.Id)) return;
            job.State = DownloadState.Queued;
            pending.Enqueue(job);
            Changed?.Invoke(job);
            Pump();
        }
    }
    public void Pump()
    {
        lock (gate)
        {
            while (!stopping && pending.Count > 0 && active.Count < Math.Clamp(maximumActive(), 1, 16))
            {
                var candidates = pending.ToArray();
                var job = candidates.FirstOrDefault(value => !pausedQueues.Contains(value.Queue) && active.Keys.Count(id => activeJobs.TryGetValue(id, out var running) && running.Queue.Equals(value.Queue, StringComparison.OrdinalIgnoreCase)) < (queueLimit?.Invoke(value.Queue) ?? 16));
                if (job == null) break;
                pending.Clear(); foreach (var item in candidates.Where(value => value != job)) pending.Enqueue(item);
                if (job.State != DownloadState.Queued) continue;
                var cancellation = new CancellationTokenSource();
                job.State = DownloadState.Connecting;
                var task = ExecuteAsync(job, cancellation);
                active.Add(job.Id, (cancellation, task));
                activeJobs[job.Id] = job;
                Changed?.Invoke(job);
            }
        }
    }
    private async Task ExecuteAsync(DownloadJob job, CancellationTokenSource cancellation)
    {
        await Task.Yield(); // Register before invoking even a synchronously completed runner.
        try { await run(job, cancellation.Token); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { job.State = DownloadState.Paused; }
        catch (Exception error) { job.State = DownloadState.Failed; job.Error = error.Message; job.ErrorKind = DownloadErrorKind.Unknown; }
        finally
        {
            lock (gate)
            {
                active.Remove(job.Id);
                activeJobs.Remove(job.Id);
                cancellation.Dispose();
                Changed?.Invoke(job);
                Pump();
            }
        }
    }
    public void Pause(DownloadJob job)
    {
        lock (gate)
        {
            if (active.TryGetValue(job.Id, out var running)) running.Cancellation.Cancel();
            else if (job.State == DownloadState.Queued)
            {
                var retained = pending.Where(value => value.Id != job.Id).ToArray();
                pending.Clear();
                foreach (var value in retained) pending.Enqueue(value);
                job.State = DownloadState.Paused;
                Changed?.Invoke(job);
            }
        }
    }
    public void PauseAll()
    {
        lock (gate)
        {
            var waiting = pending.ToArray();
            pending.Clear();
            foreach (var job in waiting) { job.State = DownloadState.Paused; Changed?.Invoke(job); }
            foreach (var running in active.Values.ToArray()) running.Cancellation.Cancel();
        }
    }
    private readonly Dictionary<string, DownloadJob> activeJobs = [];
    public void PauseQueue(string name)
    {
        lock (gate) { pausedQueues.Add(name); foreach (var job in activeJobs.Values.Concat(pending).Where(job => job.Queue.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray()) Pause(job); }
    }
    public void ReorderQueue(string name,IReadOnlyList<string> ids)
    {
        lock (gate)
        {
            var ranks = ids.Distinct().Select((id,index) => (id,index)).ToDictionary(pair => pair.id,pair => pair.index);
            var ordered = pending.Where(job => job.Queue == name).OrderBy(job => ranks.GetValueOrDefault(job.Id,int.MaxValue)).ToArray();
            var replacement = new Queue<DownloadJob>(ordered);
            var all = pending.ToArray(); pending.Clear();
            foreach (var job in all) pending.Enqueue(job.Queue == name ? replacement.Dequeue() : job);
        }
    }
    public void StartQueue(string name) { lock (gate) { pausedQueues.Remove(name); Pump(); } }
    public async Task ShutdownAsync()
    {
        Task[] running;
        lock (gate) { stopping = true; PauseAll(); running = active.Values.Select(value => value.Task).ToArray(); }
        await Task.WhenAll(running);
    }
    public async Task PauseAllAsync()
    {
        Task[] running;
        lock (gate) { stopping = true; PauseAll(); running = active.Values.Select(value => value.Task).ToArray(); }
        try { await Task.WhenAll(running); } finally { lock (gate) stopping = false; }
    }
}
