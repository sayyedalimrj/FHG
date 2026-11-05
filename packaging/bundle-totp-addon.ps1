# Bundle a signed Mozilla Add-ons TOTP extension into the portable FHG Firefox.
# Never embed or log TOTP secrets. Firefox verifies the XPI signature at install.
param([Parameter(Mandatory=$true)][string]$FirefoxDirectory)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$root=(Resolve-Path -LiteralPath $FirefoxDirectory).Path
if((Split-Path -Path $root -Leaf) -ne 'Firefox' -or
   !(Test-Path (Join-Path $root 'firefox.exe')) -or
   !(Test-Path (Join-Path $root 'distribution/policies.json'))) {
    throw 'TOTP bundling is restricted to the extracted FHG Firefox distribution.'
}
$distribution=Join-Path $root 'distribution'
$dest=Join-Path $distribution 'fhg-totp.xpi'
$url='https://addons.mozilla.org/firefox/downloads/latest/totp/latest.xpi'
$sevenZip='C:\Program Files\7-Zip\7z.exe'
if(!(Test-Path $sevenZip)){throw '7-Zip required for XPI inspection'}
$success=$false
for($attempt=1; $attempt -le 5; $attempt++){
    try {
        Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
        & curl.exe -fSL --retry 2 --retry-all-errors --connect-timeout 20 --max-time 120 -o $dest $url
        if($LASTEXITCODE -ne 0){throw "AMO download failed ($LASTEXITCODE)"}
        $length=(Get-Item $dest).Length
        if($length -lt 4000 -or $length -gt 2000000){throw "Unexpected TOTP XPI length: $length"}
        & $sevenZip t $dest | Out-Null
        if($LASTEXITCODE -ne 0){throw 'Not a valid XPI ZIP'}
        Add-Type -AssemblyName System.IO.Compression
        $zip=[System.IO.Compression.ZipFile]::OpenRead($dest)
        try {
            $entry=$zip.GetEntry('manifest.json')
            if($null -eq $entry){throw 'Missing XPI manifest'}
            $reader=[IO.StreamReader]::new($entry.Open())
            try {$manifest=$reader.ReadToEnd() | ConvertFrom-Json}
            finally {$reader.Dispose()}
            $name=[string]$manifest.name
            if($name -notmatch 'TOTP'){throw "Unexpected add-on manifest: $name"}
            if($manifest.content_scripts -or ($manifest.permissions -contains '<all_urls>') -or
                ($manifest.permissions -contains 'webRequest') -or
                ($manifest.host_permissions -and $manifest.host_permissions.Count -gt 0)){
                throw 'Unexpected broad website permissions; add-on requires review'
            }
            $names=@($zip.Entries | ForEach-Object {$_.FullName})
            if(-not ($names | Where-Object {$_ -match '^META-INF/.*\.(rsa|sf)$'})){
                throw 'XPI does not appear Mozilla-signed; refusing bundling'
            }
        } finally {$zip.Dispose()}
        $hash=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText(($dest+'.sha256'),($hash+'  fhg-totp.xpi'+[Environment]::NewLine),[Text.Encoding]::ASCII)
        Write-Host "PASS: bundled Mozilla TOTP add-on ($length bytes), SHA256 $hash"
        $success=$true
        break
    } catch {
        Write-Warning "TOTP add-on bundling attempt $attempt/5: $($_.Exception.Message)"
        Remove-Item -LiteralPath $dest -Force -ErrorAction SilentlyContinue
        if($attempt -lt 5){Start-Sleep -Seconds ([Math]::Min(18,3*$attempt))}
    }
}
if(-not $success){
    Write-Warning 'Offline TOTP was not bundled. Official Mozilla AMO install policy remains active after the proxy connects.'
}
