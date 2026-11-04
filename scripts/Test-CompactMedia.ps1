#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$CompactTools,[Parameter(Mandatory=$true)][string]$ReferenceFfmpeg)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$CompactTools=(Resolve-Path -LiteralPath $CompactTools).Path
$ReferenceFfmpeg=(Resolve-Path -LiteralPath $ReferenceFfmpeg).Path
$taskFixtures=Join-Path $taskRoot '.test-tmp/compact-media'
New-Item -ItemType Directory -Path $taskFixtures -Force|Out-Null
function Run-Media([string]$exe,[string[]]$arguments){
    $taskStart=[Diagnostics.ProcessStartInfo]::new($exe);$taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
    foreach($taskArgument in $arguments){$taskStart.ArgumentList.Add($taskArgument)}
    $taskProcess=[Diagnostics.Process]::Start($taskStart);$taskOut=$taskProcess.StandardOutput.ReadToEndAsync();$taskErr=$taskProcess.StandardError.ReadToEndAsync()
    if(!$taskProcess.WaitForExit(60000)){throw 'Media fixture command timed out.'}
    $taskText=$taskOut.GetAwaiter().GetResult();$taskErrors=$taskErr.GetAwaiter().GetResult();$taskExit=$taskProcess.ExitCode;$taskProcess.Dispose()
    if($taskExit -ne 0){throw "Media command failed: $taskErrors"}
    return $taskText
}
# Encoding is only used to manufacture fixtures. Apex does stream-copy merging,
# not transcoding, so the large reference binary is never packaged or required.
foreach($taskVideo in @(@('h264.mp4','libopenh264'),@('vp9.webm','libvpx-vp9'),@('av1.mp4','libaom-av1'))){
    $taskFile=Join-Path $taskFixtures $taskVideo[0]
    if(!(Test-Path -LiteralPath $taskFile)){
        $taskArguments=@('-v','error','-f','lavfi','-i','testsrc2=size=160x96:rate=10','-t','2','-an','-c:v',$taskVideo[1],'-threads','1','-b:v','100k')
        if($taskVideo[1] -eq 'libaom-av1'){$taskArguments+=@('-cpu-used','8')}
        if($taskVideo[1] -eq 'libvpx-vp9'){$taskArguments+=@('-deadline','realtime','-cpu-used','8')}
        $null=Run-Media $ReferenceFfmpeg ($taskArguments+@('-y',$taskFile))
    }
}
foreach($taskAudio in @(@('aac.m4a','aac'),@('opus.webm','libopus'),@('adts.aac','aac'))){
    $taskFile=Join-Path $taskFixtures $taskAudio[0]
    if(!(Test-Path -LiteralPath $taskFile)){$null=Run-Media $ReferenceFfmpeg @('-v','error','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','2','-vn','-c:a',$taskAudio[1],'-b:a','64k','-y',$taskFile)}
}
$taskResults=[Collections.Generic.List[object]]::new()
foreach($taskCase in @(@('h264.mp4','aac.m4a','h264-aac.mp4'),@('vp9.webm','opus.webm','vp9-opus.webm'),@('vp9.webm','aac.m4a','vp9-aac.mp4'),@('av1.mp4','aac.m4a','av1-aac.mp4'),@('av1.mp4','opus.webm','av1-opus.mkv'),@('h264.mp4','adts.aac','h264-adts.mp4'))){
    $taskVideoPath=Join-Path $taskFixtures $taskCase[0];$taskAudioPath=Join-Path $taskFixtures $taskCase[1];$taskOutput=Join-Path $taskFixtures $taskCase[2]
    $null=Run-Media (Join-Path $CompactTools 'ffmpeg.exe') @('-v','error','-i',$taskVideoPath,'-i',$taskAudioPath,'-map','0:v:0','-map','1:a:0','-c','copy','-y',$taskOutput)
    $taskInfo=(Run-Media (Join-Path $CompactTools 'ffprobe.exe') @('-v','error','-show_entries','stream=codec_type,codec_name:format=duration','-of','json',$taskOutput))|ConvertFrom-Json
    if(@($taskInfo.streams|Where-Object codec_type -eq 'video').Count -ne 1 -or @($taskInfo.streams|Where-Object codec_type -eq 'audio').Count -ne 1){throw 'Merged file lost an audio/video stream.'}
    $taskHashArgs=@('-v','error','-select_streams','v:0','-show_packets','-show_entries','packet=data_hash','-show_data_hash','sha256','-of','csv=p=0')
    $taskInputHashes=Run-Media (Join-Path $CompactTools 'ffprobe.exe') ($taskHashArgs+@($taskVideoPath))
    $taskOutputHashes=Run-Media (Join-Path $CompactTools 'ffprobe.exe') ($taskHashArgs+@($taskOutput))
    if(!$taskInputHashes.Contains('SHA256:') -or $taskInputHashes -ne $taskOutputHashes){throw 'Video packets changed during stream-copy merge.'}
    $taskResults.Add([pscustomobject]@{case=$taskCase[2];codecs=@($taskInfo.streams.codec_name);bytes=(Get-Item -LiteralPath $taskOutput).Length;videoPacketsUnchanged=$true})
    Write-Output ('PASS compact merge/probe with unchanged video packets: '+$taskCase[2])
}
$taskTransport=Join-Path $taskFixtures 'h264-aac.ts'
$null=Run-Media $ReferenceFfmpeg @('-v','error','-i',(Join-Path $taskFixtures 'h264-aac.mp4'),'-c','copy','-f','mpegts','-y',$taskTransport)
$taskRemux=Join-Path $taskFixtures 'transport-remux.mp4'
$null=Run-Media (Join-Path $CompactTools 'ffmpeg.exe') @('-v','error','-i',$taskTransport,'-c','copy','-y',$taskRemux)
$taskInfo=(Run-Media (Join-Path $CompactTools 'ffprobe.exe') @('-v','error','-show_entries','stream=codec_type','-of','json',$taskRemux))|ConvertFrom-Json
if(@($taskInfo.streams.codec_type) -notcontains 'video' -or @($taskInfo.streams.codec_type) -notcontains 'audio'){throw 'MPEG-TS/HLS-style input lost audio or video.'}
$taskResults.Add([pscustomobject]@{case='mpeg-ts-hls-style-remux';streams=@($taskInfo.streams.codec_type);bytes=(Get-Item -LiteralPath $taskRemux).Length})
Write-Output 'PASS compact MPEG-TS/HLS-style input remuxes to MP4 with audio and video'
$taskResults|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRoot 'docs/measurements/compact-media-validation.json') -Encoding utf8
