# Restores the bundled video tools (yt-dlp, FFmpeg/FFprobe, QuickJS) into video-tools/ from the
# repository's "video-tools" release, then checks every file's size and SHA-256 against
# video-tools/sources.json. Run once after cloning, before building the installer.
param(
  [string]$Repository,
  [string]$Tag = 'video-tools'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'video-tools'
$manifest = Get-Content -LiteralPath (Join-Path $target 'sources.json') -Raw | ConvertFrom-Json

function Test-Tools {
  foreach ($file in $manifest.files) {
    $path = Join-Path $target $file.name
    if (!(Test-Path -LiteralPath $path)) { return $false }
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes) { return $false }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256.ToLowerInvariant()) { return $false }
  }
  return $true
}
if (Test-Tools) { Write-Output 'Video tools are present and verified.'; exit 0 }

if (!$Repository) {
  $origin = git -C $root remote get-url origin 2>$null
  if ($origin -match 'github\.com[:/](.+?)(\.git)?$') { $Repository = $Matches[1] } else { throw 'Pass -Repository owner/name (no GitHub origin remote found).' }
}
$zip = Join-Path ([IO.Path]::GetTempPath()) ('flux-video-tools-' + [guid]::NewGuid().ToString('N') + '.zip')
$url = "https://github.com/$Repository/releases/download/$Tag/video-tools.zip"
Write-Output "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
try {
  $staging = Join-Path ([IO.Path]::GetTempPath()) ('flux-video-tools-' + [guid]::NewGuid().ToString('N'))
  Expand-Archive -LiteralPath $zip -DestinationPath $staging
  foreach ($file in $manifest.files) {
    $source = Join-Path $staging $file.name
    if (!(Test-Path -LiteralPath $source)) { throw "The archive is missing $($file.name)." }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $file.sha256.ToLowerInvariant()) { throw "$($file.name) does not match its recorded SHA-256. Nothing was installed." }
  }
  foreach ($file in $manifest.files) { Copy-Item -LiteralPath (Join-Path $staging $file.name) -Destination $target -Force }
  if (!(Test-Tools)) { throw 'Verification failed after copying the video tools.' }
  Write-Output "Video tools restored and verified in $target"
}
finally {
  Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
  if ($staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
