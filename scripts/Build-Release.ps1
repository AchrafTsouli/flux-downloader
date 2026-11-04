$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $workspace
try {
    $taskReleasePaths = @((Join-Path $workspace 'dist/SwiftFetch.exe'), (Join-Path $workspace 'dist/Flux.exe'))
    function Send-AppAction([string]$action) {
        $taskIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        try { $taskInstanceText = $taskIdentity.User.Value + '|' + (Join-Path $workspace 'dist').TrimEnd([IO.Path]::DirectorySeparatorChar).ToUpperInvariant() }
        finally { $taskIdentity.Dispose() }
        $taskHasher = [Security.Cryptography.SHA256]::Create()
        try { $taskInstanceHash = [BitConverter]::ToString($taskHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($taskInstanceText))).Replace('-','').Substring(0,24) } finally { $taskHasher.Dispose() }
        $taskPipe = [IO.Pipes.NamedPipeClientStream]::new('.',('SwiftFetch-' + $taskInstanceHash),[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
        $taskTimeout = [Threading.CancellationTokenSource]::new(5000)
        function Read-AppBytes([byte[]]$buffer) {
            $taskOffset = 0
            while ($taskOffset -lt $buffer.Length) {
                $taskRead = $taskPipe.ReadAsync($buffer,$taskOffset,($buffer.Length-$taskOffset),$taskTimeout.Token).GetAwaiter().GetResult()
                if ($taskRead -eq 0) { throw 'The application disconnected.' }
                $taskOffset += $taskRead
            }
        }
        try {
            $taskPipe.Connect(2000)
            $taskBody = [Text.Encoding]::UTF8.GetBytes((@{action=$action} | ConvertTo-Json -Compress))
            $taskHeader = [BitConverter]::GetBytes([int]$taskBody.Length)
            $taskPipe.Write($taskHeader,0,4); $taskPipe.Write($taskBody,0,$taskBody.Length); $taskPipe.Flush()
            $taskReplyHeader = [byte[]]::new(4); Read-AppBytes $taskReplyHeader
            $taskReplyLength = [BitConverter]::ToInt32($taskReplyHeader)
            if ($taskReplyLength -lt 1 -or $taskReplyLength -gt 65536) { return $false }
            $taskReply = [byte[]]::new($taskReplyLength); Read-AppBytes $taskReply
            return ([Text.Encoding]::UTF8.GetString($taskReply) | ConvertFrom-Json).ok
        } catch { return $false } finally { $taskTimeout.Dispose(); $taskPipe.Dispose() }
    }
    foreach ($taskProcess in @(Get-Process -Name SwiftFetch, ApexDownloader -ErrorAction SilentlyContinue)) {
        if ($taskProcess.Path -in $taskReleasePaths) {
            $taskQuitAccepted = Send-AppAction 'quit'
            if (-not $taskQuitAccepted) { $null = Send-AppAction 'show'; $taskProcess.Refresh(); $taskQuitAccepted = $taskProcess.CloseMainWindow() }
            if (-not $taskQuitAccepted -or -not $taskProcess.WaitForExit(30000)) {
                throw 'Flux is still running. Use Tasks → Exit so downloads can be saved before updating.'
            }
        }
    }
    $env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
    foreach ($taskVideoTool in @('yt-dlp.exe','ffmpeg.exe','ffprobe.exe','qjs.exe')) {
        if (!(Test-Path -LiteralPath (Join-Path 'video-tools' $taskVideoTool))) { throw 'Run scripts/Install-VideoTools.ps1 before building the video-enabled release.' }
    }
    foreach ($project in @('SwiftFetch/SwiftFetch.csproj','SwiftFetch.NativeHost/SwiftFetch.NativeHost.csproj')) {
        dotnet publish $project -c Release -o dist -m:1 -nodeReuse:false --ignore-failed-sources
        if ($LASTEXITCODE -ne 0) { throw "Failed to publish $project" }
    }
    New-Item -ItemType Directory -Path 'dist/BrowserExtension' -Force | Out-Null
    New-Item -ItemType Directory -Path 'dist/video-tools' -Force | Out-Null
    Get-ChildItem -LiteralPath 'video-tools' -File | Where-Object { $_.Extension -in @('.exe','.dll','.txt','.json') } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination 'dist/video-tools' -Force
    }
    foreach ($taskLegacyTool in @('node.exe','NODE-LICENSE.txt')) {
        $taskLegacyPath=Join-Path $workspace ('dist/video-tools/'+$taskLegacyTool)
        if(Test-Path -LiteralPath $taskLegacyPath){Remove-Item -LiteralPath $taskLegacyPath -Force}
    }
    foreach ($name in @('manifest.json','background.js','transfer.js','session.js','link-policy.js','capture-policy.js','click-capture.js','media-policy.js','media-background.js','video-cache.js','player-catalog.js','video-panel-policy.js','video-panel.js','popup.html','popup.css','popup.js','icon-16.png','icon-32.png','icon-48.png','icon-128.png')) {
        Copy-Item -LiteralPath (Join-Path 'BrowserExtension' $name) -Destination 'dist/BrowserExtension' -Force
    }
    Copy-Item -LiteralPath 'scripts/Install-BrowserBridge.ps1','scripts/Uninstall-BrowserBridge.ps1','scripts/Install Browser Integration.cmd' -Destination 'dist' -Force
    Copy-Item -LiteralPath 'SwiftFetch/app.ico' -Destination 'dist/app.ico' -Force
    & ./scripts/Install-BrowserBridge.ps1 -NoRegister
    # Mirror files to ApexDownloader.* so either name runs identically
    foreach ($ext in @('.exe', '.dll', '.deps.json', '.runtimeconfig.json', '.pdb')) {
        $src = Join-Path 'dist' ("SwiftFetch" + $ext)
        $dst = Join-Path 'dist' ("ApexDownloader" + $ext)
        if (Test-Path $src) { Copy-Item -LiteralPath $src -Destination $dst -Force }
    }
    
    # Update Desktop shortcut to point to Flux.exe
    $desktopPath = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop)
    $shortcutPath = Join-Path $desktopPath 'Flux.lnk'
    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = (Resolve-Path (Join-Path 'dist' 'Flux.exe')).Path
    $shortcut.WorkingDirectory = (Resolve-Path 'dist').Path
    $shortcut.IconLocation = $shortcut.TargetPath + ",0"
    $shortcut.Description = "Flux - High-Speed Download Accelerator"
    $shortcut.Save()
    Write-Output 'Release ready in dist.'
} finally { Pop-Location }
