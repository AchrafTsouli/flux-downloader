# Creates the release signing key once. The private key is DPAPI-protected for this Windows account
# and stored outside the project; the public key is embedded in SwiftFetch.Core/UpdateSigningKey.cs.
# Back up the private key file: without it, installed copies cannot accept later automatic updates.
param([string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'ApexRelease\update-signing.key'))
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
dotnet run --project (Join-Path $root 'tools/UpdateSigner/UpdateSigner.csproj') -- keygen $KeyPath (Join-Path $root 'SwiftFetch.Core/UpdateSigningKey.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Rebuild the release so the app contains the public key.'
