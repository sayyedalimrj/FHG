# Experimental end-to-end WFP check on an ephemeral Windows runner.
# Does not use public internet. Uses two local TCP listeners.
$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false
$guard = Join-Path $PSScriptRoot "../../build/Release/FHGGuard.exe"
$probe = Join-Path $PSScriptRoot "../../build/Release/FHGProbe.exe"
$root = Join-Path $env:RUNNER_TEMP "fhg-wfp-test"
$bundle = Join-Path $root "FHG-Firefox"
$outside = Join-Path $root "unrelated.exe"
$browser = Join-Path $bundle "firefox.exe"
New-Item -ItemType Directory -Force -Path $bundle | Out-Null
Copy-Item $probe $browser -Force
Copy-Item $probe $outside -Force
$jobs = @()
$installed = $false

function Invoke-Probe([string]$binary, [int]$port, [bool]$shouldConnect) {
  & $binary "$port"
  $result = $LASTEXITCODE
  if ($shouldConnect -and $result -ne 0) { throw "Expected connect success for $binary -> $port (exit $result)" }
  if (!$shouldConnect -and $result -eq 0) { throw "Unexpected network bypass for $binary -> $port" }
}

try {
  foreach ($port in @(12334, 22334)) {
    $jobs += Start-Job -ArgumentList $port -ScriptBlock {
      param($p)
      $server = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, [int]$p)
      $server.Start()
      try {
        while ($true) {
          if ($server.Pending()) {
            $client = $server.AcceptTcpClient()
            $client.Close()
          }
          Start-Sleep -Milliseconds 30
        }
      } finally { $server.Stop() }
    }
  }
  Start-Sleep -Seconds 3
  # Prove both destinations work before protection is installed.
  Invoke-Probe $browser 12334 $true
  Invoke-Probe $browser 22334 $true
  & $guard install $bundle
  if ($LASTEXITCODE -ne 0) { throw "WFP installation failed" }
  $installed = $true
  & $guard status $bundle
  if ($LASTEXITCODE -ne 0) { throw "WFP rule verification failed" }
  Invoke-Probe $browser 12334 $true
  Invoke-Probe $browser 22334 $false
  # Targeting must not modify an unrelated executable.
  Invoke-Probe $outside 22334 $true
  # Simulate a proxy outage: browser remains running and cannot use an alternate route.
  Stop-Job $jobs[0] -ErrorAction Stop
  Invoke-Probe $browser 12334 $false
  Invoke-Probe $browser 22334 $false
  Write-Host "WFP localhost egress checks PASS (not a full browser leak test)."
} finally {
  if ($installed) {
    & $guard remove
    if ($LASTEXITCODE -ne 0) { Write-Warning "WFP rules need cleanup (ephemeral runner)" }
  }
  $jobs | Stop-Job -ErrorAction SilentlyContinue
  $jobs | Remove-Job -Force -ErrorAction SilentlyContinue
}
