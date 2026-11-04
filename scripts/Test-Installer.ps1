#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InstallerPath,[string]$PreviousInstallerPath='')
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$taskUninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{F14B83CB-285D-459F-9E24-57D4E44F6D56}_is1'
if ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($taskUninstallKey)) { throw 'An installed Flux copy exists. This isolated test will not replace it.' }
$taskTestRoot = Join-Path $taskRoot ('.test-tmp/installer-' + [guid]::NewGuid().ToString('N'))
$taskInstall = Join-Path $taskTestRoot 'Flux Téléchargements'
$taskGroup = 'Flux Installer Test ' + [guid]::NewGuid().ToString('N')
New-Item -ItemType Directory -Path $taskTestRoot -Force | Out-Null
$taskRegistry = [Collections.Generic.List[object]]::new()
foreach ($taskView in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
    $taskBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$taskView)
    foreach ($taskBrowser in @('Software\Google\Chrome','Software\BraveSoftware\Brave-Browser')) {
        $taskKeyName = "$taskBrowser\NativeMessagingHosts\com.swiftfetch.bridge"
        $taskKey = $taskBase.OpenSubKey($taskKeyName)
        try { $taskRegistry.Add([pscustomobject]@{view=$taskView;key=$taskKeyName;existed=($null -ne $taskKey);value=if($taskKey){$taskKey.GetValue('')}else{$null}}) }
        finally { if($taskKey){$taskKey.Dispose()} }
    }
    $taskBase.Dispose()
}
$taskRunKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
try { $taskOldRun = if($taskRunKey){$taskRunKey.GetValue('ApexDownloader')}else{$null} } finally { if($taskRunKey){$taskRunKey.Dispose()} }
$taskState = Join-Path $env:LOCALAPPDATA 'SwiftFetch'
function Get-StateHashes {
    if (Test-Path -LiteralPath $taskState) {
        # SQLite readers update shared-memory coordination even in ReadOnly mode.
        # Compare durable history and nonempty WAL data, not transient lock files
        # or the creation/removal of an empty WAL by a concurrent browser reader.
        @(Get-ChildItem -LiteralPath $taskState -File | Where-Object { $_.Name -like 'history*' -and $_.Name -notlike '*-shm' -and !($_.Name -like '*-wal' -and $_.Length -eq 0) } | Sort-Object Name | ForEach-Object { $_.Name + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join '|'
    } else { '' }
}
$taskBefore = Get-StateHashes
$taskInstalled = $false
$taskPassed = [Collections.Generic.List[string]]::new()
function Require([bool]$condition,[string]$message) { if(!$condition){throw $message} }
function Run-Setup([string]$logName) {
    $taskProcess = Start-Process -FilePath $InstallerPath -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/LANG=french',('/DIR="' + $taskInstall + '"'),('/GROUP="' + $taskGroup + '"'),'/TASKS="browserintegration,startup"',('/LOG="' + (Join-Path $taskTestRoot $logName) + '"')) -WindowStyle Hidden -PassThru
    if (!$taskProcess.WaitForExit(180000)) { throw 'Installer test timed out. Its process was left intact for inspection.' }
    Require ($taskProcess.ExitCode -eq 0) "Installer exited with $($taskProcess.ExitCode). See $taskTestRoot."
}
function Run-Diagnostic {
    $taskStart = [Diagnostics.ProcessStartInfo]::new((Join-Path $taskInstall 'Flux.exe'))
    $taskStart.UseShellExecute=$false; $taskStart.CreateNoWindow=$true
    $taskStart.ArgumentList.Add('--verify-installation')
    $taskStart.Environment['DOTNET_ROOT'] = Join-Path $taskTestRoot 'no-system-runtime'
    $taskStart.Environment['DOTNET_ROOT_X64'] = Join-Path $taskTestRoot 'no-system-runtime'
    $taskStart.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $taskStart.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $taskTestRoot 'unexpected-runtime-extraction'
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    if(!$taskProcess.WaitForExit(30000)){throw 'Installation diagnostic timed out.'}
    Require ($taskProcess.ExitCode -eq 0) 'Installed WPF / SQLite / encrypted storage diagnostic failed. See %TEMP%/Flux-installation-check.log.'
    $taskProcess.Dispose()
}
try {
    if($PreviousInstallerPath){
        $taskPrevious=(Resolve-Path -LiteralPath $PreviousInstallerPath).Path
        $taskOld=Start-Process -FilePath $taskPrevious -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART',('/DIR="'+$taskInstall+'"'),'/TASKS="browserintegration,startup"',('/LOG="'+(Join-Path $taskTestRoot 'legacy-install.log')+'"')) -WindowStyle Hidden -PassThru
        Require ($taskOld.WaitForExit(180000) -and $taskOld.ExitCode -eq 0) 'Legacy installer fixture failed.'
        $taskInstalled=$true
        [IO.File]::WriteAllText((Join-Path $taskInstall 'migration-user-file.txt'),'preserve legacy user file')
    }
    Run-Setup 'install.log'; $taskInstalled=$true
    if($PreviousInstallerPath){
        Require (Test-Path -LiteralPath (Join-Path $taskInstall 'migration-user-file.txt')) 'Migration deleted an untracked user file.'
        foreach($taskOldFile in @('SwiftFetch.NativeHost.exe','SwiftFetch.exe','SwiftFetch.dll','coreclr.dll','video-tools/node.exe')){Require (!(Test-Path -LiteralPath (Join-Path $taskInstall $taskOldFile))) ('Migration retained obsolete large files: '+$taskOldFile)}
        $taskPassed.Add('Upgrade from the previous package removes obsolete binaries and preserves user files')
    }
    # Inno may allocate unins001 during migration while the old remover exits.
    $taskUninstallers=@(Get-ChildItem -LiteralPath $taskInstall -File -Filter 'unins*.exe' | Where-Object Name -Match '^unins\d{3}\.exe$')
    Require ($taskUninstallers.Count -eq 1) 'Expected exactly one installed uninstaller.'
    $taskUninstaller=$taskUninstallers[0].FullName
    $taskManifestPath = Join-Path $taskInstall 'com.swiftfetch.bridge.json'
    $taskManifest = Get-Content -LiteralPath $taskManifestPath -Raw | ConvertFrom-Json
    Require ($taskManifest.path -eq (Join-Path $taskInstall 'Flux.exe')) 'Native manifest has the wrong installed path.'
    $taskInstalledBytes=(Get-ChildItem -LiteralPath $taskInstall -Recurse -File|Measure-Object Length -Sum).Sum
    Require ($taskInstalledBytes -le 110000000) ('Installed application exceeds 100 MB: '+$taskInstalledBytes)
    $taskPassed.Add(('Complete installed footprint, including video tools and uninstaller: {0:N2} MB' -f ($taskInstalledBytes/1000000)))
    foreach ($taskEntry in $taskRegistry) {
        $taskBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$taskEntry.view)
        $taskKey=$taskBase.OpenSubKey($taskEntry.key)
        try { Require ($taskKey -and $taskKey.GetValue('') -eq $taskManifestPath) 'Native browser registration missing.' }
        finally { if($taskKey){$taskKey.Dispose()};$taskBase.Dispose() }
    }
    $taskRunKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try { Require ($taskRunKey.GetValue('ApexDownloader') -eq ('"' + (Join-Path $taskInstall 'Flux.exe') + '" --background')) 'Startup command is incorrect.' } finally { $taskRunKey.Dispose() }
    $taskShortcut=Join-Path ([Environment]::GetFolderPath('Programs')) ($taskGroup + '/Flux.lnk')
    Require (Test-Path -LiteralPath $taskShortcut) 'Start menu shortcut missing.'
    $taskLink=(New-Object -ComObject WScript.Shell).CreateShortcut($taskShortcut)
    Require ($taskLink.TargetPath -eq (Join-Path $taskInstall 'Flux.exe')) 'Shortcut points outside installed app.'
    $taskGuideShortcut=Join-Path ([Environment]::GetFolderPath('Programs')) ($taskGroup + "/Configurer l'extension Chrome et Brave.lnk")
    Require (Test-Path -LiteralPath $taskGuideShortcut) 'Extension setup guide shortcut missing.'
    $taskGuideLink=(New-Object -ComObject WScript.Shell).CreateShortcut($taskGuideShortcut)
    Require ($taskGuideLink.TargetPath -eq (Join-Path $taskInstall 'Browser-Setup.html')) 'Guide shortcut points outside installed app.'
    $taskPassed.Add('Silent installation, Unicode/spaces path, shortcuts, browser registration and optional tray startup')
    foreach($taskFile in (Get-Content -LiteralPath (Join-Path $taskInstall 'payload-sha256.json') -Raw | ConvertFrom-Json)) {
        Require ((Get-FileHash -LiteralPath (Join-Path $taskInstall $taskFile.path)).Hash -eq $taskFile.sha256) ('Installed payload checksum mismatch: ' + $taskFile.path)
    }
    Run-Diagnostic
    $taskPassed.Add('Payload SHA-256 inventory and real WPF / SQLite / DPAPI diagnostic with shared .NET lookup disabled')
    $taskStart=[Diagnostics.ProcessStartInfo]::new($taskManifest.path)
    $taskStart.UseShellExecute=$false; $taskStart.CreateNoWindow=$true; $taskStart.RedirectStandardInput=$true; $taskStart.RedirectStandardOutput=$true
    $taskStart.ArgumentList.Add($taskManifest.allowed_origins[0])
    $taskStart.Environment['DOTNET_ROOT']=Join-Path $taskTestRoot 'no-system-runtime'
    $taskStart.Environment['DOTNET_ROOT_X64']=Join-Path $taskTestRoot 'no-system-runtime'
    $taskStart.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
    $taskStart.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $taskTestRoot 'unexpected-runtime-extraction'
    $taskHost=[Diagnostics.Process]::Start($taskStart)
    try {
        $taskBytes=[Text.Encoding]::UTF8.GetBytes('{"action":"ping"}')
        $taskHeaderOut=[BitConverter]::GetBytes([int]$taskBytes.Length)
        $taskHost.StandardInput.BaseStream.Write($taskHeaderOut,0,4); $taskHost.StandardInput.BaseStream.Write($taskBytes,0,$taskBytes.Length); $taskHost.StandardInput.Close()
        Require ($taskHost.WaitForExit(20000)) 'Installed native host timed out.'
        $taskHeader=[byte[]]::new(4); $taskHost.StandardOutput.BaseStream.ReadExactly($taskHeader,0,4)
        $taskLength=[BitConverter]::ToInt32($taskHeader)
        Require ($taskLength -gt 0 -and $taskLength -lt 65536) 'Invalid native response size.'
        $taskReply=[byte[]]::new($taskLength); $taskHost.StandardOutput.BaseStream.ReadExactly($taskReply,0,$taskLength)
        Require (([Text.Encoding]::UTF8.GetString($taskReply)|ConvertFrom-Json).ok) 'Installed browser bridge rejected ping.'
    } finally { $taskHost.Dispose() }
    $taskPassed.Add('Real installed native messaging host roundtrip')
    Require (!(Test-Path -LiteralPath (Join-Path $taskTestRoot 'unexpected-runtime-extraction'))) 'The compact bundle created a hidden runtime extraction copy.'
    foreach($taskCommand in @(@('qjs.exe','--version'),@('yt-dlp.exe','--version'),@('ffmpeg.exe','-version'),@('ffprobe.exe','-version'))) {
        $taskStart=[Diagnostics.ProcessStartInfo]::new((Join-Path $taskInstall ('video-tools/' + $taskCommand[0])))
        $taskStart.ArgumentList.Add($taskCommand[1]);$taskStart.UseShellExecute=$false;$taskStart.CreateNoWindow=$true;$taskStart.RedirectStandardOutput=$true;$taskStart.RedirectStandardError=$true
        $taskTool=[Diagnostics.Process]::Start($taskStart);$taskOutput=$taskTool.StandardOutput.ReadToEndAsync();$taskError=$taskTool.StandardError.ReadToEndAsync()
        Require ($taskTool.WaitForExit(30000) -and $taskTool.ExitCode -eq 0) ('Bundled tool failed: ' + $taskCommand[0]);$null=$taskOutput.GetAwaiter().GetResult();$null=$taskError.GetAwaiter().GetResult();$taskTool.Dispose()
    }
    $taskPassed.Add('All four installed video tool executables launch')
    $taskMutex=[Threading.Mutex]::new($false,'Local\ApexDownloader.Running')
    try {
        foreach($taskGuard in @($InstallerPath,$taskUninstaller)) {
            $taskBlocked=Start-Process -FilePath $taskGuard -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART') -WindowStyle Hidden -PassThru
            Require ($taskBlocked.WaitForExit(30000) -and $taskBlocked.ExitCode -ne 0) 'Setup/uninstall did not refuse a running Flux session.'
            $taskBlocked.Dispose()
        }
    } finally {$taskMutex.Dispose()}
    # Inno's launcher can exit just before its child closes the probe handle.
    # Wait for our test-only mutex to disappear before testing reinstallation.
    $taskGuardDeadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        $taskProbe=$null
        $taskGuardPresent=[Threading.Mutex]::TryOpenExisting('Local\ApexDownloader.Running',[ref]$taskProbe)
        if($taskProbe){$taskProbe.Dispose()}
        if(!$taskGuardPresent){break}
        Start-Sleep -Milliseconds 200
    } while([DateTime]::UtcNow -lt $taskGuardDeadline)
    Require (!$taskGuardPresent) 'An Flux process still holds the installation guard after the test.'
    Require (Test-Path -LiteralPath (Join-Path $taskInstall 'Flux.exe')) 'Blocked uninstall removed application files.'
    $taskPassed.Add('Install and uninstall refuse an active app/tray session without force-closing it')
    $taskKeep=Join-Path $taskInstall 'user-created-keep.txt'; [IO.File]::WriteAllText($taskKeep,'preserve me')
    $taskDownload=Join-Path $taskTestRoot 'downloaded-file.txt';[IO.File]::WriteAllText($taskDownload,'preserve downloaded file')
    Run-Setup 'upgrade.log';Run-Diagnostic
    Require ((Get-Content -LiteralPath $taskKeep -Raw) -eq 'preserve me') 'Upgrade deleted an unknown file.'
    $taskPassed.Add('Reinstallation / upgrade preserves untracked files')
    $taskForeignManifest=Join-Path $taskTestRoot 'another-installation/com.swiftfetch.bridge.json'
    $taskForeignBase=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
    $taskForeignKey=$taskForeignBase.CreateSubKey('Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.swiftfetch.bridge')
    try{$taskForeignKey.SetValue('',$taskForeignManifest)}finally{$taskForeignKey.Dispose();$taskForeignBase.Dispose()}
    $taskUninstall=Start-Process -FilePath $taskUninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $taskTestRoot 'uninstall.log') + '"')) -WindowStyle Hidden -PassThru
    Require ($taskUninstall.WaitForExit(120000) -and $taskUninstall.ExitCode -eq 0) 'Uninstallation failed.'
    $taskInstalled=$false
    Require (!(Test-Path -LiteralPath (Join-Path $taskInstall 'Flux.exe'))) 'Uninstaller left the application executable behind.'
    Require ((Test-Path -LiteralPath $taskKeep) -and (Test-Path -LiteralPath $taskDownload)) 'Uninstaller removed a user file.'
    Require (!(Test-Path -LiteralPath $taskShortcut)) 'Uninstaller left the shortcut behind.'
    foreach($taskEntry in $taskRegistry) {
        $taskBase=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$taskEntry.view);$taskKey=$taskBase.OpenSubKey($taskEntry.key)
        try { Require (!$taskKey -or $taskKey.GetValue('') -ne $taskManifestPath) 'Uninstaller left its browser bridge registered.' } finally {if($taskKey){$taskKey.Dispose()};$taskBase.Dispose()}
    }
    $taskRunKey=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {Require (!$taskRunKey -or !$taskRunKey.GetValue('ApexDownloader')) 'Uninstaller left its startup command behind.'} finally {if($taskRunKey){$taskRunKey.Dispose()}}
    $taskForeignBase=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
    $taskForeignKey=$taskForeignBase.OpenSubKey('Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.swiftfetch.bridge')
    try {Require ($taskForeignKey -and $taskForeignKey.GetValue('') -eq $taskForeignManifest) 'Uninstaller removed another installation registration.'}finally{if($taskForeignKey){$taskForeignKey.Dispose()};$taskForeignBase.Dispose()}
    # This sentinel was written by the test, not by a concurrent installation.
    foreach($taskEntry in $taskRegistry) {
        $taskBase=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$taskEntry.view);$taskKey=$taskBase.OpenSubKey($taskEntry.key,$true)
        try{if($taskKey -and $taskKey.GetValue('') -eq $taskForeignManifest){$taskKey.DeleteValue('',$false)}}finally{if($taskKey){$taskKey.Dispose()};$taskBase.Dispose()}
    }
    Require ((Get-StateHashes) -eq $taskBefore) 'Installation test changed the real user history.'
    $taskPassed.Add('Uninstallation removes owned integration/startup/shortcuts, preserves other registrations, user files and real history')
    $taskPassed | ForEach-Object { Write-Output "PASS $_" }
    [pscustomobject]@{installer=[IO.Path]::GetFileName($InstallerPath);sha256=(Get-FileHash -LiteralPath $InstallerPath).Hash;installedBytes=$taskInstalledBytes;checks=$taskPassed;logDirectory=$taskTestRoot;timestamp=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/measurements/installer-validation.json') -Encoding utf8
} finally {
    if($taskInstalled){Write-Warning "Test installation remains at $taskInstall. Its uninstaller can be run normally."}
    # Restore only values changed by this test; do not overwrite a concurrent new registration.
    foreach($taskEntry in $taskRegistry) {
        $taskBase=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$taskEntry.view)
        $taskKey=$taskBase.OpenSubKey($taskEntry.key)
        try {$taskCurrent=if($taskKey){$taskKey.GetValue('')}else{$null}} finally {if($taskKey){$taskKey.Dispose()}}
        if(!$taskCurrent -or $taskCurrent -eq (Join-Path $taskInstall 'com.swiftfetch.bridge.json') -or ($taskForeignManifest -and $taskCurrent -eq $taskForeignManifest)) {
            if($taskEntry.existed){$taskKey=$taskBase.CreateSubKey($taskEntry.key);try{if($null -ne $taskEntry.value){$taskKey.SetValue('',$taskEntry.value)}else{$taskKey.DeleteValue('',$false)}}finally{$taskKey.Dispose()}}
            else{$taskBase.DeleteSubKey($taskEntry.key,$false)}
        }
        $taskBase.Dispose()
    }
    $taskRunKey=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        $taskCurrent=$taskRunKey.GetValue('ApexDownloader')
        if(!$taskCurrent -or $taskCurrent -eq ('"' + (Join-Path $taskInstall 'Flux.exe') + '" --background')) {
            if($null -ne $taskOldRun){$taskRunKey.SetValue('ApexDownloader',$taskOldRun)}else{$taskRunKey.DeleteValue('ApexDownloader',$false)}
        }
    } finally {$taskRunKey.Dispose()}
}
