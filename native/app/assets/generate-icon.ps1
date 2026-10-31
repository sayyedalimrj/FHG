# Generates FHG.ico locally at build time (no third-party icon or copyright).
Add-Type -AssemblyName System.Drawing
$path = Join-Path $PSScriptRoot 'FHG.ico'
$bmp = New-Object System.Drawing.Bitmap(64,64)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$navy = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(23,55,94))
$mint = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(56,217,184),5)
$white = New-Object System.Drawing.Pen([System.Drawing.Color]::White,4)
try {
  $g.FillEllipse($navy,3,3,58,58)
  $g.DrawEllipse($mint,10,10,44,44)
  $g.DrawLine($white,11,32,53,32)
  $g.DrawEllipse($white,22,10,20,44)
  $g.FillRectangle($navy,22,25,20,19)
  $g.DrawRectangle($mint,22,25,20,19)
  $g.DrawLines($white,[System.Drawing.Point[]]@(
    [System.Drawing.Point]::new(26,34),
    [System.Drawing.Point]::new(31,39),
    [System.Drawing.Point]::new(39,29)
  ))
  $h = $bmp.GetHicon()
  try {
    $icon=[System.Drawing.Icon]::FromHandle($h)
    $stream=[System.IO.File]::Create($path)
    try { $icon.Save($stream) } finally { $stream.Close() }
  } finally {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class NativeIcon { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle); }
'@ -ErrorAction SilentlyContinue
    [NativeIcon]::DestroyIcon($h) | Out-Null
  }
} finally { $g.Dispose(); $bmp.Dispose(); $navy.Dispose(); $mint.Dispose(); $white.Dispose() }
Write-Host "Created icon: $path"
