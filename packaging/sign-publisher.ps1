param(
 [Parameter(Mandatory=$true)][string]$Controller,
 [Parameter(Mandatory=$true)][string]$Guard
)
$ErrorActionPreference='Stop'
$exe=@((Resolve-Path $Controller).Path,(Resolve-Path $Guard).Path)
# A real publisher signature requires a private code-signing certificate.
# GitHub does not contain the private key. Optional repository Actions secrets are:
#   FHG_SIGNING_PFX_BASE64, FHG_SIGNING_PFX_PASSWORD
# If absent, leave EXEs unsigned and clearly say so in the release metadata.
$b64=$env:FHG_SIGNING_PFX_BASE64
$pwd=$env:FHG_SIGNING_PFX_PASSWORD
if ([string]::IsNullOrWhiteSpace($b64) -xor [string]::IsNullOrEmpty($pwd)) {
 throw 'Code signing is only partially configured: set both certificate and password'
}
if ([string]::IsNullOrWhiteSpace($b64) -or [string]::IsNullOrEmpty($pwd)) {
 Write-Host 'No publisher signing certificate in Actions secrets: EXEs remain UNSIGNED.'
 return
}
if ($env:GITHUB_EVENT_NAME -ne 'push' -or $env:GITHUB_REF -ne 'refs/heads/main') {
 throw 'Refusing to use publisher certificate outside a push to main'
}
$signtool=Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Filter signtool.exe -Recurse -ErrorAction Stop |
 Where-Object {$_.FullName -match '\\x64\\signtool\.exe$'} |
 Sort-Object FullName -Descending | Select-Object -First 1
if (!$signtool) {throw 'Windows SignTool is missing'}
$pfx=Join-Path $env:RUNNER_TEMP 'fhg-signing.pfx'
try {
 [IO.File]::WriteAllBytes($pfx,[Convert]::FromBase64String($b64))
 foreach ($path in $exe) {
  & $signtool.FullName sign /f $pfx /p $pwd /fd SHA256 /td SHA256 /tr 'http://timestamp.digicert.com' $path
  if ($LASTEXITCODE -ne 0) {throw "Authenticode signing failed for $(Split-Path $path -Leaf)"}
  & $signtool.FullName verify /pa $path
  if ($LASTEXITCODE -ne 0) {throw "Authenticode verification failed for $(Split-Path $path -Leaf)"}
  $status=(Get-AuthenticodeSignature $path).Status
  if($status -ne 'Valid') {throw "Signed file is not trust-valid: $path ($status)"}
 }
 Write-Host 'PASS: both FHG binaries have valid verified Authenticode signatures.'
} finally {
 Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
