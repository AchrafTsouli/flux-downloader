using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace SwiftFetch.Core;

public sealed class DownloadEngine : IDisposable
{
    private readonly HttpClient client;
    public EngineOptions Options { get; }
    public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public DownloadEngine(HttpMessageHandler? handler = null, EngineOptions? options = null)
    {
        Options = options ?? new();
        client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            UseCookies = false,
            Proxy = new SettingsProxy(() => Options.Proxy),
            MaxConnectionsPerServer = Options.MaximumConnectionsPerHost,
            ConnectTimeout = TimeSpan.FromSeconds(20)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
    }

    public async Task DownloadAsync(DownloadJob job, IProgress<DownloadProgress>? progress, CancellationToken token, Action<string>? resolveName = null, Action? checkpoint = null)
    {
        job.RetriesPerformed = 0;
        job.StartedAt ??= DateTimeOffset.UtcNow;
        try { job.Session?.Validate(job.Url); }
        catch (ArgumentException) { job.State = DownloadState.Failed; job.ErrorKind = DownloadErrorKind.Authentication; job.Error = "Invalid download session."; return; }
        if (!Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !Guid.TryParseExact(job.Id, "N", out _))
        {
            job.State = DownloadState.Failed; job.ErrorKind = DownloadErrorKind.Protocol;
            job.Error = "Use a valid HTTP/HTTPS URL without embedded credentials and a valid download identifier.";
            return;
        }
        if (job.ExpectedSha256 is { Length: > 0 } expected && (expected.Length != 64 || expected.Any(c => !Uri.IsHexDigit(c))))
        {
            job.State = DownloadState.Failed;
            job.ErrorKind = DownloadErrorKind.Integrity;
            job.Error = "Expected SHA-256 must contain exactly 64 hexadecimal characters.";
            return;
        }
        for (int attempt = 0; ; attempt++)
        {
            if (token.IsCancellationRequested) { job.State = DownloadState.Paused; return; }
            await DownloadAttemptAsync(job, progress, token, resolveName, checkpoint);
            if (!job.Retryable || job.State == DownloadState.Completed || job.State == DownloadState.Paused || attempt >= Math.Clamp(job.MaximumRetries ?? Options.MaximumRetries, 0, 10)) return;
            job.RetriesPerformed = attempt + 1;
            var delay = job.RetryAfter ?? TimeSpan.FromMilliseconds(Options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
            delay = TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 0, Options.MaximumRetryDelay.TotalMilliseconds));
            job.State = DownloadState.Retrying;
            job.NextRetryAt = DateTimeOffset.UtcNow + delay;
            progress?.Report(new(PartialBytes(job), job.TotalBytes, 0, job.Segments.Count));
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { job.State = DownloadState.Paused; return; }
            finally { job.NextRetryAt = null; }
        }
    }

    private async Task DownloadAttemptAsync(DownloadJob job, IProgress<DownloadProgress>? progress, CancellationToken token, Action<string>? resolveName, Action? checkpoint)
    {
        job.Status = "Connecting";
        job.Error = "";
        job.ErrorKind = DownloadErrorKind.None;
        job.Retryable = false;
        job.RetryAfter = null;
        job.IntegrityVerified = false;
        job.ActualSha256 = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
            if (job.TemporaryDirectory is { Length: > 0 }) Directory.CreateDirectory(job.TemporaryDirectory);
            await PrepareAsync(job, token, resolveName, progress);
            checkpoint?.Invoke(); // Persist ranges and validators before creating or appending segment files.
            job.Status = "Downloading";
            long bytes = Enumerable.Range(0, job.Segments.Count).Sum(i => File.Exists(job.PartPath(i)) ? new FileInfo(job.PartPath(i)).Length : 0);
            if (bytes > 0) progress?.Report(new(bytes, job.TotalBytes, 0, 0));
            var stopwatch = Stopwatch.StartNew();
            long lastBytes = bytes;
            double lastTime = 0;
            object progressLock = new();
            void Report(int count)
            {
                lock (progressLock)
                {
                    bytes += count;
                    var now = stopwatch.Elapsed.TotalSeconds;
                    if (now - lastTime < .2) return;
                    progress?.Report(new(bytes, job.TotalBytes, (bytes - lastBytes) / (now - lastTime), ActiveConnections(job)));
                    lastTime = now;
                    lastBytes = bytes;
                }
            }
            await RunSegmentsAsync(job, Report, token, checkpoint);
            token.ThrowIfCancellationRequested();
            job.Status = "Finishing";
            await using (var output = new FileStream(job.MergePath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                foreach (int i in job.Segments.Select((segment, index) => (segment.Start, index)).OrderBy(value => value.Start).Select(value => value.index))
                {
                    await using var input = new FileStream(job.PartPath(i), FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                    await input.CopyToAsync(output, token);
                }
                if (job.TotalBytes is long expected && output.Length != expected)
                    throw new IOException("The downloaded size does not match the server's file size.");
            }
            if (!string.IsNullOrWhiteSpace(job.ExpectedSha256))
            {
                job.State = DownloadState.Verifying;
                await using var verify = new FileStream(job.MergePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                job.ActualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(verify, token)).ToLowerInvariant();
                if (!job.ActualSha256.Equals(job.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new DownloadFailureException("SHA-256 verification failed. The file was not marked complete.", DownloadErrorKind.Integrity);
                job.IntegrityVerified = true;
            }
            if (Options.VirusScanner.Length > 0)
            {
                var scan = new ProcessStartInfo(Options.VirusScanner) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in CommandArguments.Split(Options.VirusScannerArguments)) scan.ArgumentList.Add(argument.Replace("{file}", job.MergePath));
                using var scanner = Process.Start(scan) ?? throw new IOException("Could not start the virus scanner.");
                try { await scanner.WaitForExitAsync(token); }
                catch (OperationCanceledException) { try { scanner.Kill(); } catch { } throw; }
                if (scanner.ExitCode != 0) throw new DownloadFailureException("Virus scanner reported a nonzero exit code. The file was not published.", DownloadErrorKind.Integrity);
            }
            if (Options.PreserveServerDate && job.LastModified is { } date) { File.SetLastWriteTimeUtc(job.MergePath, date.UtcDateTime); File.SetCreationTimeUtc(job.MergePath, date.UtcDateTime); }
            File.Move(job.MergePath, job.Destination, false);
            job.TotalBytes ??= new FileInfo(job.Destination).Length;
            CleanupParts(job);
            job.Status = "Completed";
            job.CompletedAt = DateTimeOffset.UtcNow;
            progress?.Report(new(bytes, job.TotalBytes ?? bytes, 0, 0));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            job.Status = "Paused";
        }
        catch (Exception ex)
        {
            RecordFailure(job, ex);
        }
        finally
        {
            try { if (File.Exists(job.MergePath)) File.Delete(job.MergePath); }
            catch (Exception ex) { RecordFailure(job, ex); }
        }
    }

    private async Task PrepareAsync(DownloadJob job, CancellationToken token, Action<string>? resolveName, IProgress<DownloadProgress>? progress)
    {
        var metadata = await ProbeAsync(job, token);
        job.FinalUrl = metadata.FinalUrl;
        job.ContentType = metadata.ContentType;
        if (job.Segments.Count == 0) CleanupParts(job); // Unrecorded fragments cannot be trusted after a crash.
        string? suggested = metadata.SuggestedFileName;
        bool hasParts = Enumerable.Range(0, job.Segments.Count).Any(i => File.Exists(job.PartPath(i)));
        if (!job.NameIsCustom && !hasParts && suggested != null && !suggested.Equals(job.FileName, StringComparison.OrdinalIgnoreCase))
        {
            if (resolveName != null) resolveName(suggested);
            else job.Destination = FileNames.UniquePath(Path.GetDirectoryName(job.Destination)!, suggested);
        }
        if (job.Category == "Other") job.Category = DownloadCategories.ForFile(job.FileName);
        bool ranged = metadata.RangeSupported;
        long? total = metadata.TotalBytes;
        string? etag = metadata.ETag;
        var modified = metadata.LastModified;
        bool validator = etag != null || modified != null;
        // Older jobs conflated Range support with validator availability. Their
        // single open-ended part can still be verified and resumed by bytes.
        bool legacyRange = !job.RangeSupported && job.ETag == null && job.LastModified == null &&
                           job.Segments.Count == 1 && job.Segments[0].Start == 0 && (job.Segments[0].End == null || job.Segments[0].End == total - 1);
        bool same = job.Segments.Count > 0 && (job.RangeSupported || legacyRange) && ranged && job.TotalBytes == total &&
                    (job.ETag != null ? job.ETag == etag : job.LastModified != null && !Options.IgnoreLastModifiedChanges ? job.LastModified == modified : true);
        job.ResumeNote = ranged ? validator ? "Resume supported by the server." : "Resume supported by byte ranges; the server provides no file validator." : "The server does not support byte ranges; resuming restarts the file.";
        if (same)
        {
            for (int i = 0; i < job.Segments.Count; i++)
            {
                long size = File.Exists(job.PartPath(i)) ? new FileInfo(job.PartPath(i)).Length : 0;
                var segment = job.Segments[i];
                if (segment.End is long end && size > end - segment.Start + 1) { same = false; break; }
            }
        }
        if (same)
        {
            for (int i = 0; i < job.Segments.Count; i++)
            {
                string part = job.PartPath(i);
                long actual = File.Exists(part) ? new FileInfo(part).Length : 0;
                long committed = Math.Min(actual, job.Segments[i].CommittedBytes ?? actual);
                if (committed < 0) throw new DownloadFailureException("Invalid segment checkpoint.", DownloadErrorKind.Protocol);
                if (actual > committed)
                {
                    using var trim = new FileStream(part, FileMode.Open, FileAccess.Write, FileShare.None);
                    trim.SetLength(committed);
                    trim.Flush(true);
                }
                job.Segments[i].CommittedBytes = committed;
            }
            if (legacyRange) job.Segments[0].End = total!.Value - 1;
            if (job.ETag == null && job.LastModified == null && PartialBytes(job) > 0 && Options.VerifySavedBytesWithoutValidator)
            {
                job.State = DownloadState.Verifying;
                job.ResumeNote = "Checking saved bytes before resuming; the server provides no file validator.";
                progress?.Report(new(PartialBytes(job), total, 0, 0));
                same = await VerifySavedBytesAsync(job, total!.Value, token);
                job.ResumeNote = same ? "Saved bytes checked. Continuing from the paused position." : "The saved bytes no longer match the server file; restarting safely.";
            }
            else if (PartialBytes(job) > 0)
                job.ResumeNote = validator ? "Continuing from the saved positions." : "Continuing from the saved positions without rereading downloaded data. No server file validator is available.";
        }
        if (!same)
        {
            if (hasParts && ranged && (job.ETag != null || job.LastModified != null)) job.ResumeNote = "The server file or its validator changed; restarting safely.";
            if (hasParts && job.TotalBytes != total) job.ResumeNote = "The server file size changed; restarting safely.";
            CleanupParts(job);
            job.Segments.Clear();
            int maxForHost = Uri.TryCreate(job.Url, UriKind.Absolute, out var hostUri) && Options.LimitFor(hostUri) is int limit ? limit : Options.MaximumConnections;
            int count = ranged && total >= 1048576 ? Math.Clamp(job.RequestedConnections, 1, maxForHost) : 1;
            if (ranged && total is long length)
            {
                long chunk = length / count;
                for (int i = 0; i < count; i++) job.Segments.Add(new() { Start = i * chunk, End = i == count - 1 ? length - 1 : (i + 1) * chunk - 1, CommittedBytes = 0 });
            }
            else job.Segments.Add(new() { Start = 0, CommittedBytes = 0 });
        }
        job.TotalBytes = total;
        job.ETag = etag;
        job.LastModified = modified;
        job.RangeSupported = ranged;
        for (int i = 0; i < job.Segments.Count; i++) { var part = job.Segments[i]; part.Active = false; part.WrittenBytes = File.Exists(job.PartPath(i)) ? new FileInfo(job.PartPath(i)).Length : 0; part.Finished = part.End is long end && part.WrittenBytes == end - part.Start + 1; }
        CheckAvailableSpace(job);
    }

    private async Task<bool> VerifySavedBytesAsync(DownloadJob job, long total, CancellationToken token)
    {
        // Compare every saved byte, not just a sample: equal file sizes or
        // matching boundary bytes alone cannot rule out a changed file.
        byte[] remoteBuffer = new byte[131072], localBuffer = new byte[131072];
        for (int i = 0; i < job.Segments.Count; i++)
        {
            long saved = job.Segments[i].CommittedBytes ?? 0;
            if (saved == 0) continue;
            long start = job.Segments[i].Start;
            using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
            request.Headers.Range = new RangeHeaderValue(start, start + saved - 1);
            using var response = await SendAsync(request, token, job.Session);
            CheckResponse(job, response);
            EnsureIdentity(response);
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" || range.From != start || range.To != start + saved - 1 || range.Length != total)
                throw new DownloadFailureException("The server refused the resume check. Saved parts were kept; retry or restart explicitly.", DownloadErrorKind.Protocol);
            await using var remote = await response.Content.ReadAsStreamAsync(token);
            await using var local = new FileStream(job.PartPath(i), FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            long remaining = saved;
            while (remaining > 0)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                idle.CancelAfter(Options.ReadTimeout);
                int read;
                try { read = await remote.ReadAsync(remoteBuffer.AsMemory(0, (int)Math.Min(remaining, remoteBuffer.Length)), idle.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DownloadFailureException("The server stopped during the resume check. Saved parts were kept.", DownloadErrorKind.Network, true); }
                if (read == 0) throw new DownloadFailureException("The resume check was interrupted. Saved parts were kept.", DownloadErrorKind.Network, true);
                await local.ReadExactlyAsync(localBuffer.AsMemory(0, read), token);
                if (!remoteBuffer.AsSpan(0, read).SequenceEqual(localBuffer.AsSpan(0, read))) return false;
                remaining -= read;
            }
        }
        return true;
    }

    public async Task<string?> GetSuggestedNameAsync(DownloadJob job, CancellationToken token)
    {
        return (await ProbeAsync(job, token)).SuggestedFileName;
    }

    public async Task<DownloadMetadata> ProbeAsync(DownloadJob job, CancellationToken token)
    {
        if (!Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Use a valid HTTP/HTTPS file link.");
        job.Session?.Validate(job.Url);
        if (await MediaResolver.TryResolveAsync(job.Url, job.Session, client, token) is { } resolved)
        {
            resolved.ResolvedSession.Validate(resolved.ResolvedUrl);
            job.Url = resolved.ResolvedUrl;
            job.Session = resolved.ResolvedSession;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        var response = await SendAsync(request, token, job.Session);
        // Some file hosts refuse a tiny Range probe but allow a normal GET.
        // Inspect only headers: never buffer the full file during a probe.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotAcceptable or HttpStatusCode.RequestedRangeNotSatisfiable && !response.Headers.WwwAuthenticate.Any())
        {
            response.Dispose();
            using var fallback = new HttpRequestMessage(HttpMethod.Get, job.Url);
            response = await SendAsync(fallback, token, job.Session);
        }
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotAcceptable && !response.Headers.WwwAuthenticate.Any())
        {
            // Some sites accept only requests that look like a browser navigation (Sec-Fetch-*, an HTML Accept).
            response.Dispose();
            var navigation = DownloadSession.NavigationHeaders(job.Session?.Referer != null);
            using var browserLike = new HttpRequestMessage(HttpMethod.Get, job.Url);
            var trial = job.Session?.Clone() ?? new DownloadSession { Origin = new Uri(job.Url).GetLeftPart(UriPartial.Authority) + "/" };
            trial.Headers = [.. trial.Headers.Where(h => navigation.All(n => !n.Name.Equals(h.Name, StringComparison.OrdinalIgnoreCase))), .. navigation];
            response = await SendAsync(browserLike, token, trial);
            if (response.IsSuccessStatusCode) job.Session = trial; // later requests keep the form the site accepted
        }
        using (response)
        {
            CheckResponse(job, response);
            EnsureIdentity(response);
            string? type = response.Content.Headers.ContentType?.MediaType;
            var disposition = response.Content.Headers.ContentDisposition;
            if (type is "text/html" or "application/xhtml+xml" && disposition?.FileName == null && disposition?.FileNameStar == null && Path.GetExtension(job.FileName).ToLowerInvariant() is not (".html" or ".htm"))
                throw new DownloadFailureException("The link returned a webpage instead of a file. Capture the actual download from your browser again; it may be a page link or an expired download link.", DownloadErrorKind.Protocol);
            var range = response.Content.Headers.ContentRange;
            bool partial = response.StatusCode == HttpStatusCode.PartialContent;
            if (partial && (range?.Unit != "bytes" || range.From != 0 || range.To != 0 || range.Length is <= 0))
                throw new DownloadFailureException("The server returned invalid file-size metadata.", DownloadErrorKind.Protocol);
            // Content-Length on a 206 is the size of the piece, not the file.
            long? total = partial ? range!.Length : response.Content.Headers.ContentLength;
            string? tag = response.Headers.ETag is { IsWeak: false } strong ? strong.ToString() : null;
            return new(response.RequestMessage?.RequestUri?.AbsoluteUri ?? job.Url, total, partial && total > 0,
                SuggestedName(response, job.FileName), type, tag, response.Content.Headers.LastModified);
        }
    }

    private static string? SuggestedName(HttpResponseMessage response, string current)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        string? name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name) && FileNames.IsOpaque(current) && response.RequestMessage?.RequestUri is { } final)
        {
            string candidate = Uri.UnescapeDataString(final.AbsolutePath.Split('/').Last());
            if (!FileNames.IsOpaque(candidate) && Path.HasExtension(candidate)) name = candidate;
        }
        return string.IsNullOrWhiteSpace(name) ? null : FileNames.Sanitize(name);
    }

    private async Task TransferAsync(DownloadJob job, int index, Action<int> report, CancellationToken token)
    {
        var segment = job.Segments[index];
        string path = job.PartPath(index);
        long existing = File.Exists(path) ? new FileInfo(path).Length : 0;
        long? expected, requestedEnd;
        lock (job.SegmentGate) { requestedEnd = segment.End; expected = requestedEnd - segment.Start + 1; }
        if (expected == existing) return;
        lock (job.SegmentGate) { segment.StatusText = "Send GET..."; segment.Active = true; }
        using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
        if (job.RangeSupported)
        {
            request.Headers.Range = new RangeHeaderValue(segment.Start + existing, requestedEnd);
            if (job.ETag != null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(job.ETag));
            else if (job.LastModified != null && !Options.IgnoreLastModifiedChanges) request.Headers.IfRange = new RangeConditionHeaderValue(job.LastModified.Value);
        }
        using var response = await SendAsync(request, token, job.Session);
        CheckResponse(job, response);
        EnsureIdentity(response);
        if (job.RangeSupported)
        {
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" || range.From != segment.Start + existing || range.To != requestedEnd || range.Length != job.TotalBytes)
                throw new DownloadFailureException("The server changed the file or refused a range. Resume to retry safely.", DownloadErrorKind.Protocol);
            if (job.ETag != null && response.Headers.ETag is { } currentTag && currentTag.ToString() != job.ETag)
                throw new DownloadFailureException("The file changed during the download. Resume to restart safely.", DownloadErrorKind.Protocol);
            if (!Options.IgnoreLastModifiedChanges && job.ETag == null && job.LastModified != null && response.Content.Headers.LastModified is { } currentDate && currentDate != job.LastModified)
                throw new DownloadFailureException("The file changed during the download. Resume to restart safely.", DownloadErrorKind.Protocol);
        }
        else if (response.StatusCode != HttpStatusCode.OK)
            throw new DownloadFailureException("The server did not return a complete file.", DownloadErrorKind.Protocol);
        lock (job.SegmentGate) { segment.StatusText = "Receiving data..."; }
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(path, existing > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 131072, true);
        byte[] buffer = new byte[131072];
        long received = existing;
        var flushClock = Stopwatch.StartNew();
        try
        {
         while (true)
         {
            lock (job.SegmentGate) expected = segment.End - segment.Start + 1;
            if (expected is long current && received >= current) break;
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(Options.ReadTimeout);
            int read;
            try { read = await input.ReadAsync(buffer.AsMemory(0, expected is long remaining ? (int)Math.Min(buffer.Length, remaining - received) : buffer.Length), idle.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DownloadFailureException("The server stopped sending data.", DownloadErrorKind.Network, true); }
            catch (IOException ex) { throw new DownloadFailureException("The connection was interrupted: " + ex.Message, DownloadErrorKind.Network, true); }
            if (read == 0) break;
            lock (job.SegmentGate) expected = segment.End - segment.Start + 1;
            if (expected is long size) read = (int)Math.Min(read, size - received);
            if (Options.GlobalLimiter.BytesPerSecond > 0) await Options.GlobalLimiter.ThrottleAsync(read, token);
            long fileLimit = job.SpeedLimitBytes ?? 0;
            if (job.FileLimiter.BytesPerSecond != fileLimit) job.FileLimiter.BytesPerSecond = fileLimit;
            if (fileLimit > 0) await job.FileLimiter.ThrottleAsync(read, token);
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            lock (job.SegmentGate) segment.WrittenBytes = received;
            if (flushClock.Elapsed >= TimeSpan.FromSeconds(2))
            {
                output.Flush(true);
                lock (job.SegmentGate) segment.CommittedBytes = output.Length;
                flushClock.Restart();
            }
            report(read);
         }
        }
        finally
        {
            output.Flush(true);
            lock (job.SegmentGate)
            {
                segment.CommittedBytes = output.Length;
                segment.StatusText = (expected != null && received >= expected) ? "Complete" : "Idle";
            }
        }
        lock (job.SegmentGate) expected = segment.End - segment.Start + 1;
        if (expected is long target && received != target) throw new DownloadFailureException("The connection ended before the file was complete.", DownloadErrorKind.Network, true);
        if (!job.RangeSupported && job.TotalBytes is long total && received != total) throw new DownloadFailureException("The connection ended before the file was complete.", DownloadErrorKind.Network, true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token, DownloadSession? session = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(Options.HeadersTimeout);
        try
        {
            var target = request.RequestUri!;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int redirect = 0; redirect <= 10; redirect++)
            {
                if (!visited.Add(target.AbsoluteUri)) throw new DownloadFailureException("Redirect loop detected.", DownloadErrorKind.Protocol);
                using var next = new HttpRequestMessage(request.Method, target);
                foreach (var header in request.Headers) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (!next.Headers.Contains("User-Agent"))
                    next.Headers.TryAddWithoutValidation("User-Agent", session?.UserAgent ?? DefaultUserAgent);
                if (!next.Headers.Contains("Accept"))
                    next.Headers.TryAddWithoutValidation("Accept", "*/*");
                if (!next.Headers.Contains("Accept-Language"))
                    next.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                if (!next.Headers.Contains("Referer"))
                {
                    if (!string.IsNullOrEmpty(session?.Referer))
                        next.Headers.TryAddWithoutValidation("Referer", session.Referer);
                    else
                        next.Headers.TryAddWithoutValidation("Referer", target.GetLeftPart(UriPartial.Authority) + "/");
                }
                session?.Apply(next);
                var response = await client.SendAsync(next, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.RequestMessage ??= next;
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null) throw new DownloadFailureException("Redirect has no destination.", DownloadErrorKind.Protocol);
                var destination = location.IsAbsoluteUri ? location : new Uri(target, location);
                if (destination.Scheme is not ("http" or "https") || destination.UserInfo.Length > 0 || target.Scheme == "https" && destination.Scheme != "https") throw new DownloadFailureException("Unsafe redirect rejected.", DownloadErrorKind.Protocol);
                target = destination;
            }
            throw new DownloadFailureException("Too many redirects.", DownloadErrorKind.Protocol);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DownloadFailureException("The server did not respond before the connection timeout.", DownloadErrorKind.Network, true); }
    }
    private static void EnsureIdentity(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new DownloadFailureException("The server ignored the uncompressed transfer request.", DownloadErrorKind.Protocol);
    }
    private static void CleanupParts(DownloadJob job)
    {
        string directory = job.TemporaryDirectory is { Length: > 0 } ? job.TemporaryDirectory : Path.GetDirectoryName(job.Destination)!;
        if (!Directory.Exists(directory)) return;
        string prefix = job.TemporaryDirectory is { Length: > 0 } ? job.Id + "." : Path.GetFileName(job.Destination) + "." + job.Id + ".";
        foreach (string path in Directory.EnumerateFiles(directory, prefix + "*.part"))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(name[prefix.Length..^5], out int index) && index >= 0) File.Delete(path);
        }
    }
    public void Dispose() => client.Dispose();

    private static long PartialBytes(DownloadJob job) => Enumerable.Range(0, job.Segments.Count).Sum(i => File.Exists(job.PartPath(i)) ? new FileInfo(job.PartPath(i)).Length : 0);
    private static int ActiveConnections(DownloadJob job) { lock (job.SegmentGate) return job.Segments.Count(part => part.Active); }

    private async Task RunSegmentsAsync(DownloadJob job, Action<int> report, CancellationToken token, Action? checkpoint)
    {
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(token);
        var hostUri = Uri.TryCreate(job.Url, UriKind.Absolute, out var parsed) ? parsed : null;
        int? hostLimit = hostUri != null ? Options.LimitFor(hostUri) : null;
        int maxForHost = hostLimit ?? Options.MaximumConnections;
        int count = job.RangeSupported ? Math.Clamp(job.RequestedConnections, 1, maxForHost) : 1;
        int running = count;
        (int Index, bool Split)? Claim()
        {
            lock (job.SegmentGate)
            {
                for (int i = 0; i < job.Segments.Count; i++)
                {
                    var part = job.Segments[i];
                    if (!part.Active && !part.Finished) { part.Active = true; return (i, false); }
                }
                // A site that limits connections also gets no extra split requests beyond that limit.
                int maxSegmentsForHost = hostLimit ?? Options.MaximumSegments;
                if (!job.RangeSupported || job.Segments.Count >= maxSegmentsForHost) return null;
                var candidate = job.Segments.Where(part => part.Active && part.End != null)
                    .OrderByDescending(part => part.End!.Value - part.Start + 1 - part.WrittenBytes).FirstOrDefault();
                if (candidate == null) return null;
                long remaining = candidate.End!.Value - candidate.Start + 1 - candidate.WrittenBytes;
                if (remaining < Math.Max(Options.MinimumSplitBytes * 2, 1048576)) return null;
                long split = candidate.Start + candidate.WrittenBytes + remaining / 2;
                // The midpoint stays more than one in-flight write ahead of the original connection.
                var tail = new DownloadSegment { Start = split, End = candidate.End, CommittedBytes = 0, Active = true };
                candidate.End = split - 1;
                job.Segments.Add(tail);
                job.DynamicSplits++;
                return (job.Segments.Count - 1, true);
            }
        }
        async Task Worker()
        {
            try
            {
                while (!workers.IsCancellationRequested && Claim() is { } claim)
                {
                    int index = claim.Index;
                    if (claim.Split) checkpoint?.Invoke(); // Save outside the segment lock to avoid storage/segment lock inversion.
                    try { await TransferAsync(job, index, report, workers.Token); lock (job.SegmentGate) job.Segments[index].Finished = true; }
                    catch (Exception error) when (IsRefusal(error) && Volatile.Read(ref running) > 1 && !workers.IsCancellationRequested)
                    {
                        // The site refused one more connection: this one stops, its part stays for the others,
                        // and the site is remembered with the number that worked.
                        int remaining = Interlocked.Decrement(ref running);
                        if (hostUri != null) Options.Learn(hostUri, remaining);
                        job.ResumeNote = $"{hostUri?.IdnHost ?? "The server"} allows fewer connections; continuing with {remaining}.";
                        return;
                    }
                    finally { lock (job.SegmentGate) job.Segments[index].Active = false; }
                }
            }
            catch { await workers.CancelAsync(); throw; }
        }
        var tasks = Enumerable.Range(0, count).Select(_ => Worker()).ToArray();
        try { await Task.WhenAll(tasks); }
        catch { if (tasks.FirstOrDefault(task => task.IsFaulted)?.Exception?.InnerException is { } error) throw error; throw; }
        token.ThrowIfCancellationRequested();
        if (job.RangeSupported)
        {
            long next = 0;
            foreach (var part in job.Segments.OrderBy(part => part.Start))
            {
                if (part.Start != next || part.End == null || part.WrittenBytes != part.End - part.Start + 1) throw new DownloadFailureException("Segment coverage is incomplete.", DownloadErrorKind.Protocol);
                next = part.End.Value + 1;
            }
            if (next != job.TotalBytes) throw new DownloadFailureException("Segment coverage does not match the file size.", DownloadErrorKind.Protocol);
        }
    }
    // Refusals of a request (not failures while data flows): the site wants fewer connections.
    private static bool IsRefusal(Exception error) => error switch
    {
        DownloadFailureException failure => failure.Kind is DownloadErrorKind.AccessDenied or DownloadErrorKind.RateLimited,
        HttpRequestException http => http.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden,
        _ => false
    };
    private static void CheckAvailableSpace(DownloadJob job)
    {
        if (job.TotalBytes is not long total) return;
        DriveInfo drive;
        long available;
        try { drive = new DriveInfo(Path.GetPathRoot(job.Destination)!); available = drive.AvailableFreeSpace; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return; }
        decimal required = (decimal)total * 2 - Math.Min(total, PartialBytes(job));
        if (required > available)
            throw new DownloadFailureException("Not enough free disk space for the segments and final file assembly.", DownloadErrorKind.DiskSpace);
        if (total > uint.MaxValue && drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
            throw new DownloadFailureException("This file exceeds the FAT32 file-size limit. Choose an NTFS or exFAT destination.", DownloadErrorKind.Storage);
    }
    private static void CheckResponse(DownloadJob job, HttpResponseMessage response)
    {
        job.HttpStatus = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
            job.RetryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new DownloadFailureException("The server refused this request (HTTP 403). The link may have expired, or the site may require its browser session. Capture the download again from the browser; this does not necessarily require a username or password.", DownloadErrorKind.AccessDenied);
        response.EnsureSuccessStatusCode();
    }
    private static void RecordFailure(DownloadJob job, Exception error)
    {
        job.State = DownloadState.Failed;
        job.Error = error.Message;
        job.Retryable = false;
        if (error is DownloadFailureException failure)
        {
            job.ErrorKind = failure.Kind; job.Retryable = failure.Retryable;
            if (failure.Kind == DownloadErrorKind.AccessDenied && job.RangeSupported && job.RequestedConnections > 1)
            {
                job.RequestedConnections = 1;
                job.LearnedSingleConnection = true;
                job.Retryable = true;
                job.RetryAfter = TimeSpan.FromSeconds(1);
            }
        }
        else if (error is HttpRequestException http)
        {
            if (http.StatusCode is { } code) job.HttpStatus = (int)code;
            job.ErrorKind = http.StatusCode switch
            {
                HttpStatusCode.Unauthorized => DownloadErrorKind.Authentication,
                HttpStatusCode.Forbidden => DownloadErrorKind.Protocol,
                HttpStatusCode.NotFound => DownloadErrorKind.NotFound,
                HttpStatusCode.Gone => DownloadErrorKind.ExpiredUrl,
                HttpStatusCode.TooManyRequests => DownloadErrorKind.RateLimited,
                null => DownloadErrorKind.Network,
                _ => DownloadErrorKind.Server
            };
            if (http.StatusCode == HttpStatusCode.TooManyRequests)
            {
                job.RequestedConnections = 1;
                job.RetryAfter = TimeSpan.FromSeconds(3);
            }
            job.Retryable = http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
        }
        else if (error is UnauthorizedAccessException) job.ErrorKind = DownloadErrorKind.Permission;
        else if (error is IOException io) job.ErrorKind = (io.HResult & 0xFFFF) is 39 or 112 ? DownloadErrorKind.DiskSpace : DownloadErrorKind.Storage;
        else job.ErrorKind = DownloadErrorKind.Unknown;
        job.State = job.ErrorKind switch
        {
            DownloadErrorKind.Authentication => DownloadState.AuthRequired,
            DownloadErrorKind.ExpiredUrl => DownloadState.ExpiredUrl,
            DownloadErrorKind.DiskSpace => DownloadState.NoDiskSpace,
            _ => DownloadState.Failed
        };
    }
}

