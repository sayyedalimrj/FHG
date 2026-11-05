# Build a self-contained FHG bundle with the UNMODIFIED official Hiddify GUI.
# This deliberately retires FHG's custom WPF subscription/profile UI.
param(
    [Parameter(Mandatory=$true)][string]$Launcher,
    [Parameter(Mandatory=$true)][string]$Guard,
    [string]$Output = "FHG-original-Hiddify-UI-windows-x64.zip"
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$root=Join-Path (Get-Location) 'stage-original-ui'
$downloads=Join-Path (Get-Location) 'download-original-ui'
$firefox=Join-Path $root 'Firefox'
$hiddify=Join-Path $root 'Hiddify'
$sevenZip='C:\Program Files\7-Zip\7z.exe'
if(!(Test-Path $sevenZip)){throw '7-Zip unavailable on Windows runner'}
New-Item -ItemType Directory -Force -Path $root,$downloads,$firefox,$hiddify,(Join-Path $root 'Data') | Out-Null
Copy-Item -LiteralPath $Launcher -Destination (Join-Path $root 'FHG.exe') -Force
Copy-Item -LiteralPath $Guard -Destination (Join-Path $root 'FHGGuard.exe') -Force

# 1. Upstream Firefox official Windows 64-bit installer (not separately installed).
$firefoxInstaller=Join-Path $downloads 'FirefoxInstaller.exe'
Invoke-WebRequest -Uri 'https://download.mozilla.org/?product=firefox-latest&os=win64&lang=en-US' -OutFile $firefoxInstaller -MaximumRedirection 10
if((Get-Item $firefoxInstaller).Length -lt 20000000){throw 'Unexpectedly small Firefox installer'}
if((Get-AuthenticodeSignature -LiteralPath $firefoxInstaller).Status -ne 'Valid'){
    throw 'Firefox installer Authenticode verification failed'
}
$ffExtract=Join-Path $downloads 'firefox-extracted'
New-Item -ItemType Directory -Force -Path $ffExtract | Out-Null
& $sevenZip x $firefoxInstaller "-o$ffExtract" -y | Out-Null
if($LASTEXITCODE -ne 0){throw 'Firefox extraction failed'}
$ffExe=Get-ChildItem -LiteralPath $ffExtract -Recurse -File -Filter firefox.exe |
    Where-Object {$_.Directory.Name -eq 'core'} | Select-Object -First 1
if(!$ffExe){throw 'Official Firefox firefox.exe not found in extracted installer'}
Get-ChildItem -LiteralPath $ffExe.Directory.FullName -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $firefox -Recurse -Force
}
if((Get-AuthenticodeSignature -LiteralPath (Join-Path $firefox 'firefox.exe')).Status -ne 'Valid'){
    throw 'Extracted Firefox executable Authenticode verification failed'
}
New-Item -ItemType Directory -Force -Path (Join-Path $firefox 'distribution') | Out-Null
Copy-Item -LiteralPath 'firefox/distribution/policies.json' -Destination (Join-Path $firefox 'distribution/policies.json') -Force

# 2. Full Hiddify Windows Portable, not HiddifyCLI alone.
$hiddifyVersion='v5.5.5'
$hiddifyUrl='https://github.com/hiddify/hiddify-app/releases/download/v5.5.5/Hiddify-Windows-Portable-x64.zip'
$hiddifyExpectedHash='b4579d6a9a7b0bb0dedfd2e462ab4cfefc53ec25b56f287cca5edb435f01f627'
$hiddifyZip=Join-Path $downloads 'Hiddify-Windows-Portable-x64.zip'
Invoke-WebRequest -Uri $hiddifyUrl -OutFile $hiddifyZip -MaximumRedirection 10
$hiddifyActualHash=(Get-FileHash -LiteralPath $hiddifyZip -Algorithm SHA256).Hash.ToLowerInvariant()
if($hiddifyActualHash -ne $hiddifyExpectedHash){
    throw "Hiddify official release SHA-256 mismatch: $hiddifyActualHash"
}
Write-Host "PASS: official Hiddify GUI $hiddifyVersion download verified."
$hidExtract=Join-Path $downloads 'hiddify-extracted'
New-Item -ItemType Directory -Force -Path $hidExtract | Out-Null
& $sevenZip x $hiddifyZip "-o$hidExtract" -y | Out-Null
if($LASTEXITCODE -ne 0){throw 'Cannot extract official Hiddify portable ZIP'}
$hidExe=Get-ChildItem -LiteralPath $hidExtract -Recurse -File -Filter Hiddify.exe | Select-Object -First 1
if(!$hidExe){
    $tarFiles=Get-ChildItem -LiteralPath $hidExtract -Recurse -File -Filter *.tar
    foreach($tar in $tarFiles){
        & $sevenZip x $tar.FullName "-o$hidExtract" -y | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Cannot extract nested Hiddify TAR'}
    }
    $hidExe=Get-ChildItem -LiteralPath $hidExtract -Recurse -File -Filter Hiddify.exe | Select-Object -First 1
}
if(!$hidExe){throw 'Hiddify.exe not found in upstream portable release'}
$hidAppDir=$hidExe.Directory.FullName
Get-ChildItem -LiteralPath $hidAppDir -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $hiddify -Recurse -Force
}
if(!(Test-Path (Join-Path $hiddify 'Hiddify.exe'))){throw 'Original Hiddify UI missing'}
if(!(Test-Path (Join-Path $hiddify 'flutter_windows.dll'))){throw 'Flutter GUI runtime missing; refusing incomplete bundle'}
if(!(Test-Path (Join-Path $hiddify 'data'))){throw 'Hiddify Flutter data directory missing'}

