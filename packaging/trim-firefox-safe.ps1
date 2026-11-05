# FHG Firefox SAFE slimming: delete only installer/maintenance-only binaries.
# This intentionally leaves the Firefox browser engine, codecs, certificate
# handling, isolation processes, voice/media codecs and ML runtime intact.
param(
    [Parameter(Mandatory=$true)][string]$FirefoxDirectory,
    [switch]$AuditOnly
)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $FirefoxDirectory).Path
$ff=Join-Path $root 'firefox.exe'
$policy=Join-Path $root 'distribution/policies.json'
if ((Split-Path -Path $root -Leaf) -ne 'Firefox' -or
    !(Test-Path -LiteralPath $ff) -or
    !(Test-Path -LiteralPath $policy)) {
    throw 'Refusing to edit anything except the extracted FHG Firefox folder with bundled policies.'
}
if ($root.ToLowerInvariant().StartsWith([Environment]::GetFolderPath('ProgramFiles').ToLowerInvariant())) {
    throw 'Refusing to trim a normal installed Firefox in Program Files.'
}
$config=Get-Content -LiteralPath $policy -Raw | ConvertFrom-Json
if ($config.policies.DisableAppUpdate -ne $true) {
    throw 'FHG-managed app updates must be disabled before removing installer maintenance binaries.'
}

# Removed only from the FHG portable folder. These files service installation,
# OS background maintenance, and default browser registration; none are used
# to render ChatGPT or download/upload user files.
$optionalPaths=@(
    'uninstall/helper.exe',
    'maintenanceservice.exe',
    'maintenanceservice_installer.exe',
    'default-browser-agent.exe',
    # Auto-update is locked by policy; keep ALL browser engine and media DLLs.
    'updater.exe',
    # Optional telemetry/crash uploading executables, not rendering components.
    'pingsender.exe',
    'crashreporter.exe',
    'minidump-analyzer.exe'
)

# Runtime essentials MUST NOT be stripped, even for text-only usage.
$requiredPaths=@(
    'firefox.exe',
    'xul.dll',
    'omni.ja',
    'browser/omni.ja',
    'nss3.dll',
    'mozglue.dll',
    'gkcodecs.dll',
    'dxcompiler.dll',
    'libGLESv2.dll',
    'mozavcodec.dll',
    'onnxruntime.dll',
    'mozinference.dll',
    'distribution/policies.json'
)
$removed=0L
foreach($path in $optionalPaths){
    $full=Join-Path $root $path
    if(Test-Path -LiteralPath $full -PathType Leaf){
        $bytes=(Get-Item -LiteralPath $full).Length
        Write-Host "Optional installer-only asset: $path = $bytes bytes"
        if(-not $AuditOnly){
            Remove-Item -LiteralPath $full -Force
            $removed+=$bytes
        }
    }
}
if(-not $AuditOnly){
    $uninstall=Join-Path $root 'uninstall'
    if((Test-Path $uninstall) -and !(Get-ChildItem -LiteralPath $uninstall -Force | Select-Object -First 1)){
        Remove-Item -LiteralPath $uninstall -Force
    }
}
foreach($path in $requiredPaths){
    if(!(Test-Path -LiteralPath (Join-Path $root $path) -PathType Leaf)){
        throw "Required runtime component absent: $path"
    }
}
if((Get-AuthenticodeSignature -LiteralPath $ff).Status -ne 'Valid'){
    throw 'Firefox signature invalid after slimming.'
}
Write-Host "PASS: Firefox runtime and code signature intact. Removed optional support assets: $removed bytes. AuditOnly=$AuditOnly"
