#requires -Version 7.4
# One-step release: build the installer, sign version.json, and publish both to a GitHub release.
# Installed copies of Flux pick the release up through .../releases/latest/download/version.json.
# Requires: GitHub CLI signed in (gh auth login), the signing key from New-UpdateKey.ps1, and a
# committed, pushed working tree.
param([string]$Notes = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
  $version = ([xml](Get-Content -LiteralPath 'SwiftFetch/SwiftFetch.csproj' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
  $tag = "v$version"
  if (git status --porcelain) { throw 'Commit or stash your changes before publishing a release.' }
  $repo = gh repo view --json nameWithOwner -q .nameWithOwner
  if (!$repo) { throw 'Run gh auth login and make sure the origin remote points at GitHub.' }
  if (gh release view $tag 2>$null) { throw "Release $tag already exists. Bump <Version> in SwiftFetch/SwiftFetch.csproj first." }

  & (Join-Path $PSScriptRoot 'Build-LocalInstaller.ps1')
  if ($LASTEXITCODE -ne 0) { throw 'The installer build failed.' }
  $installer = Join-Path $root "installers/Flux-Setup-$version-win-x64.exe"
  $url = "https://github.com/$repo/releases/download/$tag/Flux-Setup-$version-win-x64.exe"
  $feed = Join-Path $root 'installers/version.json'
  & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -Installer $installer -DownloadUrl $url -Version $version -Output $feed
  if ($LASTEXITCODE -ne 0) { throw 'Signing failed.' }

  git tag $tag; git push origin $tag
  $notesArgs = if ($Notes) { @('--notes', $Notes) } else { @('--generate-notes') }
  gh release create $tag $installer "$installer.sha256" $feed --title "Flux $version" @notesArgs
  Write-Output "Published ${tag}: https://github.com/$repo/releases/tag/$tag"
}
finally { Pop-Location }
