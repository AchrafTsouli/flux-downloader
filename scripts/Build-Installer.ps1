[CmdletBinding()]
param([string]$CompilerPath = '', [switch]$BootstrapCompiler, [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskStage = Join-Path $taskRoot 'artifacts/installer/win-x64'
$taskOutput = Join-Path $taskRoot 'installers'
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'SwiftFetch/SwiftFetch.csproj') -Raw)).Project.PropertyGroup.Version
if (!$taskVersion -or $taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a numeric application version.' }
if (!$CompilerPath) {
    $taskCandidates = @((Join-Path $taskRoot '.tools/InnoSetup/ISCC.exe'), "${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe", "$env:ProgramFiles/Inno Setup 6/ISCC.exe")
    $CompilerPath = $taskCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CompilerPath -and $BootstrapCompiler) { $CompilerPath = & (Join-Path $PSScriptRoot 'Install-InnoCompiler.ps1') }
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath)) { throw 'Install Inno Setup 6.7.3, pass -CompilerPath, or use -BootstrapCompiler.' }
$env:DOTNET_CLI_HOME = Join-Path $taskRoot '.dotnet-home'
Push-Location $taskRoot
try {
    if (!$SkipPublish) {
        # A fresh, dedicated staging folder prevents old framework-dependent files
        # and development state from leaking into the installer.
        $taskResolved = [IO.Path]::GetFullPath($taskStage)
        $taskExpected = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/installer/win-x64'))
        if ($taskResolved -ne $taskExpected -or !$taskResolved.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging path.' }
        $taskAncestor = $taskResolved
        while ($taskAncestor -and $taskAncestor -ne $taskRoot) {
            if ((Test-Path -LiteralPath $taskAncestor) -and ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Refusing linked staging path.' }
            $taskAncestor = Split-Path -Parent $taskAncestor
        }
        if (Test-Path -LiteralPath $taskResolved) { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
        # Flux exposes English/French. Keep French framework translations plus
        # invariant English, rather than bundling twelve unused WPF translations.
        dotnet publish SwiftFetch/SwiftFetch.csproj -c Release -r win-x64 --self-contained true -o $taskStage -m:1 -nodeReuse:false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:PublishTrimmed=false -p:SatelliteResourceLanguages=fr -p:DebugType=None
        if ($LASTEXITCODE -ne 0) { throw 'Self-contained compressed publish failed.' }
        # One executable serves both desktop and Chrome native messaging. Keep
        # WPF/SQLite native DLLs outside the bundle: no runtime extraction cache.
        Rename-Item -LiteralPath (Join-Path $taskStage 'SwiftFetch.exe') -NewName 'Flux.exe'
        New-Item -ItemType Directory -Path (Join-Path $taskStage 'BrowserExtension'),(Join-Path $taskStage 'video-tools') -Force | Out-Null
        Get-ChildItem -LiteralPath 'BrowserExtension' -File | Where-Object { $_.Name -ne 'package.json' -and $_.Name -notlike '*.test.js' } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskStage 'BrowserExtension') -Force
        }
        Get-ChildItem -LiteralPath 'video-tools' -File | Where-Object { $_.Extension -in @('.exe','.dll','.txt','.json') -and $_.Name -notin @('node.exe','NODE-LICENSE.txt') } | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $taskStage 'video-tools') -Force
        }
        Copy-Item -LiteralPath 'SwiftFetch/app.ico','SwiftFetch/app-icon.png','installer/Browser-Setup.html','installer/INSTALLATION.txt','scripts/Install-BrowserBridge.ps1' -Destination $taskStage -Force
        $taskRuntimeOptions = (Get-Content -LiteralPath (Join-Path $taskRoot 'SwiftFetch/bin/Release/net10.0-windows/win-x64/SwiftFetch.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
        if ($taskRuntimeOptions.framework -or $taskRuntimeOptions.frameworks -or !$taskRuntimeOptions.includedFrameworks) { throw 'The bundle must include its own .NET runtime.' }
        $taskRuntime = $taskRuntimeOptions.includedFrameworks
        $taskNuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:DOTNET_CLI_HOME '.nuget/packages' }
        New-Item -ItemType Directory -Path (Join-Path $taskStage 'licenses') -Force | Out-Null
        foreach ($taskFramework in $taskRuntime) {
            $taskPackage = Join-Path $taskNuget ($taskFramework.name.ToLowerInvariant() + '.runtime.win-x64/' + $taskFramework.version)
            $taskLicense = Get-ChildItem -LiteralPath $taskPackage -File | Where-Object { $_.Name -like 'LICENSE*' -or $_.Name -like '*NOTICE*' }
            if (!$taskLicense) { throw "Missing runtime license: $taskPackage" }
            foreach ($taskNotice in $taskLicense) { Copy-Item -LiteralPath $taskNotice.FullName -Destination (Join-Path $taskStage ('licenses/' + $taskFramework.name + '-' + $taskNotice.Name)) -Force }
        }
        @{selfContained=$true;compressedManagedAssemblies=$true;nativeLibrariesExtracted=$false;includedFrameworks=$taskRuntime;applicationVersion=$taskVersion} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskStage 'deployment.json') -Encoding utf8
    }
    foreach ($taskRequired in @('Flux.exe','wpfgfx_cor3.dll','PresentationNative_cor3.dll','e_sqlite3.dll','BrowserExtension/manifest.json','video-tools/ffmpeg.exe','video-tools/ffprobe.exe','video-tools/yt-dlp.exe','video-tools/qjs.exe','Browser-Setup.html','deployment.json')) {
        if (!(Test-Path -LiteralPath (Join-Path $taskStage $taskRequired))) { throw "Incomplete self-contained payload: $taskRequired" }
    }
    $taskManifest = Get-Content -LiteralPath (Join-Path $taskStage 'BrowserExtension/manifest.json') -Raw | ConvertFrom-Json
    $taskHash = [Security.Cryptography.SHA256]::Create()
    try { $taskKeyHash = $taskHash.ComputeHash([Convert]::FromBase64String($taskManifest.key)) } finally { $taskHash.Dispose() }
    $taskHex = [BitConverter]::ToString($taskKeyHash).Replace('-','').Substring(0,32).ToLowerInvariant()
    $taskId = -join ($taskHex.ToCharArray() | ForEach-Object { [char](97 + [Convert]::ToInt32([string]$_,16)) })
    # Include an inventory for diagnostics; never include the local history/database.
    $taskPrefix = $taskStage.TrimEnd('\') + '\'
    $taskInventory = @(Get-ChildItem -LiteralPath $taskStage -File -Recurse | Where-Object Name -ne 'payload-sha256.json' | ForEach-Object {
        [pscustomobject]@{path=$_.FullName.Substring($taskPrefix.Length);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
    })
    $taskInventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskStage 'payload-sha256.json') -Encoding utf8
    $taskPayloadBytes=(Get-ChildItem -LiteralPath $taskStage -File -Recurse | Measure-Object Length -Sum).Sum
    if($taskPayloadBytes -gt 100000000){throw ('Installed payload exceeds the 100 MB target: {0:N2} MB' -f ($taskPayloadBytes/1000000))}
    Write-Output ('Installed payload: {0:N2} MB, including video tools and the private .NET runtime.' -f ($taskPayloadBytes/1000000))
    New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
    Write-Output 'Compressing the self-contained application and bundled video tools...'
    & $CompilerPath /Q "/DPayloadDir=$taskStage" "/DOutputDir=$taskOutput" "/DAppVersion=$taskVersion" "/DExtensionId=$taskId" (Join-Path $taskRoot 'installer/ApexDownloader.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $taskInstaller = Join-Path $taskOutput "Flux-Setup-$taskVersion-win-x64.exe"
    $taskInstallerHash = (Get-FileHash -LiteralPath $taskInstaller).Hash.ToLowerInvariant()
    "$taskInstallerHash  $([IO.Path]::GetFileName($taskInstaller))" | Set-Content -LiteralPath "$taskInstaller.sha256" -Encoding ascii
    Write-Output "Installer ready: $taskInstaller"
} finally { Pop-Location }
