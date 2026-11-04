#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskOutput=Join-Path $taskRoot 'installers'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskLog=Join-Path $taskOutput ('build-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.log')
Start-Transcript -LiteralPath $taskLog | Out-Null
try {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {throw 'Install the .NET 10 SDK, then run Build Installer.cmd again.'}
    $taskSdks=& dotnet --list-sdks
    if ($LASTEXITCODE -ne 0 -or !($taskSdks -match '^10\.')) {throw 'The .NET 10 SDK is required (the runtime alone cannot build the app).'}
    foreach ($taskTool in @('ffmpeg.exe','ffprobe.exe','yt-dlp.exe','qjs.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskRoot "video-tools/$taskTool"))) {throw "Missing video-tools/$taskTool. Restore the compact video tools before building; see docs/INSTALLER.md."}
    }
    $taskNode=Get-Command node -ErrorAction SilentlyContinue
    if (!$taskNode) {throw 'Install Node.js to run the extension checks, then run this command again.'}
    Push-Location (Join-Path $taskRoot 'BrowserExtension')
    try {
        $taskTests=@(Get-ChildItem -File -Filter '*.test.js' | Select-Object -ExpandProperty FullName)
        & $taskNode.Source --test @taskTests
        if ($LASTEXITCODE -ne 0) {throw 'Extension checks failed; installer was not built.'}
    } finally {Pop-Location}
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -BootstrapCompiler
    $taskVersion=([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'SwiftFetch/SwiftFetch.csproj') -Raw)).Project.PropertyGroup.Version
    $taskInstaller=Join-Path $taskOutput "Flux-Setup-$taskVersion-win-x64.exe"
    if (!(Test-Path -LiteralPath $taskInstaller)) {throw 'The expected installer was not produced.'}
    Write-Host "`nSUCCESS: $taskInstaller" -ForegroundColor Green
    Write-Host "SHA-256: $taskInstaller.sha256"
    Write-Host "Build log: $taskLog"
} finally {Stop-Transcript | Out-Null}
