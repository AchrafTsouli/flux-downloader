$ErrorActionPreference = 'Stop'
foreach ($browserKey in @('Software\Google\Chrome', 'Software\BraveSoftware\Brave-Browser')) {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey("$browserKey\NativeMessagingHosts\com.swiftfetch.bridge", $false)
}
Write-Output 'SwiftFetch browser bridge registration removed. Remove the SwiftFetch extension in your browser as well.'
