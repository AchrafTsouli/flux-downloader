using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using SwiftFetch.Core;

string root = Path.Combine(Environment.CurrentDirectory, ".test-tmp", "SwiftFetch-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
byte[] original = new byte[3 * 1024 * 1024 + 17];
new Random(42).NextBytes(original);
int passed = 0;
try
{
    await Check("display speed smooths bursts and follows sustained changes", () =>
    {
        var clock = new RateTestClock(); var display = new TransferRateDisplay(clock);
        display.Update(6_000_000);
        clock.Advance(.2); display.Update(12_000_000);
        Require(display.BytesPerSecond > 6_000_000 && display.BytesPerSecond < 7_000_000,"Short burst was not smoothed.");
        for(int i=0;i<100;i++){clock.Advance(.2);display.Update(12_000_000);}
        Require(Math.Abs(display.BytesPerSecond-12_000_000)<1000,"Display failed to follow a sustained speed change.");
        return Task.CompletedTask;
    });
    await Check("display speed decays on stalls and resets between transfers", () =>
    {
        var clock = new RateTestClock(); var display = new TransferRateDisplay(clock);
        display.Update(8_000_000); clock.Advance(4);
        Require(display.BytesPerSecond > 0 && display.BytesPerSecond < 2_000_000,"Stalled speed stayed high.");
        clock.Advance(6); Require(display.BytesPerSecond==0,"Stalled speed did not reach zero.");
        display.Reset(); Require(display.BytesPerSecond==0,"Paused transfer retained its speed.");
        display.Update(double.NaN); display.Update(double.PositiveInfinity); display.Update(-100);
        Require(display.BytesPerSecond==0,"Invalid speed accepted.");
        display.Update(3_000_000); Require(display.BytesPerSecond==3_000_000,"Resume reused stale transfer speed.");
        return Task.CompletedTask;
    });
    await Check("incremental checkpoints preserve history preferences and pending requests", () =>
    {
        var store = new SqliteDownloadStateStore(Path.Combine(root,"checkpoint-only.db"));
        var active = new DownloadJob { Url="https://example.test/a.zip",Destination=Path.Combine(root,"a.zip"),State=DownloadState.Downloading };
        var old = new DownloadJob { Url="https://example.test/b.zip",Destination=Path.Combine(root,"b.zip"),State=DownloadState.Completed,Description="Keep me" };
        var pending = new PendingDownload { RequestId=Guid.NewGuid().ToString(),Url="https://example.test/c.zip" };
        store.Save(new() { Settings=new() { DownloadDirectory=root,Language="fr" },Downloads=[active,old],PendingDownloads=[pending] });
        active.TotalBytes=1000; active.Segments=[new(){Start=0,End=999,CommittedBytes=250}];
        store.SaveCheckpoints([active]);
        var saved=store.Load();
        Require(saved.Downloads.Count==2 && saved.Downloads.Single(j=>j.Id==old.Id).Description=="Keep me","Checkpoint removed history.");
        Require(saved.Settings.Language=="fr" && saved.PendingDownloads.Single().RequestId==pending.RequestId,"Checkpoint changed settings/pending requests.");
        Require(saved.Downloads.Single(j=>j.Id==active.Id).Segments[0].CommittedBytes==250,"Checkpoint bytes lost.");
        active.TotalBytes=2000;
        try { store.SaveCheckpoints([active,new(){Url="https://example.test/new.zip",Destination=Path.Combine(root,"new.zip")}]);throw new Exception("Unknown job checkpoint accepted."); }
        catch(InvalidDataException) { }
        Require(store.Load().Downloads.Single(j=>j.Id==active.Id).TotalBytes==1000,"A failed incremental checkpoint was not rolled back.");
        return Task.CompletedTask;
    });
    await Check("queue membership excludes completed cancelled and every failure state", () =>
    {
        foreach (var state in Enum.GetValues<DownloadState>())
        {
            bool terminal = state is DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed or DownloadState.AuthRequired or DownloadState.ExpiredUrl or DownloadState.NoDiskSpace;
            Require(DownloadQueueMembership.IsUnfinished(state) != terminal,"Wrong queue membership: " + state);
        }
        return Task.CompletedTask;
    });
    await Check("delete confirmation defaults on and its opt-out survives restart", () =>
    {
        var settings = new DownloadSettings { DownloadDirectory = root }; Require(settings.ConfirmDeleteDownloads,"Delete confirmation must default on.");
        settings.ConfirmDeleteDownloads = false;
        var store = new SqliteDownloadStateStore(Path.Combine(root,"delete-preference.db")); store.Save(new() { Settings = settings });
        Require(!store.Load().Settings.ConfirmDeleteDownloads,"Delete confirmation opt-out was lost."); return Task.CompletedTask;
    });
    await Check("four connections produce a byte-identical file", async () =>
    {
        using var server = new FileServer(original);
        using var engine = new DownloadEngine(server);
        var job = Job("parallel");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.Segments.Count == 4, "Expected four segments.");
        Require(server.Requests.Count == 5, "Expected one probe and four transfer requests.");
    });
    await Check("pause and resume after serializing history", async () =>
    {
        using var server = new FileServer(original) { Slow = true };
        using var engine = new DownloadEngine(server);
        var job = Job("resume");
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(p => { if (p.Bytes > 0) cancel.Cancel(); }), cancel.Token);
        Require(job.Status == "Paused", "Expected paused state.");
        long saved = PartialBytes(job);
        Require(saved > 0 && saved < original.Length, "Expected a partial download.");
        job = JsonSerializer.Deserialize<DownloadJob>(JsonSerializer.Serialize(job))!;
        long before = server.Served;
        server.Slow = false;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(server.Served - before <= original.Length - saved + 1, "Resume downloaded existing bytes again.");
    });
    await Check("server without ranges falls back to one connection", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { SupportsRange = false });
        var job = Job("fallback");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.Segments.Count == 1 && !job.RangeSupported, "Expected single connection fallback.");
    });
    await Check("changed ETag restarts safely", async () =>
    {
        using var server = new FileServer(original) { Slow = true };
        using var engine = new DownloadEngine(server);
        var job = Job("changed");
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(_ => cancel.Cancel()), cancel.Token);
        Require(job.Status == "Paused" && PartialBytes(job) > 0, "Expected partial bytes.");
        byte[] replacement = original.Select(b => (byte)(b ^ 0xAA)).ToArray();
        server.Data = replacement;
        server.Tag = "\"v2\"";
        server.Slow = false;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, replacement);
    });
    await Check("malformed range response is rejected", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { BadRange = true });
        var job = Job("bad-range");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.Status == "Failed" && !File.Exists(job.Destination), "Invalid range must not produce a final file.");
    });
    await Check("truncated response is rejected", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { Truncate = true }, new EngineOptions { MaximumRetries = 0 });
        var job = Job("truncated");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.Status == "Failed" && !File.Exists(job.Destination), "Truncated data must not be marked complete.");
    });
    await Check("existing destination is never overwritten", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("existing");
        File.WriteAllText(job.Destination, "keep me");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.Status == "Failed" && File.ReadAllText(job.Destination) == "keep me", "Existing file was overwritten.");
    });
    await Check("unknown file length downloads correctly", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { SupportsRange = false, UnknownLength = true });
        var job = Job("unknown");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
    });
    await Check("optional full check detects changed saved bytes without a validator", async () =>
    {
        using var server = new FileServer(original) { Tag = null, Slow = true };
        using var engine = new DownloadEngine(server);
        engine.Options.VerifySavedBytesWithoutValidator = true;
        var job = Job("no-validator");
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(_ => cancel.Cancel()), cancel.Token);
        Require(job.Status == "Paused" && job.RangeSupported, "Byte ranges should work without a validator.");
        server.Data = original.Select(b => (byte)(b ^ 0x33)).ToArray();
        server.Slow = false;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, server.Data);
        Require(job.ResumeNote.Contains("no longer match"), "Restart must explain the changed bytes.");
    });
    await Check("without a validator, unchanged bytes resume from the saved offsets", async () =>
    {
        using var server = new FileServer(original) { Tag = null, Slow = true };
        using var engine = new DownloadEngine(server);
        var job = Job("no-validator-resume");
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(_ => cancel.Cancel()), cancel.Token);
        Require(job.State == DownloadState.Paused && PartialBytes(job) > 0, "Expected partial bytes.");
        job = JsonSerializer.Deserialize<DownloadJob>(JsonSerializer.Serialize(job))!;
        var saved = job.Segments.Select((part, index) => (part.Start, Bytes: new FileInfo(job.PartPath(index)).Length, part.End)).ToArray();
        server.Requests.Clear();
        long servedBeforeResume = server.Served;
        server.Slow = false;
        long firstProgress = -1;
        await engine.DownloadAsync(job, new ImmediateProgress(p => { if (firstProgress < 0) firstProgress = p.Bytes; }), CancellationToken.None);
        Complete(job, original);
        Require(firstProgress == saved.Sum(part => part.Bytes), "Resume progress must start at the paused position.");
        var newRequests = server.Requests.ToArray();
        foreach (var part in saved.Where(part => part.Bytes > 0 && part.Start + part.Bytes <= part.End))
            Require(newRequests.Contains($"bytes={part.Start + part.Bytes}-{part.End}"), "Transfer must start after saved bytes.");
        Require(server.Served - servedBeforeResume <= original.Length - saved.Sum(part => part.Bytes) + 1, "Resume reread previously downloaded data.");
        Require(job.ResumeNote.Contains("without rereading"), "Expected fast resume.");
    });
    await Check("legacy single-part jobs without validators retain their saved prefix", async () =>
    {
        using var server = new FileServer(original) { Tag = null };
        using var engine = new DownloadEngine(server);
        var job = Job("legacy-no-validator");
        job.RangeSupported = false; job.TotalBytes = original.Length;
        int saved = 400000;
        job.Segments.Add(new() { Start = 0, CommittedBytes = saved });
        File.WriteAllBytes(job.PartPath(0), original[..saved]);
        long first = -1;
        await engine.DownloadAsync(job, new ImmediateProgress(p => { if (first < 0) first = p.Bytes; }), CancellationToken.None);
        Complete(job, original);
        Require(first == saved, "Legacy job resumed at zero.");
        Require(server.Requests.Contains($"bytes={saved}-{original.Length - 1}"), "Expected append request after the existing prefix.");
    });
    await Check("pause during validatorless verification keeps every saved part", async () =>
    {
        using var server = new FileServer(original) { Tag = null };
        using var engine = new DownloadEngine(server);
        engine.Options.VerifySavedBytesWithoutValidator = true;
        var job = Job("pause-verification");
        job.TotalBytes = original.Length;
        job.Segments.Add(new() { Start = 0, CommittedBytes = 400000 });
        File.WriteAllBytes(job.PartPath(0), original[..400000]);
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(_ => cancel.Cancel()), cancel.Token);
        Require(job.State == DownloadState.Paused && PartialBytes(job) == 400000, "Verification pause destroyed saved bytes.");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.ResumeNote.Contains("Continuing"), "Retry after verification pause restarted instead of resuming.");
    });
    await Check("resume at 34 percent requests only the remaining 66 percent", async () =>
    {
        using var server = new FileServer(original) { Tag = null };
        using var engine = new DownloadEngine(server);
        var job = Job("resume-34-percent");
        job.RequestedConnections = 1;
        int saved = original.Length * 34 / 100;
        job.TotalBytes = original.Length; job.RangeSupported = true;
        job.Segments.Add(new() { Start = 0, End = original.Length - 1, CommittedBytes = saved });
        File.WriteAllBytes(job.PartPath(0), original[..saved]);
        var values = new List<DownloadProgress>();
        await engine.DownloadAsync(job, new ImmediateProgress(values.Add), CancellationToken.None);
        Complete(job, original);
        Require(values[0].Bytes == saved, "Progress did not restart at 34 percent.");
        Require(server.Served == original.Length - saved, "Saved 34 percent was downloaded again.");
        Require(server.Requests.All(value => value == "bytes=0-0" || value == $"bytes={saved}-{original.Length - 1}"), "Unexpected verification range requested.");
    });
    await Check("full resume verification is opt-in and persists in SQLite settings", () =>
    {
        Require(!new EngineOptions().VerifySavedBytesWithoutValidator && !new DownloadSettings().VerifySavedBytesWithoutValidator, "Default resume must avoid rechecking saved data.");
        var store = new SqliteDownloadStateStore(Path.Combine(root, "resume-settings.db"));
        var snapshot = store.Load(); snapshot.Settings.VerifySavedBytesWithoutValidator = true;
        store.Save(snapshot);
        Require(store.Load().Settings.VerifySavedBytesWithoutValidator, "Explicit full-check preference was lost.");
        return Task.CompletedTask;
    });
    await Check("server filename replaces opaque URL while preserving bytes", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { FileName = "relinker-v0.1.1.exe" });
        var job = Job(Guid.NewGuid().ToString());
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.FileName == "relinker-v0.1.1.exe", "Server filename was ignored.");
    });
    await Check("server filenames cannot escape the target folder or overwrite files", async () =>
    {
        string existing = Path.Combine(root, "keep.zip");
        File.WriteAllText(existing, "keep");
        using var engine = new DownloadEngine(new FileServer(original) { FileName = "../keep.zip" });
        var job = Job("opaque");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.FileName == "keep (1).zip" && File.ReadAllText(existing) == "keep", "Filename collision or traversal protection failed.");
    });
    await Check("custom filenames are preserved", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { FileName = "server-name.zip" });
        var job = Job("my-name"); job.NameIsCustom = true;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.FileName == "my-name.bin", "Custom filename was replaced.");
    });
    await Check("UTF-8 filename header is preferred", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { FileNameStar = "téléchargement.zip" });
        var job = Job("unicode-name");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.FileName == "téléchargement.zip", "UTF-8 filename was not preserved.");
    });
    await Check("temporary server failure retries and succeeds", async () =>
    {
        using var server = new FileServer(original) { FailuresRemaining = 2 };
        using var engine = new DownloadEngine(server, new EngineOptions { RetryBaseDelay = TimeSpan.Zero });
        var job = Job("retry-success");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.RetriesPerformed == 2 && job.ErrorKind == DownloadErrorKind.None, "Retry state was not cleared correctly.");
    });
    await Check("authentication failure is not retried", async () =>
    {
        using var server = new FileServer(original) { FailuresRemaining = 10, FailureStatus = HttpStatusCode.Unauthorized };
        using var engine = new DownloadEngine(server, new EngineOptions { RetryBaseDelay = TimeSpan.Zero });
        var job = Job("auth-required");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.AuthRequired && job.ErrorKind == DownloadErrorKind.Authentication && server.FailuresRemaining == 9, "Authentication error was retried or misclassified.");
    });
    await Check("pausing during backoff prevents another attempt", async () =>
    {
        using var server = new FileServer(original) { FailuresRemaining = 10 };
        using var engine = new DownloadEngine(server);
        var job = Job("cancel-backoff");
        using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(_ => { if (job.State == DownloadState.Retrying) cancel.Cancel(); }), cancel.Token);
        Require(job.State == DownloadState.Paused && server.FailuresRemaining == 9 && job.NextRetryAt == null, "Cancelled retry sent another request.");
    });
    await Check("valid SHA-256 is verified before completion", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("hash-valid"); job.ExpectedSha256 = Convert.ToHexString(SHA256.HashData(original));
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.IntegrityVerified && job.ActualSha256!.Equals(job.ExpectedSha256, StringComparison.OrdinalIgnoreCase), "Checksum was not verified.");
    });
    await Check("invalid SHA-256 never publishes the destination", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("hash-wrong"); job.ExpectedSha256 = new string('0', 64);
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.Failed && job.ErrorKind == DownloadErrorKind.Integrity && !File.Exists(job.Destination), "Bad hash was published.");
    });
    await Check("metadata checkpoint failure prevents writing segments", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("checkpoint-failure");
        await engine.DownloadAsync(job, null, CancellationToken.None, checkpoint: () => { Require(job.Segments.Count == 4 && job.ETag != null, "Checkpoint ran before metadata preparation."); throw new IOException("simulated state-store failure"); });
        Require(job.State == DownloadState.Failed && PartialBytes(job) == 0 && !File.Exists(job.Destination), "Transfer started without its checkpoint.");
    });
    await Check("unrecorded fragments are discarded instead of appended", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("orphan");
        File.WriteAllBytes(job.PartPath(0), new byte[100]);
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
    });
    await Check("legacy history migrates with settings and good backup recovery", () =>
    {
        string history = Path.Combine(root, "state.json");
        var old = Job("stored"); old.CreatedAt = null;
        File.WriteAllText(history, JsonSerializer.Serialize(new[] { old }));
        var store = new DownloadStateStore(history);
        var snapshot = store.Load();
        Require(snapshot.Downloads.Single().Id == old.Id, "Legacy task was lost.");
        snapshot.Settings.DownloadDirectory = root;
        snapshot.Settings.MaximumActiveDownloads = 2;
        store.Save(snapshot); store.Save(snapshot);
        File.WriteAllText(history, "{corrupt");
        var recovered = store.Load();
        Require(store.RecoveredFromBackup && recovered.Settings.MaximumActiveDownloads == 2 && recovered.Downloads.Count == 1, "Backup recovery failed.");
        store.Save(recovered);
        Require(Directory.GetFiles(root, "state.json.corrupt-*").Length == 1 && store.Load().Downloads.Count == 1, "Corrupt history was not preserved.");
        return Task.CompletedTask;
    });
    await Check("unreadable or future history is preserved", () =>
    {
        string bad = Path.Combine(root, "unreadable.json"); File.WriteAllText(bad, "bad");
        try { new DownloadStateStore(bad).Load(); throw new Exception("Corrupt history was accepted."); } catch (InvalidDataException) { }
        Require(File.ReadAllText(bad) == "bad", "Corrupt history was changed.");
        string future = Path.Combine(root, "future.json"); File.WriteAllText(future, "{\"SchemaVersion\":999}");
        try { new DownloadStateStore(future).Load(); throw new Exception("Future version was accepted."); } catch (NotSupportedException) { }
        return Task.CompletedTask;
    });
    await Check("queue bounds concurrency and pauses active and queued tasks", async () =>
    {
        var jobs = Enumerable.Range(0, 5).Select(i => Job("queue-" + i)).ToArray();
        var started = jobs.ToDictionary(j => j.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var release = jobs.ToDictionary(j => j.Id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        int active = 0, peak = 0;
        var scheduler = new DownloadScheduler(async (job, token) =>
        {
            int count = Interlocked.Increment(ref active);
            InterlockedExtensions.Maximum(ref peak, count);
            started[job.Id].SetResult();
            try { await release[job.Id].Task.WaitAsync(token); job.State = DownloadState.Completed; }
            finally { Interlocked.Decrement(ref active); }
        }, () => 2);
        foreach (var job in jobs) scheduler.Enqueue(job);
        await Task.WhenAll(started[jobs[0].Id].Task, started[jobs[1].Id].Task).WaitAsync(TimeSpan.FromSeconds(5));
        Require(!started[jobs[2].Id].Task.IsCompleted && jobs[2].State == DownloadState.Queued, "Queue exceeded its limit.");
        release[jobs[0].Id].SetResult();
        await started[jobs[2].Id].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(!started[jobs[3].Id].Task.IsCompleted, "FIFO was not respected.");
        await scheduler.ShutdownAsync();
        Require(peak <= 2 && scheduler.ActiveCount == 0 && jobs.Skip(1).All(j => j.State == DownloadState.Paused), "Shutdown did not pause all work safely.");
    });
    await Check("bytes beyond a saved durable checkpoint are discarded", async () =>
    {
        using var server = new FileServer(original);
        using var engine = new DownloadEngine(server);
        var job = Job("saved-tail");
        job.TotalBytes = original.Length; job.ETag = "\"v1\""; job.RangeSupported = true;
        long chunk = original.Length / 4;
        for (int i = 0; i < 4; i++) job.Segments.Add(new() { Start = i * chunk, End = i == 3 ? original.Length - 1 : (i + 1) * chunk - 1, CommittedBytes = i == 0 ? 1024 : 0 });
        File.WriteAllBytes(job.PartPath(0), original[..1024].Concat(new byte[100]).ToArray());
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(server.Served == original.Length - 1024, "Saved checkpoint was not used.");
    });
    await Check("sixteen connections produce the same file", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original));
        var job = Job("sixteen"); job.RequestedConnections = 16;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.Segments.Count == 16, "Requested connection count was ignored.");
    });
    await Check("queue retry limit overrides the global retry limit for its file", async () =>
    {
        using var server = new FileServer(original) { FailuresRemaining = 10 };
        using var engine = new DownloadEngine(server,new EngineOptions { MaximumRetries = 5, RetryBaseDelay = TimeSpan.Zero });
        var job = Job("queue-retry-limit"); job.MaximumRetries = 1;
        await engine.DownloadAsync(job,null,CancellationToken.None);
        Require(job.State == DownloadState.Failed && job.RetriesPerformed == 1 && server.FailuresRemaining == 8,"Per-queue retries were ignored.");
    });
    await Check("retry count is bounded", async () =>
    {
        using var server = new FileServer(original) { FailuresRemaining = 10 };
        using var engine = new DownloadEngine(server, new EngineOptions { MaximumRetries = 2, RetryBaseDelay = TimeSpan.Zero });
        var job = Job("retry-bound");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.Failed && job.RetriesPerformed == 2 && server.FailuresRemaining == 7 && job.HttpStatus == 503, "Retry limit was not enforced.");
    });
    await Check("embedded credentials are rejected before sending requests", async () =>
    {
        using var server = new FileServer(original);
        using var engine = new DownloadEngine(server);
        var job = Job("credentials"); job.Url = "https://user:secret@example.test/file";
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.Failed && server.Requests.Count == 0 && !job.Error.Contains("secret"), "Embedded credentials were sent or exposed.");
    });
    await Check("insufficient disk space is detected before segment transfers", async () =>
    {
        using var server = new FileServer(original) { ProbeLengthOverride = long.MaxValue };
        using var engine = new DownloadEngine(server);
        var job = Job("disk-space");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.NoDiskSpace && job.ErrorKind == DownloadErrorKind.DiskSpace && server.Requests.Count == 1 && PartialBytes(job) == 0, "Disk-space failure started a transfer or was misclassified.");
    });
    await Check("idle connections dynamically split slow segments without gaps", async () =>
    {
        byte[] large = new byte[24 * 1024 * 1024]; new Random(77).NextBytes(large);
        using var server = new FileServer(large) { SlowSegmentStart = 18 * 1024 * 1024 };
        using var engine = new DownloadEngine(server);
        var job = Job("dynamic");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, large);
        Require(job.DynamicSplits > 0 && job.Segments.Count > 4, "No dynamic rebalancing occurred.");
    });
    await Check("dynamically split geometry survives pause and resume", async () =>
    {
        byte[] large = new byte[24 * 1024 * 1024]; new Random(88).NextBytes(large);
        using var server = new FileServer(large) { SlowSegmentStart = 18 * 1024 * 1024 };
        using var engine = new DownloadEngine(server);
        var job = Job("dynamic-resume"); using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, null, cancel.Token, checkpoint: () => { if (job.DynamicSplits > 0) cancel.Cancel(); });
        Require(job.State == DownloadState.Paused && job.DynamicSplits > 0, "Pause did not occur after a split.");
        job = JsonSerializer.Deserialize<DownloadJob>(JsonSerializer.Serialize(job))!;
        server.SlowSegmentStart = null;
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, large);
    });
    await Check("authenticated ranged downloads use their own cookies and authorization", async () =>
    {
        using var server = new FileServer(original) { RequiredCookie = "sid=test-session-only", RequiredAuthorization = "Bearer test-token-only" };
        using var engine = new DownloadEngine(server);
        var job = Job("authenticated");
        job.Session = new() { Origin = "https://test.invalid", Authorization = "Bearer test-token-only", UserAgent = "TestBrowser/1", Referer = "https://test.invalid/account", Cookies = [new() { Name = "sid", Value = "test-session-only", Domain = "test.invalid", Secure = true }] };
        await engine.DownloadAsync(job, null, CancellationToken.None); Complete(job, original);
        var anonymous = Job("anonymous");
        await engine.DownloadAsync(anonymous, null, CancellationToken.None);
        Require(anonymous.State == DownloadState.AuthRequired, "Sessions leaked between jobs.");
    });
    await Check("redirects do not leak cookies or authorization to other hosts", async () =>
    {
        using var server = new FileServer(original) { RedirectFrom = "secure.test", RedirectTo = "https://elsewhere.test/file.bin" };
        using var engine = new DownloadEngine(server);
        var job = Job("redirect"); job.Url = "https://secure.test/file.bin";
        job.Session = new() { Origin = "https://secure.test", Authorization = "Basic dGVzdDp0ZXN0", Cookies = [new() { Name = "sid", Value = "never-forward", Domain = "secure.test" }] };
        await engine.DownloadAsync(job, null, CancellationToken.None); Complete(job, original);
        Require(server.Headers.Where(value => value.Host == "elsewhere.test").All(value => value.Cookie == "" && value.Authorization == ""), "Redirect leaked session headers.");
        Require(job.FinalUrl == "https://elsewhere.test/file.bin", "Final URL was not recorded.");
    });
    await Check("SQLite migrates legacy state and encrypts sessions", () =>
    {
        string legacy = Path.Combine(root, "migration.json"), database = Path.Combine(root, "migration.db");
        var job = Job("sqlite-auth");
        File.WriteAllText(legacy, JsonSerializer.Serialize(new[] { job }));
        var store = new SqliteDownloadStateStore(database, legacy);
        var snapshot = store.Load();
        Require(snapshot.Downloads.Single().Id == job.Id && File.Exists(legacy + ".pre-sqlite"), "Legacy migration failed.");
        snapshot.Downloads[0].Session = new() { Origin = "https://test.invalid", Cookies = [new() { Name = "sid", Value = "ENCRYPTED-COOKIE-TEST-ONLY-5199", Domain = "test.invalid" }] };
        snapshot.Downloads[0].Segments.Add(new() { Start = 0, End = 100, CommittedBytes = 10 });
        store.Save(snapshot);
        store.CreateBackup();
        Require(store.BackupError == null && File.Exists(Path.ChangeExtension(database, ".backup.db")), "SQLite backup failed.");
        var restored = new SqliteDownloadStateStore(database, legacy).Load();
        Require(restored.Downloads[0].Session!.Cookies[0].Value == "ENCRYPTED-COOKIE-TEST-ONLY-5199" && restored.Downloads[0].Segments[0].CommittedBytes == 10, "Encrypted session/segment roundtrip failed.");
        foreach (var file in Directory.GetFiles(root, "migration.db*")) Require(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("ENCRYPTED-COOKIE-TEST-ONLY-5199"), "Plaintext cookie was stored on disk.");
        Require(new SqliteDownloadStateStore(Path.ChangeExtension(database, ".backup.db")).Load().Downloads[0].Session!.Cookies[0].Value == "ENCRYPTED-COOKIE-TEST-ONLY-5199", "Backup did not restore the protected session.");
        return Task.CompletedTask;
    });
    await Check("SQLite rolls back an entire failed save", () =>
    {
        string database = Path.Combine(root, "rollback.db"); var store = new SqliteDownloadStateStore(database);
        var job = Job("rollback-job"); store.Save(new() { Downloads = [job] });
        job.State = DownloadState.Paused;
        var invalid = Job("invalid-session"); invalid.Session = new() { Origin = "https://wrong.invalid" };
        try { store.Save(new() { Downloads = [job, invalid] }); throw new Exception("Invalid transaction was accepted."); } catch (ArgumentException) { }
        var restored = store.Load(); Require(restored.Downloads.Count == 1 && restored.Downloads[0].State == DownloadState.Ready, "A failed save partially modified the database.");
        return Task.CompletedTask;
    });
    await Check("an HTML login page is not saved as a completed binary", async () =>
    {
        using var server = new FileServer(original) { ReturnLoginPage = true };
        using var engine = new DownloadEngine(server);
        var job = Job("login-page"); job.Session = new() { Origin = "https://test.invalid" };
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.Failed && job.ErrorKind == DownloadErrorKind.Protocol && !File.Exists(job.Destination), "A webpage was mislabeled as an authentication challenge or published as a binary.");
    });
    await Check("metadata reports the full range size without reading file data", async () =>
    {
        using var server = new FileServer(original);
        using var engine = new DownloadEngine(server);
        var job = Job("metadata-size");
        var metadata = await engine.ProbeAsync(job, CancellationToken.None);
        Require(metadata.TotalBytes == original.Length && metadata.RangeSupported, "The one-byte Content-Length was used as file size.");
        Require(server.Served == 0 && !File.Exists(job.Destination), "Metadata probing downloaded the file body.");
    });
    await Check("unknown complete range length never becomes a one-byte file", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { UnknownRangeLength = true });
        var metadata = await engine.ProbeAsync(Job("unknown-range-size"), CancellationToken.None);
        Require(metadata.TotalBytes == null && !metadata.RangeSupported, "Unknown total must not use the piece's Content-Length.");
    });
    await Check("hosts rejecting tiny range probes still download through a normal GET", async () =>
    {
        using var server = new FileServer(original) { RejectProbe = true };
        using var engine = new DownloadEngine(server);
        var job = Job("probe-refused");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.TotalBytes == original.Length && server.Served == original.Length, "Fallback downloaded probe data or used an error-page size.");
    });
    await Check("HTTP 403 is access denied, not a password prompt", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original) { FailuresRemaining = 10, FailureStatus = HttpStatusCode.Forbidden });
        var job = Job("forbidden");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Require(job.State == DownloadState.Failed && job.ErrorKind == DownloadErrorKind.AccessDenied && job.HttpStatus == 403, "Forbidden was mislabeled as authentication.");
        Require(job.TotalBytes == null && !File.Exists(job.Destination), "Error response was treated as a file.");
    });
    await Check("refused parallel transfers retry one connection without losing saved parts", async () =>
    {
        using var server = new FileServer(original) { RefuseFirstTransfer = true };
        using var engine = new DownloadEngine(server, new EngineOptions { MaximumRetryDelay = TimeSpan.Zero });
        var job = Job("parallel-refused");
        await engine.DownloadAsync(job, null, CancellationToken.None);
        Complete(job, original);
        Require(job.RequestedConnections == 1 && job.RetriesPerformed == 1, "Server rejection did not reduce parallel connections.");
    });
    await Check("media resolution keeps only cookies belonging to the resolved host", async () =>
    {
        using var engine = new DownloadEngine(new ScriptedHandler(request =>
        {
            if (request.RequestUri!.Host == "www.mp4upload.com") return new(HttpStatusCode.OK) { Content = new StringContent("src: 'https://cdn.example.test/video.mp4'", System.Text.Encoding.UTF8, "text/html") };
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([42]) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 1000);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return response;
        }));
        var job = Job("resolved-video"); job.Url = "https://www.mp4upload.com/embed-test123.html";
        job.Session = new() { Origin = "https://www.mp4upload.com", Cookies = [new() { Name = "sid", Value = "test-only", Domain = "www.mp4upload.com" }] };
        var metadata = await engine.ProbeAsync(job, CancellationToken.None);
        job.Session!.Validate(job.Url);
        Require(job.Url == "https://cdn.example.test/video.mp4" && job.Session.Cookies.Count == 0 && metadata.TotalBytes == 1000, "Media URL retained an incompatible source session.");
    });
    await Check("pending browser confirmations persist encrypted outside the download list", () =>
    {
        var store = new SqliteDownloadStateStore(Path.Combine(root, "pending.db"));
        var snapshot = store.Load();
        snapshot.PendingDownloads.Add(new() { RequestId = Guid.NewGuid().ToString(), Url = "https://test.invalid/file.zip", FileName = "file.zip", Session = new() { Origin = "https://test.invalid", Cookies = [new() { Name = "sid", Value = "PENDING-SECRET-TEST-97531", Domain = "test.invalid" }] } });
        store.Save(snapshot);
        var loaded = store.Load();
        Require(loaded.Downloads.Count == 0 && loaded.PendingDownloads.Count == 1 && loaded.PendingDownloads[0].Session!.Cookies[0].Value == "PENDING-SECRET-TEST-97531", "Pending handoff became a listed download or lost its session.");
        Require(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "pending.db"))).Contains("PENDING-SECRET-TEST-97531"), "Pending session was stored in plaintext.");
        loaded.PendingDownloads.Clear(); store.Save(loaded);
        Require(store.Load().PendingDownloads.Count == 0 && store.Load().Downloads.Count == 0, "Cancel left a placeholder in history.");
        return Task.CompletedTask;
    });
    await Check("confirming a pending handoff and adding history is one transaction", () =>
    {
        var store = new SqliteDownloadStateStore(Path.Combine(root, "confirm.db"));
        var snapshot = store.Load(); var pending = new PendingDownload { RequestId = Guid.NewGuid().ToString(), Url = "https://test.invalid/file.bin" };
        snapshot.PendingDownloads.Add(pending); store.Save(snapshot);
        snapshot.PendingDownloads.Clear(); var job = Job("confirmed"); job.BrowserRequestId = pending.RequestId;
        job.Session = new() { Origin = "https://wrong.invalid" }; snapshot.Downloads.Add(job);
        try { store.Save(snapshot); throw new Exception("Expected failed promotion."); } catch (ArgumentException) { }
        Require(store.Load().Downloads.Count == 0 && store.Load().PendingDownloads.Count == 1, "A failed confirmation lost the captured request.");
        job.Session = null; store.Save(snapshot);
        Require(store.Load().Downloads.Count == 1 && store.Load().PendingDownloads.Count == 0, "Confirmation did not atomically replace the pending request.");
        return Task.CompletedTask;
    });
    await Check("saved credentials are encrypted and restricted to the exact site", () =>
    {
        string database = Path.Combine(root, "preferences-secret.db"); var store = new SqliteDownloadStateStore(database); var snapshot = store.Load();
        snapshot.Settings.SiteLogins.Add(new() { Origin = "https://login.test/", Username = "test", Password = "SETTINGS-SECRET-991122" });
        snapshot.Settings.ProxyPassword = "PROXY-SECRET-778899"; store.Save(snapshot);
        var settings = SqliteDownloadStateStore.ReadPreferences(database);
        Require(settings.SiteLogins[0].Password == "SETTINGS-SECRET-991122", "Credential did not survive encryption.");
        Require(settings.ManualSession(new Uri("https://login.test/file.zip")).Authorization?.StartsWith("Basic ") == true, "Exact origin login was not applied.");
        Require(settings.ManualSession(new Uri("https://sub.login.test/file.zip")).Authorization == null, "Credentials leaked to another origin.");
        Require(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(database)).Contains("SETTINGS-SECRET-991122"), "Saved password is plaintext.");
        return Task.CompletedTask;
    });
    await Check("nested batch ranges obey a total cap", () =>
    {
        Require(BatchUrlExpander.Expand("https://test.invalid/file[01-03]-[1-2].zip").Count == 6, "Nested expansion failed.");
        try { BatchUrlExpander.Expand("https://test.invalid/[1-100]-[1-100].zip"); throw new Exception("Expected batch cap rejection."); } catch (ArgumentException) { }
        return Task.CompletedTask;
    });
    await Check("custom category extensions and folders apply to new files", () =>
    {
        var settings = new DownloadSettings { DownloadDirectory = root };
        settings.CategoryExtensions["Design"] = "psd .blend"; settings.CategoryDirectories["Design"] = Path.Combine(root, "design");
        Require(settings.CategoryForFile("model.BLEND") == "Design" && settings.GetDirectoryForCategory("Design") == Path.Combine(root,"design"), "Custom category was ignored.");
        return Task.CompletedTask;
    });
    await Check("temporary parts survive destination rename and complete correctly", async () =>
    {
        using var server = new FileServer(original) { Slow = true }; using var engine = new DownloadEngine(server);
        var job = Job("temporary"); job.TemporaryDirectory = Path.Combine(root,"parts"); using var cancel = new CancellationTokenSource();
        await engine.DownloadAsync(job, new ImmediateProgress(p => { if (p.Bytes > 0) cancel.Cancel(); }), cancel.Token);
        long saved = PartialBytes(job); job.Destination = Path.Combine(root,"renamed-temporary.bin"); server.Slow = false; long before = server.Served;
        await engine.DownloadAsync(job,null,CancellationToken.None); Complete(job,original);
        Require(server.Served - before <= original.Length - saved + 1, "Temporary folder lost partial bytes after rename.");
    });
    await Check("per-file limiter does not modify the global limiter", async () =>
    {
        using var engine = new DownloadEngine(new FileServer(original)); engine.Options.GlobalLimiter.BytesPerSecond = 9_000_000;
        var job = Job("file-limit"); job.SpeedLimitBytes = 8_000_000;
        await engine.DownloadAsync(job,null,CancellationToken.None); Complete(job,original);
        Require(engine.Options.GlobalLimiter.BytesPerSecond == 9_000_000 && job.FileLimiter.BytesPerSecond == 8_000_000, "Per-file and global limits were mixed.");
    });
    await Check("named queue slots do not block other queues", async () =>
    {
        var scheduler = new DownloadScheduler(async (job, token) => await Task.Delay(Timeout.Infinite,token), () => 3, _ => 1);
        var a = Job("queue-a"); a.Queue = "A"; var a2 = Job("queue-a2"); a2.Queue = "A"; var b = Job("queue-b"); b.Queue = "B";
        scheduler.Enqueue(a); scheduler.Enqueue(a2); scheduler.Enqueue(b);
        Require(scheduler.ActiveCount == 2 && a2.State == DownloadState.Queued, "Per-queue capacity did not let another queue run.");
        scheduler.PauseQueue("A"); await scheduler.ShutdownAsync();
        Require(a.State == DownloadState.Paused && a2.State == DownloadState.Paused, "Queue pause lost its state.");
    });
    await Check("queue reordering preserves other queues and paused queues restart in order", async () =>
    {
        var started = new List<string>();
        var scheduler = new DownloadScheduler((job, token) => { started.Add(job.FileName); return Task.Delay(Timeout.Infinite,token); }, () => 1);
        var blocker = Job("blocker"); var a = Job("order-a"); var b = Job("order-b"); var c = Job("other-c"); c.Queue = "Other";
        scheduler.Enqueue(blocker); scheduler.Enqueue(a); scheduler.Enqueue(c); scheduler.Enqueue(b);
        scheduler.ReorderQueue("Default",[b.Id,a.Id]); scheduler.Pause(blocker);
        await Task.Delay(50);
        Require(scheduler.IsRunning(b) && a.State == DownloadState.Queued && c.State == DownloadState.Queued,"Reordered queue did not start its first file.");
        scheduler.PauseQueue("Default"); await Task.Delay(50);
        Require(scheduler.IsRunning(c) && a.State == DownloadState.Paused,"A stopped queue blocked the other queue.");
        await scheduler.ShutdownAsync();
    });
    await Check("queue schedules order and completion preferences persist in SQLite", () =>
    {
        var settings = new DownloadSettings { DownloadDirectory = root };
        var queue = settings.Queues[0]; queue.StartOnStartup = true; queue.StartTime = "08:30"; queue.StopTime = "18:00"; queue.Weekdays = [DayOfWeek.Monday,DayOfWeek.Friday]; queue.ScheduledDate = new DateTime(2026,10,20); queue.FileOrder = ["one","two"]; queue.Retries = 7; queue.ExitWhenDone = true;
        settings.Validate(); var store = new SqliteDownloadStateStore(Path.Combine(root,"queue-settings.db")); store.Save(new() { Settings = settings }); var saved = store.Load().Settings;
        Require(saved.Queues[0].DisplayName == "Main download queue" && saved.Queues[1].Synchronization && saved.Queues[0].StartOnStartup && saved.Queues[0].Retries == 7 && saved.Queues[0].FileOrder.SequenceEqual(queue.FileOrder) && saved.Queues[0].Weekdays.SequenceEqual(queue.Weekdays) && saved.Queues[0].ScheduledDate == queue.ScheduledDate && saved.Queues[0].ExitWhenDone,"Queue preferences were lost.");
        queue.Retries = 11; try { settings.Validate(); throw new Exception("Invalid retry count accepted."); } catch (ArgumentException) { }
        return Task.CompletedTask;
    });
    await Check("remote ZIP preview reads valid directory metadata", async () =>
    {
        using var memory = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Create,true)) { using var writer = new StreamWriter(archive.CreateEntry("hello.txt").Open()); writer.Write("hello"); }
        byte[] zip = memory.ToArray();
        var handler = new ScriptedHandler(request =>
        {
            if (request.Method == HttpMethod.Head) { var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }; head.Content.Headers.ContentLength = zip.Length; return head; }
            var range = request.Headers.Range!.Ranges.Single(); int start = (int)range.From!.Value, end = (int)range.To!.Value;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(zip[start..(end+1)]) }; response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start,end,zip.Length); return response;
        });
        var entries = await RemoteZipInspector.InspectAsync("https://test.invalid/archive.zip",handler: handler);
        Require(entries.Count == 1 && entries[0].Name == "hello.txt" && entries[0].UncompressedSize == 5, "ZIP preview returned wrong entries.");
    });
    await Check("ZIP preview refuses a full response without reading its body", async () =>
    {
        long read = 0;
        var handler = new ScriptedHandler(request =>
        {
            if (request.Method == HttpMethod.Head) { var head = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }; head.Content.Headers.ContentLength = 10_000_000; return head; }
            return new(HttpStatusCode.OK) { Content = new StreamContent(new SimulatedStream(new byte[1000],0,1000,false, count => read += count)) };
        });
        try { await RemoteZipInspector.InspectAsync("https://test.invalid/large.zip",handler: handler); throw new Exception("Expected range refusal."); } catch (InvalidOperationException) { }
        Require(read == 0,"ZIP preview consumed file data from a range-refusing server.");
    });
    await Check("bandwidth pacing does not truncate long reservations", async () =>
    {
        var limiter = new BandwidthLimiter(10_000); var clock = System.Diagnostics.Stopwatch.StartNew();
        await limiter.ThrottleAsync(80_000);
        Require(clock.Elapsed.TotalSeconds >= 6.6, "A seven-second reservation was truncated.");
        limiter.BytesPerSecond = 1; using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await limiter.ThrottleAsync(100_000,cancel.Token); throw new Exception("Expected cancelled throttle."); } catch (OperationCanceledException) { }
    });
    await Check("server dates are applied before publishing the file", async () =>
    {
        var date = new DateTimeOffset(2024,1,2,12,30,0,TimeSpan.Zero);
        using var engine = new DownloadEngine(new FileServer(original) { Modified = date }, new EngineOptions { PreserveServerDate = true });
        var job = Job("server-dates"); await engine.DownloadAsync(job,null,CancellationToken.None); Complete(job,original);
        Require(Math.Abs((File.GetLastWriteTimeUtc(job.Destination)-date.UtcDateTime).TotalSeconds) < 1 && Math.Abs((File.GetCreationTimeUtc(job.Destination)-date.UtcDateTime).TotalSeconds) < 1,"Server file dates were not preserved.");
    });
    await Check("invalid font values are rejected before saving preferences", () =>
    {
        try { new DownloadSettings { FontSize = double.NaN }.Validate(); throw new Exception("Expected font size rejection."); } catch (ArgumentException) { }
        try { new DownloadSettings { FontFamily = "" }.Validate(); throw new Exception("Expected font name rejection."); } catch (ArgumentException) { }
        return Task.CompletedTask;
    });
    await Check("pausing all jobs for completion actions still allows later resume", async () =>
    {
        var scheduler = new DownloadScheduler(async (job,token) => await Task.Delay(Timeout.Infinite,token),() => 1);
        var job = Job("pause-all-resume"); scheduler.Enqueue(job); await scheduler.PauseAllAsync();
        Require(job.State == DownloadState.Paused && scheduler.ActiveCount == 0,"Pause-all did not wait for jobs to finish pausing.");
        scheduler.Enqueue(job); Require(scheduler.IsRunning(job),"Pause-all permanently stopped the scheduler."); await scheduler.ShutdownAsync();
    });
    await Check("download descriptions persist in protected history", () =>
    {
        var store = new SqliteDownloadStateStore(Path.Combine(root,"description.db")); var snapshot = store.Load(); var job = Job("description"); job.Description = "Ressources graphiques — édition 2026"; snapshot.Downloads.Add(job); store.Save(snapshot);
        Require(store.Load().Downloads.Single().Description == job.Description,"Description was lost on restart."); return Task.CompletedTask;
    });
    await Check("update feeds install only with a valid signature over version, link and checksum", () =>
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        string original = AppUpdates.PublicKey;
        try
        {
            AppUpdates.PublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
            string sha = new string('a', 64), url = "https://example.test/Setup-9.0.0.exe";
            string Feed(string version, string link, string hash, string signature) => System.Text.Json.JsonSerializer.Serialize(new { version, url = link, sha256 = hash, signature });
            string sign(string version, string link, string hash) => Convert.ToBase64String(key.SignData(AppUpdates.SignedPayload(version, link, hash), System.Security.Cryptography.HashAlgorithmName.SHA256));
            var good = AppUpdates.Parse(System.Text.Encoding.UTF8.GetBytes(Feed("9.0.0", url, sha, sign("9.0.0", url, sha))));
            Require(good.Signed && good.Version == new Version(9,0,0) && good.Sha256 == sha, "A correctly signed release was not accepted.");
            foreach (var tampered in new[] { Feed("9.0.1", url, sha, sign("9.0.0", url, sha)), Feed("9.0.0", "https://evil.test/x.exe", sha, sign("9.0.0", url, sha)), Feed("9.0.0", url, new string('b', 64), sign("9.0.0", url, sha)), Feed("9.0.0", url, sha, "bm90IGEgc2lnbmF0dXJl") })
            {
                try { AppUpdates.Parse(System.Text.Encoding.UTF8.GetBytes(tampered)); throw new Exception("A tampered release was accepted."); }
                catch (InvalidDataException) { }
            }
            var unsigned = AppUpdates.Parse("""{"version":"9.0.0","downloadUrl":"https://example.test/page"}"""u8.ToArray());
            Require(!unsigned.Signed, "An unsigned feed was marked installable.");
            try { AppUpdates.DownloadInstallerAsync(new HttpClient(), unsigned, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("An unsigned release was downloaded for installation."); }
            catch (InvalidOperationException) { }
            try { AppUpdates.Parse("""{"version":"9.0.0","url":"http://example.test/x.exe"}"""u8.ToArray()); throw new Exception("A plain-HTTP release was accepted."); }
            catch (InvalidDataException) { }
        }
        finally { AppUpdates.PublicKey = original; }
        return Task.CompletedTask;
    });
    await Check("an updated yt-dlp is used only when it is newer than the bundled copy", () =>
    {
        Require(YtDlpUpdater.ParseVersion("2026.10.30") == new Version(2026,10,30,0) && YtDlpUpdater.ParseVersion("2026.10.30.1") > YtDlpUpdater.ParseVersion("2026.10.30") && YtDlpUpdater.ParseVersion("latest") == null, "yt-dlp versions parsed incorrectly.");
        string bundled = Path.Combine(root, "bundled-tools"), updates = Path.Combine(root, "updated-tools");
        Directory.CreateDirectory(bundled); Directory.CreateDirectory(updates);
        File.WriteAllText(Path.Combine(bundled, "sources.json"), """{"ytDlp":{"version":"2026.08.19"}}""");
        Require(YtDlpUpdater.PreferredExecutable(bundled, updates) == null, "A missing update was preferred.");
        File.WriteAllText(Path.Combine(updates, "yt-dlp.exe"), "fixture"); File.WriteAllText(Path.Combine(updates, "yt-dlp.version"), "2026.08.01");
        Require(YtDlpUpdater.PreferredExecutable(bundled, updates) == null, "An older update replaced the bundled copy.");
        File.WriteAllText(Path.Combine(updates, "yt-dlp.version"), "2026.10.30");
        Require(YtDlpUpdater.PreferredExecutable(bundled, updates) == Path.Combine(updates, "yt-dlp.exe"), "A newer verified update was not used.");
        return Task.CompletedTask;
    });
    Console.WriteLine($"All {passed} integration checks passed.");
}
finally
{
    // This directory was created above, exclusively for this test run.
    Directory.Delete(root, true);
}

