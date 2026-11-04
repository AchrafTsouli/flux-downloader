param([string]$InstallDirectory = '', [switch]$NoRegister)
$ErrorActionPreference = 'Stop'
if (!$InstallDirectory) {
    $InstallDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SwiftFetch.exe')) { $PSScriptRoot } else { Join-Path $PSScriptRoot '..\dist' }
}
$InstallDirectory = (Resolve-Path -LiteralPath $InstallDirectory).Path
$extensionPath = Join-Path $InstallDirectory 'BrowserExtension'
$manifest = Get-Content -LiteralPath (Join-Path $extensionPath 'manifest.json') -Raw | ConvertFrom-Json
$keyBytes = [Convert]::FromBase64String($manifest.key)
$hasher = [Security.Cryptography.SHA256]::Create()
try { $hash = $hasher.ComputeHash($keyBytes) } finally { $hasher.Dispose() }
$hex = [BitConverter]::ToString($hash).Replace('-','').Substring(0,32).ToLowerInvariant()
$extensionId = -join ($hex.ToCharArray() | ForEach-Object { [char](97 + [Convert]::ToInt32([string]$_,16)) })
$appExe = if (Test-Path -LiteralPath (Join-Path $InstallDirectory 'Flux.exe')) { Join-Path $InstallDirectory 'Flux.exe' } else { Join-Path $InstallDirectory 'SwiftFetch.exe' }
$hostExe = if (Test-Path -LiteralPath (Join-Path $InstallDirectory 'SwiftFetch.NativeHost.exe')) { Join-Path $InstallDirectory 'SwiftFetch.NativeHost.exe' } else { $appExe }
if (!(Test-Path -LiteralPath $hostExe) -or !(Test-Path -LiteralPath $appExe)) { throw 'Build Flux first.' }
$hostManifest = Join-Path $InstallDirectory 'com.swiftfetch.bridge.json'
$hostJson = @{name='com.swiftfetch.bridge'; description='SwiftFetch Chromium download bridge'; path=$hostExe; type='stdio'; allowed_origins=@("chrome-extension://$extensionId/")} | ConvertTo-Json
[IO.File]::WriteAllText($hostManifest, $hostJson, (New-Object System.Text.UTF8Encoding($false)))
if (!$NoRegister) {
    foreach ($browserKey in @('Software\Google\Chrome', 'Software\BraveSoftware\Brave-Browser')) {
        $registryKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$browserKey\NativeMessagingHosts\com.swiftfetch.bridge")
        try { $registryKey.SetValue('', $hostManifest, [Microsoft.Win32.RegistryValueKind]::String) } finally { $registryKey.Dispose() }
    }
    Write-Output 'Browser bridge registered for your Windows account (Chrome and Brave).'
}
Write-Output "Extension ID: $extensionId"
Write-Output "Load unpacked extension folder: $extensionPath"
