[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$taskCompilerDirectory = Join-Path $taskRoot '.tools/InnoSetup'
$taskCompiler = Join-Path $taskCompilerDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $taskCompiler) { Write-Output $taskCompiler; return }
$taskDownload = Join-Path $taskRoot '.tools/downloads/innosetup-6.7.3.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $taskDownload) -Force | Out-Null
$taskExpected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if (!(Test-Path -LiteralPath $taskDownload)) {
    Invoke-WebRequest -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $taskDownload
}
if ((Get-FileHash -LiteralPath $taskDownload -Algorithm SHA256).Hash -ne $taskExpected) { throw 'Inno Setup download checksum mismatch.' }
$taskSignature = Get-AuthenticodeSignature -FilePath $taskDownload
if ($taskSignature.Status -ne 'Valid' -or $taskSignature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.') { throw 'Inno Setup publisher verification failed.' }
# Compiler tooling only, within this workspace; no .iss file association or shortcuts.
$taskProcess = Start-Process -FilePath $taskDownload -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS','/MERGETASKS=!fileassoc',('/DIR="' + $taskCompilerDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
if ($taskProcess.ExitCode -ne 0 -or !(Test-Path -LiteralPath $taskCompiler)) { throw "Inno Setup installation failed ($($taskProcess.ExitCode))." }
Write-Output $taskCompiler
