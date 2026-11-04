[CmdletBinding(SupportsShouldProcess=$true)]
param()
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path.TrimEnd([IO.Path]::DirectorySeparatorChar)
$taskPrefix=$taskRoot+[IO.Path]::DirectorySeparatorChar
$taskTargets=[Collections.Generic.List[string]]::new()
foreach($taskRelative in @('.test-tmp','video-tools/packages','scripts/__pycache__','.dotnet-home/.dotnet/TelemetryStorageService')){$taskTargets.Add((Join-Path $taskRoot $taskRelative))}
foreach($taskProject in Get-ChildItem -LiteralPath $taskRoot -Directory -Filter 'SwiftFetch*'){
 if(Get-ChildItem -LiteralPath $taskProject.FullName -File -Filter '*.csproj'){
  foreach($taskGenerated in @('bin','obj')){$taskTargets.Add((Join-Path $taskProject.FullName $taskGenerated))}
 }
}
$taskTargets.Add((Join-Path $taskRoot 'ui-preview.png'))
$taskReport=[Collections.Generic.List[object]]::new()
foreach($taskCandidate in $taskTargets){
 $taskTarget=[IO.Path]::GetFullPath($taskCandidate)
 if(!$taskTarget.StartsWith($taskPrefix,[StringComparison]::OrdinalIgnoreCase)){throw "Cleanup target escapes workspace: $taskTarget"}
 if(!(Test-Path -LiteralPath $taskTarget)){continue}
 $taskEntry=Get-Item -LiteralPath $taskTarget -Force
 $taskAncestor=if($taskEntry.PSIsContainer){$taskEntry}else{$taskEntry.Directory}
 while($taskAncestor -and $taskAncestor.FullName.StartsWith($taskPrefix,[StringComparison]::OrdinalIgnoreCase)){
  if($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing cleanup through a reparse point: $($taskAncestor.FullName)"}
  $taskAncestor=$taskAncestor.Parent
 }
 if($taskEntry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Refusing linked cleanup target: $taskTarget"}
 $taskBytes=if($taskEntry.PSIsContainer){(Get-ChildItem -LiteralPath $taskTarget -Recurse -File -Force -ErrorAction SilentlyContinue|Measure-Object Length -Sum).Sum}else{$taskEntry.Length}
 if($PSCmdlet.ShouldProcess($taskTarget,'Remove generated project artifacts')){
  try{Remove-Item -LiteralPath $taskTarget -Recurse -Force;$taskReport.Add([pscustomobject]@{path=$taskTarget.Substring($taskPrefix.Length);removedBytes=[long]$taskBytes;status='removed'})}
  catch{$taskReport.Add([pscustomobject]@{path=$taskTarget.Substring($taskPrefix.Length);removedBytes=0;status=$_.Exception.Message})}
 }
}
$taskReport | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $taskRoot 'docs/measurements/cleanup-0.8.1.json') -Encoding utf8
$taskTotal=($taskReport|Where-Object status -eq 'removed'|Measure-Object removedBytes -Sum).Sum
Write-Output ('Generated artifacts removed: {0:N1} MiB' -f ($taskTotal/1MB))
$taskReport | Where-Object status -ne 'removed' | Format-Table -AutoSize
