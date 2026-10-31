param(
 [Parameter(Mandatory=$true)][string]$Controller,
 [Parameter(Mandatory=$true)][string]$Guard
)
$ErrorActionPreference='Stop'
# Windows executable icon must be an embedded RT_GROUP_ICON, not a loose .ico.
# The resource ID is assigned by MSVC/dotnet and is NOT necessarily 101.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FHGResourceCheck {
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
 public static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,EntryPoint="EnumResourceNamesW")]
 public static extern bool EnumResourceNames(IntPtr module,IntPtr type,EnumResNameProc callback,IntPtr data);
 public delegate bool EnumResNameProc(IntPtr module,IntPtr type,IntPtr name,IntPtr data);
 [DllImport("kernel32.dll")]
 public static extern bool FreeLibrary(IntPtr module);
 public static bool HasGroupIcon(string file) {
  var m=LoadLibraryEx(file,IntPtr.Zero,2);
  if(m==IntPtr.Zero)return false;
  try {
   bool found=false;
   EnumResNameProc cb=(mod,type,name,data)=>{found=true;return false;};
   EnumResourceNames(m,(IntPtr)14,cb,IntPtr.Zero);
   GC.KeepAlive(cb);
   return found;
  } finally {FreeLibrary(m);}
 }
}
'@
$exe=(Resolve-Path $Controller).Path
if(![FHGResourceCheck]::HasGroupIcon($exe)) {throw 'FHG executable lacks an embedded icon (RT_GROUP_ICON)'}
Write-Host 'PASS: FHG application icon is embedded in the executable.'
foreach ($file in @($Controller,$Guard)) {
 $fi=Get-Item $file
 if ($fi.Length -lt 20000) {throw "Executable too small or missing: $file"}
 $sig=Get-AuthenticodeSignature -LiteralPath $fi.FullName
 Write-Host "Signature: $($fi.Name) = $($sig.Status)"
 if ($sig.Status -notin @('Valid','NotSigned')) {throw "Bad/invalid signature: $file, status=$($sig.Status)"}
}
