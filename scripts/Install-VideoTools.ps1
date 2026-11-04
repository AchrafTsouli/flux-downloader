param([string]$Destination = (Join-Path $PSScriptRoot '../video-tools'), [switch]$FullTools)
$ErrorActionPreference = 'Stop'
if (!$FullTools) { & (Join-Path $PSScriptRoot 'Install-CompactVideoTools.ps1') -Destination $Destination; return }
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$taskCache = Join-Path $Destination 'packages'
New-Item -ItemType Directory -Path $taskCache -Force | Out-Null
function Get-VerifiedAsset($Url, $Hash, $File) {
    if (!(Test-Path -LiteralPath $File) -or (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Hash) {
        Write-Output "Downloading $([IO.Path]::GetFileName($File))"
        Invoke-WebRequest -Uri $Url -OutFile $File
    }
    if ((Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash -ne $Hash) { throw "SHA-256 verification failed: $File" }
}
$taskYt = Invoke-RestMethod 'https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest'
$taskYtAsset = $taskYt.assets | Where-Object name -eq 'yt-dlp.exe'
if ($taskYtAsset.digest -notmatch '^sha256:([a-f0-9]{64})$') { throw 'No yt-dlp SHA-256 digest provided.' }
$taskYtHash = $Matches[1]
Get-VerifiedAsset $taskYtAsset.browser_download_url $taskYtHash (Join-Path $Destination 'yt-dlp.exe')
$taskFf = Invoke-RestMethod 'https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest'
$taskFfAsset = $taskFf.assets | Where-Object name -eq 'ffmpeg-master-latest-win64-lgpl.zip'
if ($taskFfAsset.digest -notmatch '^sha256:([a-f0-9]{64})$') { throw 'No FFmpeg SHA-256 digest provided.' }
$taskFfHash = $Matches[1]
$taskFfZip = Join-Path $taskCache 'ffmpeg-win64-lgpl.zip'
Get-VerifiedAsset $taskFfAsset.browser_download_url $taskFfHash $taskFfZip
$taskFfExtract = Join-Path $taskCache 'ffmpeg'
Expand-Archive -LiteralPath $taskFfZip -DestinationPath $taskFfExtract -Force
foreach ($taskTool in @('ffmpeg.exe','ffprobe.exe')) {
    $taskSource = Get-ChildItem -LiteralPath $taskFfExtract -Filter $taskTool -Recurse | Select-Object -First 1
    if (!$taskSource) { throw "Missing $taskTool in archive" }
    Copy-Item -LiteralPath $taskSource.FullName -Destination $Destination -Force
}
$taskNodeIndex = Invoke-RestMethod 'https://nodejs.org/dist/index.json'
$taskNode = $taskNodeIndex | Where-Object { $_.version -like 'v24.*' -and $_.lts } | Select-Object -First 1
if (!$taskNode) { throw 'No Node 24 LTS release found.' }
$taskNodeArchive = "node-$($taskNode.version)-win-x64.zip"
$taskNodeBase = "https://nodejs.org/dist/$($taskNode.version)"
$taskNodeSums = (Invoke-WebRequest "$taskNodeBase/SHASUMS256.txt").Content
$taskNodeMatch = [regex]::Match($taskNodeSums,"(?m)^([a-f0-9]{64})\s+$([regex]::Escape($taskNodeArchive))\r?$ ".TrimEnd())
if (!$taskNodeMatch.Success) { throw 'No Node SHA-256 checksum provided.' }
$taskNodeHash = $taskNodeMatch.Groups[1].Value
$taskNodeZip = Join-Path $taskCache $taskNodeArchive
Get-VerifiedAsset "$taskNodeBase/$taskNodeArchive" $taskNodeHash $taskNodeZip
$taskNodeExtract = Join-Path $taskCache 'node'
Expand-Archive -LiteralPath $taskNodeZip -DestinationPath $taskNodeExtract -Force
$taskNodeFolder = Join-Path $taskNodeExtract "node-$($taskNode.version)-win-x64"
Copy-Item -LiteralPath (Join-Path $taskNodeFolder 'node.exe') -Destination $Destination -Force
Copy-Item -LiteralPath (Join-Path $taskNodeFolder 'LICENSE') -Destination (Join-Path $Destination 'NODE-LICENSE.txt') -Force
Invoke-WebRequest "https://raw.githubusercontent.com/yt-dlp/yt-dlp/$($taskYt.tag_name)/LICENSE" -OutFile (Join-Path $Destination 'YT-DLP-LICENSE.txt')
$taskFfLicense = Get-ChildItem -LiteralPath $taskFfExtract -Filter 'LICENSE.txt' -Recurse | Select-Object -First 1
if ($taskFfLicense) { Copy-Item -LiteralPath $taskFfLicense.FullName -Destination (Join-Path $Destination 'FFMPEG-LICENSE.txt') -Force }
@{ ytDlp = @{ version=$taskYt.tag_name; url=$taskYtAsset.browser_download_url; sha256=$taskYtHash }; ffmpeg=@{url=$taskFfAsset.browser_download_url;sha256=$taskFfHash}; node=@{version=$taskNode.version;url="$taskNodeBase/$taskNodeArchive";sha256=$taskNodeHash} } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Destination 'sources.json') -Encoding utf8
Write-Output "Video tools ready: $Destination"
