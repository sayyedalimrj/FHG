param(
 [Parameter(Mandatory=$true)][string]$Controller,
 [Parameter(Mandatory=$true)][string]$Guard
)
$ErrorActionPreference='Stop'
# The icon must be embedded as RT_GROUP_ICON. A loose .ico next to an EXE is not enough.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FHGResourceCheck {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
 [DllImport("kernel32.dll", SetLastError=true)]
 public static extern IntPtr FindResource(IntPtr module,IntPtr name,IntPtr type);
 [DllImport("kernel32.dll")]
 public static extern bool FreeLibrary(IntPtr module);
}
'@
$m=[FHGResourceCheck]::LoadLibraryEx((Resolve-Path $Controller).Path,[IntPtr]::Zero,2)
if ($m -eq [IntPtr]::Zero) {throw 'Cannot map controller EXE'}
try {
 $group=[FHGResourceCheck]::FindResource($m,[IntPtr]101,[IntPtr]14)
 if ($group -eq [IntPtr]::Zero) {throw 'FHG controller icon RT_GROUP_ICON/101 missing'}
 Write-Host 'PASS: FHG icon is embedded in FHG.exe.'
} finally {[FHGResourceCheck]::FreeLibrary($m)|Out-Null}
$files=@($Controller,$Guard)
foreach ($file in $files) {
 $fi=Get-Item $file
 if ($fi.Length -lt 20000) {throw "Executable too small or missing: $file"}
 $sig=Get-AuthenticodeSignature -LiteralPath $fi.FullName
 Write-Host "Signature: $($fi.Name) = $($sig.Status)"
 if ($sig.Status -notin @('Valid','NotSigned')) {throw "Bad/invalid signature: $file, status=$($sig.Status)"}
}
