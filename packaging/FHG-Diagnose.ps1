# FHG read-only connectivity diagnostics. Windows PowerShell 5.1 compatible.
# Never accepts a subscription URL on the command line, prints tokens, or edits
# firewall/profile data. Review output before sharing it.
param([string]$FhgRoot = '')
$ErrorActionPreference='Continue'
if(!$FhgRoot){$FhgRoot=$PSScriptRoot}
if(!(Test-Path (Join-Path $FhgRoot 'FHG.exe'))){throw 'Put this file next to FHG.exe, or pass -FhgRoot C:\path\to\extracted-FHG'}
$root=(Resolve-Path $FhgRoot).Path
$results=[Collections.Generic.List[string]]::new()
function Say([string]$t){$script:results.Add($t);Write-Host $t}
function Clean([string]$t){
 $t=[regex]::Replace($t,'(?i)\b[a-z][a-z0-9+.-]*://[^\s"<>]+','[URL REDACTED]')
 if($env:USERNAME){$t=$t.Replace($env:USERNAME,'[USER]')}
 $t=[regex]::Replace($t,'(?i)(token|secret|password|bearer)\s*[:=]\s*\S+','$1=[REDACTED]')
 return $t
}
Say ('FHG diagnostic (READ ONLY) '+[DateTime]::UtcNow.ToString('u'))
Say ('Bundle '+(Split-Path $root -Leaf))
Say '=== SIGNATURES ==='
foreach($name in @('FHG.exe','FHGGuard.exe','Hiddify\FHGProxy.exe','Firefox\firefox.exe')){
 $p=Join-Path $root $name
 if(!(Test-Path $p)){Say ("MISSING "+$name);continue}
 $sig=Get-AuthenticodeSignature $p -ErrorAction SilentlyContinue
 Say ("{0}: {1}" -f $name,$sig.Status)
}
Say '=== LOCAL LISTENERS ==='
$listen=@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue)
foreach($port in @(17078,27178,16756,26756,12334,12345)){
 $hit=@($listen|Where-Object {$_.LocalPort -eq $port -and
  $_.LocalAddress -in @('127.0.0.1','0.0.0.0','::1','::')})
 if(!$hit.Count){Say ("Port {0}: NOT LISTENING" -f $port);continue}
 foreach($h in $hit){
  $proc='unknown'
  try{$proc=(Get-Process -Id $h.OwningProcess -ErrorAction Stop).ProcessName}catch{}
  Say ("Port {0} = {1} process={2},pid={3}" -f $port,$h.LocalAddress,$proc,$h.OwningProcess)
 }
}
Say '=== EXPLICIT LOCAL PROXY HTTPS CHECK (NEVER DIRECT) ==='
if(Get-Command curl.exe -ErrorAction SilentlyContinue){
 foreach($port in @(12345,12334)){
  if(!(@($listen|Where-Object LocalPort -eq $port).Count)){continue}
  foreach($scheme in @('socks5h','http')){
   $code=& curl.exe -q --silent --connect-timeout 5 --max-time 14 --noproxy '' --proxy ("{0}://127.0.0.1:{1}" -f $scheme,$port) --output NUL --write-out '%{http_code}' https://example.com/ 2>$null
   Say ("Port {0}, {1}: curl exit={2}, HTTP={3}" -f $port,$scheme,$LASTEXITCODE,([string]$code))
  }
 }
}else{Say 'curl.exe not installed; skipped external HTTPS check.'}
Say '=== CORE / GUARD ==='
if(!(@($listen|Where-Object LocalPort -eq 27178).Count)){
 Say 'FHG control gRPC :27178 is NOT LISTENING. Core startup/port conflict may block profile import.'
}else{Say 'FHG 27178 TCP listener exists; this does not prove gRPC parse readiness.'}
$guard=Join-Path $root 'FHGGuard.exe'
if(Test-Path $guard){
 $guardMessage=& $guard status (Join-Path $root 'Firefox') 2>&1 | Out-String
 Say ("Guard status exit: {0}" -f $LASTEXITCODE)
 foreach($line in ($guardMessage -split "\r?\n"|Select-Object -First 4)){if($line){Say (Clean $line)}}
}
$dir=Join-Path $root 'Hiddify\fhg_proxy_portable_data'
if(!(Test-Path $dir)){Say 'FHG portable data directory not found; startup may have failed.'}
else{
 $logs=@(Get-ChildItem -LiteralPath $dir -File -Recurse -ErrorAction SilentlyContinue|
  Where-Object {$_.Name -match '^(app|box|core)\.log$'}|Select-Object -First 3)
 foreach($f in $logs){
  Say ('Recent sanitized '+$f.Name+' diagnostics:')
  Get-Content -LiteralPath $f.FullName -Tail 180 -ErrorAction SilentlyContinue|
   Where-Object {$_ -match '(?i)error|failed|exception|grpc|bind|timeout|refused|parse'}|
   Select-Object -Last 10|ForEach-Object{Say (Clean $_)}
 }
}
Say 'Interpretation: no 27178 => core/init; 27178 with no 12345 after Connect => config/start; 12345 with HTTPS failure => node/outbound/routing/DNS.'
Say 'Please review the report before sharing; do not share URLs, account secrets or TOTP keys.'
$desktop=[Environment]::GetFolderPath('Desktop')
if(!$desktop){$desktop=$env:TEMP}
$file=Join-Path $desktop ('FHG-diagnostic-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'.txt')
[IO.File]::WriteAllLines($file,$results.ToArray(),[Text.Encoding]::UTF8)
Write-Host ('Report saved: '+$file) -ForegroundColor Cyan
