# Validate admin installs the FHG-only WFP rules, non-admin can verify them.
param([Parameter(Mandatory=$true)][string]$GuardPath,
      [Parameter(Mandatory=$true)][string]$ProbePath)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$root=Join-Path $env:ProgramData ('FHG-guard-nonadmin-'+[guid]::NewGuid().ToString('N').Substring(0,9))
$who='fhg-ci-'+[guid]::NewGuid().ToString('N').Substring(0,8)
$userCreated=$false
$guardInstalled=$false
try {
  New-Item -ItemType Directory -Path $root -Force | Out-Null
  Copy-Item $ProbePath (Join-Path $root 'firefox.exe') -Force
  $pw=ConvertTo-SecureString ('FHGz7!'+[guid]::NewGuid().ToString('N')) -AsPlainText -Force
  New-LocalUser -Name $who -Password $pw -AccountNeverExpires -PasswordNeverExpires -Description 'FHG ephemeral WFP ACL CI test' | Out-Null
  $userCreated=$true
  & $GuardPath install $root
  if($LASTEXITCODE -ne 0){throw 'Admin WFP installation or post-install verification failed'}
  $guardInstalled=$true
  & $GuardPath status $root
  if($LASTEXITCODE -ne 0){throw 'Admin WFP status failed'}
  $credential=[pscredential]::new("$env:COMPUTERNAME\$who",$pw)
  $arg='status "'+$root+'"'
  $proc=Start-Process -FilePath $GuardPath -Credential $credential -WorkingDirectory $root -ArgumentList $arg -PassThru -Wait
  if($proc.ExitCode -ne 0){throw "Non-admin WFP verification failed (exit $($proc.ExitCode)); users would see repeat UAC prompts"}
  Write-Host 'PASS: standard Windows user can verify FHG WFP rules without admin access.'
  $reject=Start-Process -FilePath $GuardPath -Credential $credential -WorkingDirectory $root -ArgumentList 'remove' -PassThru -Wait
  if($reject.ExitCode -eq 0){throw 'Security regression: standard user was allowed to remove FHG WFP rules'}
  & $GuardPath status $root
  if($LASTEXITCODE -ne 0){throw 'Non-admin removal attempt damaged admin-installed rules'}
  Write-Host 'PASS: standard Windows user cannot remove rules.'
}
finally {
  if($guardInstalled){
    & $GuardPath remove
    if($LASTEXITCODE -ne 0){Write-Warning 'Admin guard cleanup failed'}
  }
  if($userCreated){Remove-LocalUser -Name $who -ErrorAction SilentlyContinue}
  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
