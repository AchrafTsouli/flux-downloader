using System.Diagnostics;
using System.Text.Json;
using SwiftFetch.Core;

string root = Path.Combine(Environment.CurrentDirectory,".test-tmp","media-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string tools = Path.GetFullPath(args.FirstOrDefault(value=>value.StartsWith("--tools="))?[8..] ?? Path.Combine(Environment.CurrentDirectory,"video-tools"));
const string page = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
if (args.FirstOrDefault(value=>value.StartsWith("--update-ytdlp=")) is string updateArg)
{
    // Real updater check into a scratch folder: latest release, checksum, execution and version.
    var updater=new YtDlpUpdater(new System.Net.Http.HttpClient(),updateArg[15..]);
    using var updateTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));
    Console.WriteLine("LATEST "+await updater.LatestTagAsync(updateTimeout.Token));
    Console.WriteLine("INSTALLED "+(await updater.UpdateAsync(tools,updateTimeout.Token)??"(already current)"));
    Console.WriteLine("PREFERRED "+(YtDlpUpdater.PreferredExecutable(tools,updateArg[15..])??"bundled"));
    return;
}
if (args.FirstOrDefault(value=>value.StartsWith("--probe=")) is string probeArg)
{
    // Real-tool smoke check for any page: lists qualities or playlist entries; --download also fetches one preset.
    string target=probeArg[8..];var real=new VideoTools(tools);
    using var probeTimeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
    var found=await real.ProbeAsync(target,null,probeTimeout.Token);
    if(found.Playlist is {} list){Console.WriteLine($"PLAYLIST {list.Title}: {list.Entries.Count} entries");foreach(var e in list.Entries.Take(5))Console.WriteLine($"  {e.Title} | {e.Url} | {e.DurationSeconds}");return;}
    Console.WriteLine($"VIDEO {found.Catalog!.Title} [{found.Catalog.Site}]: {found.Catalog.Formats.Count} qualities");
    foreach(var f in found.Catalog.Formats.Take(8))Console.WriteLine($"  {f.FormatId} | {f.Label} | {f.TotalBytes}");
    if(args.FirstOrDefault(value=>value.StartsWith("--download=")) is string preset)
    {
        string kind=preset[11..];var selection=kind=="audio"?VideoSelection.Preset(null,true):kind=="best"?VideoSelection.Preset(null,false):kind=="first"?found.Catalog.Formats[^1].ToSelection():VideoSelection.Preset(int.Parse(kind),false);
        var job=new DownloadJob{Url=target,Destination=Path.Combine(root,"probe."+selection.Extension),Video=selection};
        await real.DownloadAsync(job,null,probeTimeout.Token);
        Console.WriteLine(job.State==DownloadState.Completed?$"DOWNLOADED {job.Destination} {new FileInfo(job.Destination).Length} bytes, tracks {string.Join("+",job.MediaTracks)}":$"FAILED {job.Error}");
    }
    return;
}
if (args.Contains("--live") || args.Contains("--live-pause"))
{
    var pipeline = new VideoTools(tools, new LoggingRunner());
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var catalog = await pipeline.GetFormatsAsync(page,null,timeout.Token);
    var format = catalog.Formats.Where(f => f.Extension == "mp4" && f.FormatId.Contains('+')).Last();
    if (args.FirstOrDefault(value=>value.StartsWith("--format=")) is string requested) format = new(requested[9..],"Page-player selected tracks",args.FirstOrDefault(value=>value.StartsWith("--container="))?[12..]??"mp4",null,false);
    Console.WriteLine($"LIVE formats: {catalog.Formats.Count}; selected {format.Label}; estimated bytes {format.TotalBytes}");
    var live = new DownloadJob {Url=page,Destination=Path.Combine(root,"live-test."+format.Extension),Video=new(){FormatId=format.FormatId,Extension=format.Extension,Label=format.Label,TotalBytes=format.TotalBytes,Approximate=format.Approximate},TotalBytes=format.TotalBytes,VideoSizeEstimated=format.Approximate,MaximumRetries=1};
    long pausedBytes=0;
    if (args.Contains("--live-pause"))
    {
        live.SpeedLimitBytes=32768;
        using var pause=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await pipeline.DownloadAsync(live,new InlineProgress(p=>{if(p.Bytes>=16384)pause.Cancel();}),pause.Token);
        Require(live.State==DownloadState.Paused&&live.MediaDownloadedBytes>=16384,"Real pause did not retain progress.");
        Require(Directory.EnumerateFiles(live.MediaWorkDirectory!).Any(),"No retained partial video files.");
        Console.WriteLine("PASS real pause retained parts: "+live.MediaDownloadedBytes+" bytes");
        pausedBytes=live.MediaDownloadedBytes;
        live=JsonSerializer.Deserialize<DownloadJob>(JsonSerializer.Serialize(live))!;
        live.SpeedLimitBytes=null;
    }
    int reports=0;
    await pipeline.DownloadAsync(live,new InlineProgress(p=> {reports++;}),timeout.Token);
    Require(live.State==DownloadState.Completed,"Live pipeline failed: "+live.Error);
    Require(reports>1,"No track progress reported by real yt-dlp.");
    Console.WriteLine($"PASS real YouTube separate-track download, FFmpeg merge and audio/video verification: {new FileInfo(live.Destination).Length} bytes. {live.Destination}");
    string report=Path.Combine(Environment.CurrentDirectory,"docs","measurements","compact-live-validation.json");
    var records=File.Exists(report)?JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(report))!:[];
    records.Add(JsonSerializer.SerializeToElement(new{page,format=format.FormatId,container=format.Extension,pausedBytes,completedBytes=new FileInfo(live.Destination).Length,progressReports=reports,tools=Path.GetFileName(tools),verifiedAudioAndVideo=true,timestamp=DateTimeOffset.UtcNow}));
    File.WriteAllText(report,JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
    return;
}
int passed=0;
void Check(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
async Task CheckAsync(string name,Func<Task> action){await action();passed++;Console.WriteLine("PASS "+name);}
string json="""
{"title":"A video","formats":[
{"format_id":"137","ext":"mp4","url":"https://cdn.test/v","vcodec":"avc1","acodec":"none","height":1080,"fps":60,"filesize":100},
{"format_id":"140","ext":"m4a","url":"https://cdn.test/a","vcodec":"none","acodec":"mp4a","abr":128,"filesize":20},
{"format_id":"18","ext":"mp4","url":"https://cdn.test/m","vcodec":"avc1","acodec":"mp4a","height":360,"filesize_approx":40},
{"format_id":"drm","ext":"mp4","url":"https://cdn.test/drm","vcodec":"avc1","acodec":"mp4a","has_drm":true},
{"format_id":"story","ext":"mhtml","url":"https://cdn.test/story","vcodec":"none","acodec":"none"}]}
""";
Check("video URL canonicalization rejects unsafe pages and lookalike YouTube hosts",()=>{
    Require(VideoPages.Canonicalize("https://youtu.be/jNQXAC9IVRw")==page,"Short URL failed");
    Require(VideoPages.Canonicalize("https://www.youtube.com/shorts/jNQXAC9IVRw")==page,"Shorts failed");
    foreach(string url in new[]{"http://youtube.com/watch?v=jNQXAC9IVRw","https://u:p@youtube.com/watch?v=jNQXAC9IVRw","https://youtube.com/watch?v=bad","https://www.youtube.com/playlist?list=PL123","file:///C:/x.mp4","ytsearch:cats"})Require(VideoPages.Canonicalize(url)==null,"Unsafe URL accepted: "+url);
    // Other hosts pass to the generic extractor unchanged; they never become a YouTube page.
    Require(VideoPages.Canonicalize("https://youtube.com.evil.test/watch?v=jNQXAC9IVRw#t")=="https://youtube.com.evil.test/watch?v=jNQXAC9IVRw","Lookalike host was rewritten");
    Require(VideoPages.Canonicalize("https://cdn.test/live/master.m3u8?token=1")=="https://cdn.test/live/master.m3u8?token=1","Stream manifest rejected");
});
Check("known video sites, streams and playlists are detected without matching listings",()=>{
    foreach(string url in new[]{"https://vimeo.com/76979871","https://x.com/user/status/1234567890","https://www.tiktok.com/@a.b/video/7000000000000000000","https://www.instagram.com/reel/AbC_d-1/","https://www.facebook.com/watch/?v=123","https://cdn.test/a/master.m3u8","https://cdn.test/manifest.mpd","https://www.youtube.com/playlist?list=PLabc_DEF-1"})
        Require(VideoPages.IsKnownVideoPage(url),"Not detected: "+url);
    foreach(string url in new[]{"https://vimeo.com/","https://x.com/home","https://www.facebook.com/watch/","https://vimeo.com.evil.test/123","https://cdn.test/file.zip"})
        Require(!VideoPages.IsKnownVideoPage(url),"Wrongly detected: "+url);
    Require(VideoPages.Playlist("https://www.youtube.com/watch?v=jNQXAC9IVRw&list=PLabc")=="https://www.youtube.com/playlist?list=PLabc","Watch-with-list not normalized");
    Require(VideoPages.Playlist("https://www.youtube.com/@SomeChannel")=="https://www.youtube.com/@SomeChannel/videos","Channel not normalized");
    Require(VideoPages.Playlist("https://www.youtube.com/playlist?list=bad;id")==null,"Invalid playlist ID accepted");
});
Check("generic and HLS qualities with unknown codecs, silent video and an audio-only choice",()=>{
    var catalog=VideoFormats.Parse("""
    {"title":"Stream","extractor_key":"Generic","formats":[
    {"format_id":"hls-2500","ext":"mp4","protocol":"m3u8_native","url":"https://cdn.test/720.m3u8","height":720,"tbr":2500},
    {"format_id":"hls-800","ext":"mp4","protocol":"m3u8_native","url":"https://cdn.test/360.m3u8","height":360,"tbr":800},
    {"format_id":"rtmp-1","ext":"mp4","protocol":"rtmp","url":"https://cdn.test/rtmp","height":1080},
    {"format_id":"flv","ext":"flv","url":"https://cdn.test/a.flv","height":1080}]}
    """);
    Require(catalog.Formats.Count==2&&catalog.Formats[0].FormatId=="hls-2500"&&catalog.Formats[0].Label.Contains("720p")&&catalog.Formats[0].Label.Contains("video + audio")&&catalog.Site=="Generic","HLS variants mis-parsed: "+string.Join(" | ",catalog.Formats.Select(f=>f.Label)));
    var silent=VideoFormats.Parse("""{"title":"Clip","formats":[{"format_id":"dash-v","ext":"mp4","url":"https://cdn.test/v","vcodec":"avc1","acodec":"none","height":480}]}""");
    Require(silent.Formats.Single().NoAudio&&silent.Formats[0].Label.Contains("video only"),"Silent video not offered");
    var withAudio=VideoFormats.Parse(json);
    Require(withAudio.Formats.Last().AudioOnly&&withAudio.Formats.Last().Extension=="m4a"&&withAudio.Formats.Last().FormatId=="140","Audio-only choice missing");
    var multi=VideoFormats.Parse("{\"_type\":\"playlist\",\"entries\":["+json+"]}");
    Require(multi.Title=="A video","Multi-video pages should use their first video");
});
Check("flat playlists keep safe unique video links and skip private entries",()=>{
    var list=VideoFormats.ParsePlaylist("""
    {"_type":"playlist","title":"Mix","entries":[
    {"url":"https://www.youtube.com/watch?v=jNQXAC9IVRw","title":"First","duration":19},
    {"url":"https://www.youtube.com/watch?v=jNQXAC9IVRw","title":"Duplicate"},
    {"url":"https://www.youtube.com/watch?v=aaaaaaaaaaa","title":"[Private video]"},
    {"url":"javascript:alert(1)","title":"Bad"},
    {"url":"dQw4w9WgXcQ","webpage_url":"https://youtu.be/dQw4w9WgXcQ","title":""}]}
    """);
    Require(list.Title=="Mix"&&list.Entries.Count==2&&list.Entries[0].DurationSeconds==19&&list.Entries[1].Url=="https://www.youtube.com/watch?v=dQw4w9WgXcQ"&&list.Entries[1].Title=="Video 2","Playlist entries wrong: "+string.Join(",",list.Entries.Select(e=>e.Url)));
});
Check("quality selection pairs audio, includes frame rate, sums sizes, excludes DRM",()=>{
    var catalog=VideoFormats.Parse(json);Require(catalog.Formats.Count==3&&catalog.Formats[2].AudioOnly,"Unexpected formats");
    var best=catalog.Formats[0];Require(best.FormatId=="137+140"&&best.Label.Contains("60 fps")&&best.TotalBytes==120&&!best.Approximate,"Separate track selection failed");
    Require(catalog.Formats[1].Approximate,"Estimated size must be labelled");
    var duplicateRoot=System.Text.Json.Nodes.JsonNode.Parse(json)!;
    var duplicateFormats=duplicateRoot["formats"]!.AsArray();
    var duplicate=duplicateFormats[0]!.DeepClone(); duplicate["format_id"]="duplicate"; duplicateFormats.Add(duplicate);
    Require(VideoFormats.Parse(duplicateRoot.ToJsonString()).Formats.Count==3,"Equivalent quality variants were duplicated.");
});
Check("format options cannot inject yt-dlp switches or filters",()=>{
    foreach(string id in new[]{"--exec", "137;calc", "bestvideo/best", "137+140+249", "bv*[height<=720]", "apex-999", "apex-audio", "apex-"})
    {try{new VideoSelection{FormatId=id}.Validate(page);throw new Exception("Invalid format accepted: "+id);}catch(ArgumentException){}}
    foreach(var preset in new[]{VideoSelection.Preset(null,false),VideoSelection.Preset(720,false),VideoSelection.Preset(null,true)}) preset.Validate(page);
    new VideoSelection{FormatId="hls-1080@60+audio_eng.1",Extension="mkv"}.Validate("https://cdn.test/master.m3u8");
});
Check("live broadcasts are rejected",()=>{try{VideoFormats.Parse("{\"is_live\":true}");throw new Exception("Live accepted");}catch(InvalidDataException){}});
var runner=new FakeRunner();var pipelineFake=new VideoTools(tools,runner);
DownloadJob Job(string name)=>new(){Url=page,Destination=Path.Combine(root,name+".mp4"),Video=new(){FormatId="137+140",Label="MP4 1080p",Extension="mp4",TotalBytes=120},TotalBytes=120};
await CheckAsync("pipeline tracks both parts, verifies both streams and publishes only final output",async()=>{
    var job=Job("complete");var progress=new List<DownloadProgress>();
    await pipelineFake.DownloadAsync(job,new InlineProgress(progress.Add),CancellationToken.None);
    Require(job.State==DownloadState.Completed&&File.Exists(job.Destination),job.Error);
    Require(progress.Any(p=>p.Bytes==120),"Aggregate track progress missing");
    Require(runner.Calls[0].ArgumentList.Contains("--continue")&&runner.Calls[0].ArgumentList.Contains("--abort-on-unavailable-fragments"),"Resume/incomplete fragment safeguards missing");
    string expectedRuntime=File.Exists(Path.Combine(tools,"qjs.exe"))?"quickjs:":"node:";
    Require(runner.Calls[0].ArgumentList.Any(argument=>argument.StartsWith(expectedRuntime,StringComparison.Ordinal)),"The bundled JavaScript runtime was not selected.");
});
await CheckAsync("a missing audio stream cannot be marked complete",async()=>{
    runner.MissingAudio=true;var job=Job("no-audio");await pipelineFake.DownloadAsync(job,null,CancellationToken.None);
    Require(job.State==DownloadState.Failed&&!File.Exists(job.Destination),"Incomplete video published");runner.MissingAudio=false;
});
await CheckAsync("failed or incomplete probe output fails the job without crashing or publishing",async()=>{
    foreach(bool failed in new[]{true,false}){
        runner.BadProbe=true;runner.ProbeExit=failed?1:0;var job=Job("bad-probe-"+failed);
        await pipelineFake.DownloadAsync(job,null,CancellationToken.None);
        Require(job.State==DownloadState.Failed&&!File.Exists(job.Destination),"Bad probe output escaped as an exception or completed file.");
    }
    runner.BadProbe=false;runner.ProbeExit=0;
});
await CheckAsync("pause retains bytes and parts, persisted jobs resume in the same workspace",async()=>{
    var job=Job("pause");runner.Cancel=true;using var cancel=new CancellationTokenSource();runner.CancelSource=cancel;
    await pipelineFake.DownloadAsync(job,null,cancel.Token);Require(job.State==DownloadState.Paused&&job.MediaDownloadedBytes==100,"Pause lost track bytes");
    string work=job.MediaWorkDirectory!;Require(File.Exists(Path.Combine(work,"result.f137.mp4.part")),"Part removed on pause");
    var saved=JsonSerializer.Deserialize<DownloadJob>(JsonSerializer.Serialize(job))!;runner.Cancel=false;
    await pipelineFake.DownloadAsync(saved,null,CancellationToken.None);Require(saved.State==DownloadState.Completed&&saved.MediaWorkDirectory==work,"Resume did not reuse workspace");
});
await CheckAsync("presets let yt-dlp choose tracks and the file is named after the real container",async()=>{
    var job=new DownloadJob{Url=page,Destination=Path.Combine(root,"preset.mp4"),Video=VideoSelection.Preset(720,false)};
    runner.ResultExtension="mkv";runner.Calls.Clear();
    await pipelineFake.DownloadAsync(job,null,CancellationToken.None);
    runner.ResultExtension="mp4";
    Require(job.State==DownloadState.Completed,job.Error);
    var args=runner.Calls[0].ArgumentList;
    Require(args[args.IndexOf("--format")+1]=="bv*+ba/b"&&args[args.IndexOf("--format-sort")+1]=="res:720,ext:mp4:m4a"&&!args.Contains("--merge-output-format"),"Preset arguments wrong");
    Require(job.MediaTracks.SequenceEqual(new[]{"137","140"})&&job.MediaDownloadedBytes>0,"Chosen tracks were not tracked");
    Require(Path.GetExtension(job.Destination)==".mkv"&&File.Exists(job.Destination),"Destination keeps a misleading extension: "+job.Destination);
});
await CheckAsync("referer and browser identity are forwarded to the video tools",async()=>{
    string stream="https://cdn.test/hls/master.m3u8";runner.Calls.Clear();
    var job=new DownloadJob{Url=stream,Destination=Path.Combine(root,"stream.mp4"),Video=new(){FormatId="137+140",Extension="mp4",Label="HLS"},Session=new(){Origin="https://cdn.test/",Referer="https://page.test/watch",UserAgent="TestAgent/1"}};
    await pipelineFake.DownloadAsync(job,null,CancellationToken.None);
    var args=runner.Calls[0].ArgumentList;
    Require(job.State==DownloadState.Completed&&args[args.IndexOf("--referer")+1]=="https://page.test/watch"&&args[args.IndexOf("--user-agent")+1]=="TestAgent/1"&&args[^1]==stream,"Session headers not forwarded");
});
await CheckAsync("an existing destination is preserved",async()=>{
    var job=Job("collision");File.WriteAllText(job.Destination,"existing");await pipelineFake.DownloadAsync(job,null,CancellationToken.None);
    Require(job.State==DownloadState.Failed&&File.ReadAllText(job.Destination)=="existing","Existing file overwritten");
});
Console.WriteLine($"{passed} media checks passed.");
static void Require(bool value,string error){if(!value)throw new Exception(error);}
sealed class InlineProgress(Action<DownloadProgress> report):IProgress<DownloadProgress>{public void Report(DownloadProgress p)=>report(p);}
sealed class LoggingRunner:IMediaProcessRunner
{
 public async Task<MediaProcessResult> RunAsync(ProcessStartInfo start,Action<string>? line,CancellationToken token)
 {
  return await new MediaProcessRunner().RunAsync(start,value=>{if(value.Contains("APEXPROGRESS"))File.AppendAllText(Path.Combine(Environment.CurrentDirectory,".test-tmp","video-progress.log"),value+"\n");line?.Invoke(value);},token);
 }
}
sealed class FakeRunner:IMediaProcessRunner
{
 public List<ProcessStartInfo> Calls{get;}=[];public bool MissingAudio,Cancel,BadProbe;public int ProbeExit;public CancellationTokenSource? CancelSource;public string ResultExtension="mp4";
 public Task<MediaProcessResult> RunAsync(ProcessStartInfo start,Action<string>? line,CancellationToken token)
 {
  Calls.Add(start);
  if(start.FileName.EndsWith("ffprobe.exe"))return Task.FromResult(new MediaProcessResult(ProbeExit,BadProbe?"{\"streams\":[{}]}":MissingAudio?"{\"streams\":[{\"codec_type\":\"video\"}]}":"{\"streams\":[{\"codec_type\":\"video\"},{\"codec_type\":\"audio\"}]}",BadProbe?"Invalid media header":""));
  int index=start.ArgumentList.IndexOf("--output");string output=start.ArgumentList[index+1].Replace("%(ext)s",ResultExtension);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
  line?.Invoke("APEXFORMAT:\"137+140\"");
  line?.Invoke("APEXPROGRESS:{\"format\":\"137\",\"bytes\":100,\"total\":100,\"estimate\":null,\"speed\":50}");
  if(Cancel){File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!,"result.f137.mp4.part"),"retained");CancelSource!.Cancel();throw new OperationCanceledException(token);}
  line?.Invoke("APEXPROGRESS:{\"format\":\"140\",\"bytes\":20,\"total\":20,\"estimate\":null,\"speed\":10}");
  File.WriteAllText(output,"assembled audio/video fixture");line?.Invoke("APEXFILE:"+JsonSerializer.Serialize(output));
  return Task.FromResult(new MediaProcessResult(0,"",""));
 }
}