# Do not change or rebuild any Hiddify GUI resources; the vendor itself manages
# subscriptions, real quota/expiry, ping tests, server selection and updates.
# The "hiddify_portable_data" directory is used by the upstream portable build.
if(!(Test-Path (Join-Path $hiddify 'hiddify_portable_data'))){
    New-Item -ItemType Directory -Path (Join-Path $hiddify 'hiddify_portable_data') | Out-Null
}

@'
FHG Browser — Original Hiddify Interface — Windows 10/11 x64 (experimental)
==========================================================================

Run FHG.exe. It opens the FULL, UNMODIFIED Hiddify interface and your isolated
Firefox browser. Subscriptions, server lists, traffic balance, expiry, latency
tests and connection options are handled ONLY by Hiddify itself.

IMPORTANT:
- In Hiddify Settings, use LOCAL PROXY mode (not TUN or system proxy) and set
  the local mixed SOCKS5 proxy port to 12334 on loopback/127.0.0.1.
- Connect a subscription using Hiddify's normal interface.
- Firefox is configured to talk only to SOCKS5 127.0.0.1:12334.
- If Hiddify disconnects, Firefox stays open but requests should fail.
- Windows asks for one-time administrator authorization only if the
  FHG-specific app-scoped WFP guard is not already installed.
- Your ordinary installed Firefox remains separate from FHG.
- Hiddify proxy routing may contain DIRECT rules; this release has NOT
  verified/disabled all such paths. Check Hiddify's route mode and rules.
- FHG EXEs are not trusted publisher-signed without a real certificate.
- NO complete real-world zero-leak security validation has been done.
  NOT FOR HIGH-RISK OR ANONYMITY-CRITICAL BROWSING.
- Hiddify original GUI: https://github.com/hiddify/hiddify-app (license:
  https://github.com/hiddify/hiddify-app/blob/main/LICENSE.md)
- Mozilla Firefox: https://www.mozilla.org/firefox/
'@ | Set-Content -LiteralPath (Join-Path $root 'START-HERE.txt') -Encoding utf8

$report=[ordered]@{
    product='FHG Browser'
    version='0.4.0-alpha.1'
    stage='experimental-not-zero-leak-certified'
    hiddify_release=$hiddifyVersion
    hiddify_upstream_sha256=$hiddifyActualHash
    github_commit=$env:GITHUB_SHA
    built_utc=[DateTime]::UtcNow.ToString('o')
    binaries=@()
}
foreach($relative in @('FHG.exe','FHGGuard.exe','Firefox/firefox.exe','Hiddify/Hiddify.exe','Hiddify/flutter_windows.dll')){
    $file=Join-Path $root $relative
    if(!(Test-Path $file)){throw "Required file absent: $relative"}
    $item=Get-Item $file
    $signature=Get-AuthenticodeSignature -LiteralPath $file
    $report.binaries += [ordered]@{
        path=$relative
        bytes=[long]$item.Length
        sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        authenticode_status=[string]$signature.Status
        signer=if($signature.SignerCertificate){$signature.SignerCertificate.Subject}else{''}
    }
}
($report | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath (Join-Path $root 'BUILD-SECURITY.json') -Encoding utf8

$target=Join-Path (Get-Location) $Output
if(Test-Path $target){Remove-Item -LiteralPath $target -Force}
Push-Location $root
try{
    & $sevenZip a -tzip -mx=5 $target '*' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot create ZIP'}
}finally{Pop-Location}
& $sevenZip t $target | Out-Null
if($LASTEXITCODE -ne 0){throw 'ZIP CRC integrity test failed'}
Write-Host "PASS: original upstream Hiddify GUI, Firefox and native guard fully bundled."
Write-Host "Created: $target"
