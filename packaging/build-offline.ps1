# Build a complete *experimental* offline bundle on Windows runners.
# Firefox executable comes from Mozilla's official current release redirect.
# Hiddify CLI comes from Hiddify's official portable release.
param(
    [Parameter(Mandatory=$true)][string]$Controller,
    [Parameter(Mandatory=$true)][string]$Guard,
    [string]$Output = "FHG-experimental-win-x64.zip"
)
$ErrorActionPreference = 'Stop'
$root = Join-Path (Get-Location) 'staging'
$download = Join-Path (Get-Location) 'download'
$firefox = Join-Path $root 'Firefox'
$core = Join-Path $root 'Core'
New-Item -ItemType Directory -Force -Path $root,$download,$firefox,$core,(Join-Path $root 'Data') | Out-Null
Copy-Item $Controller (Join-Path $root 'FHG.exe') -Force
Copy-Item $Guard (Join-Path $root 'FHGGuard.exe') -Force

Write-Host "Download official Firefox..."
$firefoxInstaller = Join-Path $download 'FirefoxSetup.exe'
Invoke-WebRequest -Uri 'https://download.mozilla.org/?product=firefox-latest&os=win64&lang=en-US' -OutFile $firefoxInstaller -MaximumRedirection 10
if ((Get-Item $firefoxInstaller).Length -lt 20000000) {throw 'Firefox installer download is unexpectedly small'}
$mozillaInstallerSign = Get-AuthenticodeSignature -LiteralPath $firefoxInstaller
if ($mozillaInstallerSign.Status -ne 'Valid') {
 throw "Official Firefox installer has an invalid Authenticode signature: $($mozillaInstallerSign.Status)"
}
$extract = Join-Path $download 'firefox-unpacked'
New-Item -ItemType Directory -Force $extract | Out-Null
$sevenZip='C:\Program Files\7-Zip\7z.exe'
if (!(Test-Path $sevenZip)) {throw '7zip missing on the Windows builder'}
& $sevenZip x $firefoxInstaller "-o$extract" -y | Out-Null
if ($LASTEXITCODE -ne 0) {throw 'Firefox unpacking failed'}
$ff = Get-ChildItem $extract -Recurse -Filter firefox.exe -File |
  Where-Object { $_.Directory.Name -eq 'core' } |
  Select-Object -First 1
if (!$ff) {throw 'No Firefox executable extracted from official installer'}
Copy-Item (Join-Path $ff.Directory.FullName '*') $firefox -Recurse -Force
if (!(Test-Path (Join-Path $firefox 'firefox.exe'))) {throw 'Firefox staging failed'}
$firefoxSign = Get-AuthenticodeSignature -LiteralPath (Join-Path $firefox 'firefox.exe')
if ($firefoxSign.Status -ne 'Valid') {
 throw "Firefox binary signature is not trusted: $($firefoxSign.Status)"
}
New-Item -ItemType Directory -Force -Path (Join-Path $firefox 'distribution') | Out-Null
Copy-Item 'firefox/distribution/policies.json' (Join-Path $firefox 'distribution/policies.json') -Force

Write-Host "Download Hiddify 4.1.1 portable release..."
$hid = Join-Path $download 'Hiddify-Windows-Portable-x64.zip'
Invoke-WebRequest -Uri 'https://github.com/hiddify/hiddify-app/releases/download/v4.1.1/Hiddify-Windows-Portable-x64.zip' -OutFile $hid -MaximumRedirection 10
if ((Get-Item $hid).Length -lt 10000000) {throw 'Hiddify download unexpectedly small'}
$hidOut=Join-Path $download 'hiddify-unpacked'
New-Item -ItemType Directory -Force $hidOut | Out-Null
# The upstream asset can be a TAR archive even though the extension says .zip.
& $sevenZip x $hid "-o$hidOut" -y | Out-Null
if ($LASTEXITCODE -ne 0) {throw 'Hiddify archive extraction failed'}
$cli=Get-ChildItem $hidOut -Recurse -Filter HiddifyCli.exe -File | Select-Object -First 1
if (!$cli) {
    $tarFiles=Get-ChildItem $hidOut -Recurse -File | Where-Object {$_.Extension -eq '.tar'}
    foreach ($tarFile in $tarFiles) {
      & $sevenZip x $tarFile.FullName "-o$hidOut" -y | Out-Null
      if ($LASTEXITCODE -ne 0) {throw 'Hiddify nested TAR extraction failed'}
    }
    $cli=Get-ChildItem $hidOut -Recurse -Filter HiddifyCli.exe -File | Select-Object -First 1
}
if (!$cli) {throw 'HiddifyCli.exe not found after extraction'}
Copy-Item $cli.FullName (Join-Path $core 'HiddifyCli.exe')
Get-ChildItem $cli.Directory.FullName -File -Filter '*.dll' | ForEach-Object {
    Copy-Item $_.FullName $core -Force
}
if (!(Test-Path (Join-Path $core 'hiddify-core.dll'))) {throw 'Missing Hiddify Core DLL'}

@'
FHG browser (EXPERIMENTAL, NOT SECURITY-CERTIFIED)
1. Extract the complete ZIP into a writable folder.
2. Run FHG.exe.
3. Choose a local Hiddify config file and press Connect Hiddify.
4. Press Open Browser; approve one-time security guard setup if requested.
The browser remains open if Hiddify disconnects; online requests must fail.
The network guard is experimental and MUST NOT yet be trusted for sensitive activity.
No system Firefox installation is modified. Do not move the app after enabling WFP
until its guard setup is updated for the new executable path.
'@ | Set-Content -Path (Join-Path $root 'START-HERE.txt') -Encoding utf8

# Reproducible checksum & signature report lives alongside the application.
$report = [ordered]@{
  project = 'FHG Browser'
  stage = 'experimental-alpha-not-security-certified'
  git_commit = $env:GITHUB_SHA
  generated_utc = [DateTime]::UtcNow.ToString('o')
  source = @{
    firefox = 'https://download.mozilla.org/?product=firefox-latest&os=win64&lang=en-US'
    hiddify = 'https://github.com/hiddify/hiddify-app/releases/download/v4.1.1/Hiddify-Windows-Portable-x64.zip'
  }
  files = @()
}
foreach($relative in @('FHG.exe','FHGGuard.exe','Firefox/firefox.exe','Core/HiddifyCli.exe','Core/hiddify-core.dll')) {
 $path=Join-Path $root $relative
 if(!(Test-Path $path)) { throw "Required binary missing: $relative" }
 $entry=Get-Item $path
 $sig=Get-AuthenticodeSignature -LiteralPath $entry.FullName
 $report.files += [ordered]@{
   path = $relative
   bytes = [long]$entry.Length
   sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
   authenticode_status = [string]$sig.Status
   authenticode_signer = if($sig.SignerCertificate) {$sig.SignerCertificate.Subject} else {''}
 }
}
($report | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath (Join-Path $root 'BUILD-SECURITY.json') -Encoding utf8
Write-Host 'Stage files:' (Get-ChildItem $root -Recurse -File | Measure-Object).Count
$target=Join-Path (Get-Location) $Output
if (Test-Path $target) {Remove-Item $target -Force}
Push-Location $root
try {
  & $sevenZip a -tzip -mx=5 $target '*' | Out-Null
  if ($LASTEXITCODE -ne 0) {throw 'Archive creation failed'}
} finally {Pop-Location}
& $sevenZip t $target | Out-Null
if ($LASTEXITCODE -ne 0) {throw 'Archive integrity check failed'}
Write-Host "Created:" $target
