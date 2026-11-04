#requires -Version 7.4
[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot '../video-tools'))
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$Destination=[IO.Path]::GetFullPath($Destination)
$taskBuildTools=Join-Path $taskRoot '.tools/media-build'
$taskDownloads=Join-Path $taskRoot '.tools/compact-video'
New-Item -ItemType Directory -Path $taskBuildTools,$taskDownloads,$Destination -Force|Out-Null
function Get-Asset([string]$url,[string]$hash,[string]$file){
    if(!(Test-Path -LiteralPath $file)) {Invoke-WebRequest -Uri $url -OutFile $file -TimeoutSec 180 -MaximumRetryCount 2}
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash){throw "Verified download mismatch: $file"}
}
$taskSourceHash='464beb5e7bf0c311e68b45ae2f04e9cc2af88851abb4082231742a74d97b524c'
$taskFfmpegSource=Join-Path $taskBuildTools 'ffmpeg-8.1.2'
$taskCompiler=Join-Path $taskBuildTools 'llvm-mingw-20261006-ucrt-x86_64'
$taskMsys=Join-Path $taskBuildTools 'msys64'
$taskPrefix=Join-Path $taskRoot 'artifacts/compact-ffmpeg'
$taskBuild=Join-Path $taskRoot 'artifacts/ffmpeg-build'
$taskScriptHash=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Build-CompactFFmpeg.sh')).Hash
$taskStamp=Join-Path $taskPrefix 'build-script.sha256'
$taskNeedBuild=!(Test-Path -LiteralPath (Join-Path $taskPrefix 'bin/ffmpeg.exe')) -or !(Test-Path -LiteralPath $taskStamp) -or (Get-Content -LiteralPath $taskStamp -Raw -ErrorAction SilentlyContinue).Trim() -ne $taskScriptHash
if($taskNeedBuild){
    $taskAssets=@(
        @{name='llvm.zip';url='https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-x86_64.zip';hash='317492c456aa27ee607a5919f1d2d38dcdc1112516a24d0bf4b00d078f52d17a';folder=$taskCompiler},
        @{name='msys.tar.xz';url='https://github.com/msys2/msys2-installer/releases/download/2026-09-27/msys2-base-x86_64-20260927.tar.xz';hash='ea2f31a0b6ade63914ce441ffb022f0f6aa96982bfefa2326460a26d5fb01322';folder=$taskMsys},
        @{name='ffmpeg.tar.xz';url='https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz';hash=$taskSourceHash;folder=$taskFfmpegSource}
    )
    foreach($taskAsset in $taskAssets){
        $taskArchive=Join-Path $taskBuildTools $taskAsset.name
        Get-Asset $taskAsset.url $taskAsset.hash $taskArchive
        if(!(Test-Path -LiteralPath $taskAsset.folder)){tar -xf $taskArchive -C $taskBuildTools;if($LASTEXITCODE -ne 0){throw 'Build tool extraction failed.'}}
    }
    $taskBash=Join-Path $taskMsys 'usr/bin/bash.exe'
    $env:MSYSTEM='MSYS';$env:CHERE_INVOKING='1'
    if(!(Test-Path -LiteralPath (Join-Path $taskMsys 'usr/bin/make.exe')) -or !(Test-Path -LiteralPath (Join-Path $taskMsys 'usr/bin/cmp.exe')) -or !(Test-Path -LiteralPath (Join-Path $taskMsys 'usr/bin/pkgconf.exe'))){& $taskBash -lc 'pacman -Sy --noconfirm --needed make diffutils pkgconf';if($LASTEXITCODE -ne 0){throw 'Signed MSYS2 build utility installation failed.'}}
    & $taskBash (Join-Path $PSScriptRoot 'Build-CompactFFmpeg.sh') $taskFfmpegSource $taskCompiler $taskBuild $taskPrefix
    if($LASTEXITCODE -ne 0){throw 'Compact FFmpeg build failed.'}
    $taskScriptHash|Set-Content -LiteralPath $taskStamp -Encoding ascii
}
$taskQuickHash='2aeabf0092c3262d6b2609824418f7dd7ed1f1df939f73b2b15645230cac0d77'
$taskYtHash='66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a'
Get-Asset 'https://github.com/quickjs-ng/quickjs/releases/download/v0.17.0/qjs-windows-x86_64.exe' $taskQuickHash (Join-Path $taskDownloads 'qjs.exe')
$taskYt=Join-Path $taskRoot 'video-tools/yt-dlp.exe'
if(!(Test-Path -LiteralPath $taskYt) -or (Get-FileHash -LiteralPath $taskYt).Hash -ne $taskYtHash){$taskYt=Join-Path $taskDownloads 'yt-dlp.exe';Get-Asset 'https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe' $taskYtHash $taskYt}
if(![string]::Equals([IO.Path]::GetFullPath($taskYt),(Join-Path $Destination 'yt-dlp.exe'),[StringComparison]::OrdinalIgnoreCase)){Copy-Item -LiteralPath $taskYt -Destination (Join-Path $Destination 'yt-dlp.exe') -Force}
Copy-Item -LiteralPath (Join-Path $taskDownloads 'qjs.exe') -Destination $Destination -Force
Get-ChildItem -LiteralPath (Join-Path $taskPrefix 'bin') -File | Where-Object Extension -in @('.exe','.dll')|ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination $Destination -Force}
Copy-Item -LiteralPath (Join-Path $taskFfmpegSource 'COPYING.LGPLv2.1') -Destination (Join-Path $Destination 'FFMPEG-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build-CompactFFmpeg.sh') -Destination (Join-Path $Destination 'FFMPEG-BUILD.txt') -Force
Invoke-WebRequest 'https://raw.githubusercontent.com/quickjs-ng/quickjs/v0.17.0/LICENSE' -OutFile (Join-Path $Destination 'QUICKJS-LICENSE.txt') -TimeoutSec 60
Invoke-WebRequest 'https://raw.githubusercontent.com/yt-dlp/yt-dlp/2026.08.19/LICENSE' -OutFile (Join-Path $Destination 'YT-DLP-LICENSE.txt') -TimeoutSec 60
@"
FFmpeg 8.1.2, LGPL-2.1-or-later, shared libraries; no GPL/nonfree components.
Complete corresponding source: https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz
Source SHA256: $taskSourceHash
Build: scripts/Build-CompactFFmpeg.sh (SHA256: $taskScriptHash).
Local source fix: mov_read_chnl returns ff_mov_read_chnl's status rather than an
uninitialized variable; the one-line patch is applied by the build script.
The shipped DLLs can be replaced/relinked with an ABI-compatible FFmpeg 8 build.
Only packet-copy merging, containers, parsers, bitstream filters and networking
used by Apex are included. Apex does not offer transcoding or video editing.
Build-only toolchain: LLVM-MinGW 20261006 UCRT x64; MSYS2 2026-09-27 + GNU Make.
Rebuild using scripts/Install-VideoTools.ps1. No compiler is needed by end users.
"@|Set-Content -LiteralPath (Join-Path $Destination 'FFMPEG-SOURCE.txt') -Encoding utf8
$taskFiles=@(Get-ChildItem -LiteralPath $Destination -File|Where-Object Extension -in @('.exe','.dll')|Where-Object Name -ne 'node.exe'|ForEach-Object{[pscustomobject]@{name=$_.Name;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
@{ytDlp=@{version='2026.08.19';sha256=$taskYtHash;url='https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe'};quickjs=@{version='0.17.0';sha256=$taskQuickHash;url='https://github.com/quickjs-ng/quickjs/releases/download/v0.17.0/qjs-windows-x86_64.exe'};ffmpeg=@{version='8.1.2';sourceSha256=$taskSourceHash;url='https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz';configurationSha256=$taskScriptHash};files=$taskFiles}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Destination 'sources.json') -Encoding utf8
foreach($taskObsolete in @('node.exe','NODE-LICENSE.txt')){$taskOld=Join-Path $Destination $taskObsolete;if(Test-Path -LiteralPath $taskOld){Remove-Item -LiteralPath $taskOld -Force}}
Write-Output ('Compact video tools ready: {0:N2} MB' -f ((Get-ChildItem -LiteralPath $Destination -File|Measure-Object Length -Sum).Sum/1000000))
