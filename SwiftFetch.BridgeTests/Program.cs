using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using SwiftFetch.Bridge;

string release = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "dist"));
string host = Path.Combine(release, "SwiftFetch.NativeHost.exe");
using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(release, Protocol.HostName + ".json")));
string origin = manifest.RootElement.GetProperty("allowed_origins")[0].GetString()!;
int passed = 0;
await Check("UTF-8 native message roundtrip", async () =>
{
    using var stream = new MemoryStream();
    var original = new BridgeRequest("download", "https://example.test/fichier", "téléchargement.zip", Guid.NewGuid().ToString());
    await Protocol.WriteAsync(stream, original);
    stream.Position = 0;
    Require(await Protocol.ReadAsync<BridgeRequest>(stream) == original, "UTF-8 message changed.");
});
await Check("malformed frame length is rejected", async () =>
{
    using var stream = new MemoryStream([255, 255, 255, 127]);
    try { await Protocol.ReadAsync<BridgeRequest>(stream); throw new Exception("Expected invalid size."); }
    catch (InvalidDataException) { }
});
await Check("non-HTTP links and embedded credentials are rejected", () =>
{
    foreach (string url in new[] { "file:///C:/x", "javascript:alert(1)", "https://user:password@example.test/file" })
        Require(Protocol.Validate(new("download", url, RequestId: Guid.NewGuid().ToString())) != null, "Unsafe URL accepted.");
    Require(Protocol.Validate(new("download", "https://example.test/file", RequestId: "bad")) != null, "Invalid request ID accepted.");
    return Task.CompletedTask;
});
await Check("session data roundtrips in native frames", async () =>
{
    using var stream = new MemoryStream();
    var request = new BridgeRequest("download", "https://test.invalid/file.zip", RequestId: Guid.NewGuid().ToString(), Session: new() { Origin = "https://test.invalid", Cookies = [new() { Name = "sid", Value = "PIPE-SESSION-TEST", Domain = "test.invalid" }] });
    Require(Protocol.Validate(request) == null, "Valid session was rejected.");
    await Protocol.WriteAsync(stream, request); stream.Position = 0;
    var restored = await Protocol.ReadAsync<BridgeRequest>(stream);
    Require(restored?.Session?.Cookies.Single().Value == "PIPE-SESSION-TEST", "Session was lost in the frame.");
    Require(Protocol.Validate(request with { Session = new() { Origin = "https://other.invalid" } }) != null, "Mismatched session origin was accepted.");
});
await Check("typed video requests validate format IDs and supported page URLs", () =>
{
    var selected = new SwiftFetch.Core.VideoSelection { FormatId="137+140", Extension="mp4", Label="1080p" };
    var request = new BridgeRequest("download","https://www.youtube.com/watch?v=jNQXAC9IVRw",RequestId:Guid.NewGuid().ToString(),Video:selected);
    Require(Protocol.Validate(request)==null,"Valid video rejected.");
    Require(Protocol.Validate(request with { Url="https://example.test/page" })==null,"A page found with generic detection was rejected.");
    foreach (string unsafeUrl in new[] { "https://u:p@example.test/page", "file:///C:/Windows/win.ini", "javascript:alert(1)" })
        Require(Protocol.Validate(request with { Url=unsafeUrl })!=null && Protocol.Validate(new BridgeRequest("video-formats",unsafeUrl))!=null,"Unsafe video address accepted: "+unsafeUrl);
    Require(Protocol.Validate(request with { Url="https://cdn.test/hls/master.m3u8" })==null,"An HLS stream was rejected.");
    Require(Protocol.Validate(request with { Url="https://vimeo.com/76979871" })==null,"A known video site was rejected.");
    Require(Protocol.Validate(new BridgeRequest("video-formats","https://example.test/page"))==null,"Generic page search rejected.");
    Require(Protocol.Validate(new BridgeRequest("download","magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=x",RequestId:Guid.NewGuid().ToString()))==null,"A magnet link from the browser was rejected.");
    Require(Protocol.Validate(new BridgeRequest("download","magnet:?xt=urn:btih:nothash",RequestId:Guid.NewGuid().ToString()))!=null,"An invalid magnet link was accepted.");
    selected.FormatId="--exec"; Require(Protocol.Validate(request)!=null,"Unsafe format accepted.");
    Require(Protocol.Validate(new("video-formats","https://www.youtube.com/watch?v=jNQXAC9IVRw"))==null,"Format discovery action rejected.");
    return Task.CompletedTask;
});
await Check("real native host responds to connection test", async () =>
{
    Require((await SendHost(new("ping"))).Ok, "Native ping failed.");
});
await Check("real native host returns capture policy without opening the manager", async () =>
{
    int before = Process.GetProcessesByName("SwiftFetch").Length + Process.GetProcessesByName("ApexDownloader").Length;
    var response = await SendHost(new("capture-policy"));
    Require(response.Ok && response.Policy != null, "Capture policy was not returned.");
    Require(Process.GetProcessesByName("SwiftFetch").Length + Process.GetProcessesByName("ApexDownloader").Length == before, "Policy query opened the manager.");
});
await Check("real native host rejects an unregistered extension", async () =>
{
    Require(!(await SendHost(new("ping"), "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/")).Ok, "Unregistered origin accepted.");
});
await Check("real native host rejects invalid requests", async () =>
{
    Require(!(await SendHost(new("download", "file:///C:/x", RequestId: Guid.NewGuid().ToString()))).Ok, "Invalid URL accepted by host.");
});
await Check("real host forwards a download over the per-user Windows pipe", async () =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    using var pipe = new NamedPipeServerStream(Protocol.GetInstanceName(release), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    var request = new BridgeRequest("download", "https://example.test/file.zip", "fichier.zip", Guid.NewGuid().ToString());
    var responseTask = SendHost(request);
    await pipe.WaitForConnectionAsync(timeout.Token);
    Require(await Protocol.ReadAsync<BridgeRequest>(pipe, timeout.Token) == request, "Request changed in transit.");
    await Protocol.WriteAsync(pipe, new BridgeResponse(true, JobId: "job-123"), timeout.Token);
    var response = await responseTask;
    Require(response.Ok && response.JobId == "job-123", "App acknowledgement was lost.");
});
Console.WriteLine($"All {passed} browser bridge checks passed.");
if (args.Contains("--open-app"))
{
    Require((await SendHost(new("show"))).Ok, "Could not open the real SwiftFetch app.");
    Require((await SendHost(new("show"))).Ok, "Could not reuse the real SwiftFetch app window.");
    Console.WriteLine("PASS native bridge opens the real app and accepts a second show request.");
}

async Task<BridgeResponse> SendHost(BridgeRequest request, string? caller = null)
{
    var start = new ProcessStartInfo(host) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(caller ?? origin);
    using var process = Process.Start(start)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await Protocol.WriteAsync(process.StandardInput.BaseStream, request, timeout.Token);
    var response = await Protocol.ReadAsync<BridgeResponse>(process.StandardOutput.BaseStream, timeout.Token);
    await process.WaitForExitAsync(timeout.Token);
    if (process.ExitCode != 0) throw new Exception("Host exited unsuccessfully: " + await process.StandardError.ReadToEndAsync(timeout.Token));
    return response ?? throw new Exception("Native host returned no message.");
}
async Task Check(string name, Func<Task> test) { await test(); passed++; Console.WriteLine("PASS " + name); }
static void Require(bool condition, string error) { if (!condition) throw new Exception(error); }
