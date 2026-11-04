# Writes a signed version.json for an installer. Upload both files to the release, then set the
# app's update feed (Options -> View) to the version.json URL, e.g.
#   https://github.com/<owner>/<repo>/releases/latest/download/version.json
param(
  [Parameter(Mandatory)][string]$Installer,
  [Parameter(Mandatory)][string]$DownloadUrl,
  [Parameter(Mandatory)][string]$Version,
  [string]$Output = (Join-Path (Split-Path -Parent $Installer) 'version.json'),
  [string]$KeyPath = (Join-Path $env:LOCALAPPDATA 'ApexRelease\update-signing.key')
)
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
dotnet run --project (Join-Path $root 'tools/UpdateSigner/UpdateSigner.csproj') -- sign $KeyPath (Resolve-Path $Installer) $DownloadUrl $Version $Output
exit $LASTEXITCODE
