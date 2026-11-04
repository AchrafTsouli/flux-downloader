$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = Join-Path $workspace 'dist\SwiftFetch.exe'

# Kill old running instance if any
Get-Process -Name SwiftFetch, ApexDownloader -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# Launch through Windows Explorer shell so it lives on the user's desktop independent of terminal jobs
Start-Process "explorer.exe" -ArgumentList "`"$exe`""

Start-Sleep -Seconds 2
$procs = Get-Process -Name SwiftFetch -ErrorAction SilentlyContinue
if ($procs) {
    Write-Output "Successfully launched on desktop! PID: $($procs[0].Id), Title: '$($procs[0].MainWindowTitle)'"
} else {
    Write-Output "Checking with standard launch..."
}