DownloadJob Job(string name) => new() { Url = "https://test.invalid/file.bin", Destination = Path.Combine(root, name + ".bin") };
long PartialBytes(DownloadJob job) => Enumerable.Range(0, job.Segments.Count).Sum(i => File.Exists(job.PartPath(i)) ? new FileInfo(job.PartPath(i)).Length : 0);
async Task Check(string name, Func<Task> test)
{
    await test();
    passed++;
    Console.WriteLine("PASS " + name);
}
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Complete(DownloadJob job, byte[] expected)
{
    Require(job.Status == "Completed", "Expected complete: " + job.Status + " " + job.Error);
    Require(SHA256.HashData(File.ReadAllBytes(job.Destination)).SequenceEqual(SHA256.HashData(expected)), "File contents differ.");
    Require(!Enumerable.Range(0, job.Segments.Count).Any(i => File.Exists(job.PartPath(i))), "Completed transfer left segment files.");
}

sealed class ImmediateProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress>
{ public void Report(DownloadProgress value) => action(value); }

sealed class FileServer(byte[] data) : HttpMessageHandler
{
    public byte[] Data { get; set; } = data;
    public bool SupportsRange { get; set; } = true;
    public bool Slow { get; set; }
    public bool BadRange { get; set; }
    public bool Truncate { get; set; }
    public bool UnknownLength { get; set; }
    public bool UnknownRangeLength { get; set; }
    public bool RejectProbe { get; set; }
    public bool RefuseFirstTransfer { get; set; }
    public string? Tag { get; set; } = "\"v1\"";
    public DateTimeOffset? Modified { get; set; }
    public string? FileName { get; set; }
    public string? FileNameStar { get; set; }
    public int FailuresRemaining { get; set; }
    public HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.ServiceUnavailable;
    public long? ProbeLengthOverride { get; set; }
    public long? SlowSegmentStart { get; set; }
    public string? RequiredCookie { get; set; }
    public string? RequiredAuthorization { get; set; }
    public string? RedirectFrom { get; set; }
    public string? RedirectTo { get; set; }
    public bool ReturnLoginPage { get; set; }
    public ConcurrentBag<(string Host, string Cookie, string Authorization)> Headers { get; } = [];
    public ConcurrentBag<string> Requests { get; } = [];
    public long Served;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join("; ", values) : "";
        string authorization = request.Headers.Authorization?.ToString() ?? "";
        Headers.Add((request.RequestUri!.Host, cookie, authorization));
        if (ReturnLoginPage) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>sign in</html>", System.Text.Encoding.UTF8, "text/html") });
        if (request.RequestUri.Host == RedirectFrom) { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new Uri(RedirectTo!); return Task.FromResult(redirect); }
        if (RequiredCookie != null && cookie != RequiredCookie || RequiredAuthorization != null && authorization != RequiredAuthorization) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        if (FailuresRemaining > 0) { FailuresRemaining--; return Task.FromResult(new HttpResponseMessage(FailureStatus)); }
        var range = SupportsRange ? request.Headers.Range?.Ranges.Single() : null;
        Requests.Add(request.Headers.Range?.ToString() ?? "full");
        long from = range?.From ?? 0;
        long to = range?.To ?? Data.Length - 1;
        int count = (int)(to - from + 1);
        bool probe = from == 0 && to == 0;
        if (probe && RejectProbe) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("range refused") });
        if (!probe && RefuseFirstTransfer) { RefuseFirstTransfer = false; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }
        var response = new HttpResponseMessage(range != null ? HttpStatusCode.PartialContent : HttpStatusCode.OK);
        var stream = new SimulatedStream(Data, (int)from, Truncate && !probe ? count / 2 : count, Slow || from == SlowSegmentStart && !probe, n => Interlocked.Add(ref Served, n));
        response.Content = new StreamContent(stream);
        if (FileName != null || FileNameStar != null) response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = FileName, FileNameStar = FileNameStar };
        if (!UnknownLength) response.Content.Headers.ContentLength = count;
        if (range != null) response.Content.Headers.ContentRange = UnknownRangeLength ? new ContentRangeHeaderValue(from, to) : new ContentRangeHeaderValue(BadRange && !probe ? from + 1 : from, to, probe ? ProbeLengthOverride ?? Data.Length : Data.Length);
        if (Tag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(Tag);
        response.Content.Headers.LastModified = Modified;
        return Task.FromResult(response);
    }
}

sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request));
}

static class InterlockedExtensions
{
    public static void Maximum(ref int target, int value)
    {
        int previous;
        do { previous = Volatile.Read(ref target); if (previous >= value) return; } while (Interlocked.CompareExchange(ref target, value, previous) != previous);
    }
}

sealed class SimulatedStream(byte[] data, int offset, int count, bool slow, Action<int> served) : Stream
{
    private int position;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => position; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (slow) await Task.Delay(5, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        int n = Math.Min(Math.Min(buffer.Length, slow ? 16384 : 131072), count - position);
        data.AsMemory(offset + position, n).CopyTo(buffer);
        position += n;
        served(n);
        return n;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
