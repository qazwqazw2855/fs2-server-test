param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][uint32]$TargetAddress,
    [uint32]$ModuleBase = 0x00400000,
    [uint32]$ModuleSize = 6942720,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class LiveCallerNative {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a,bool i,int p);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,IntPtr s,out IntPtr r);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
 public static int[] Find(byte[] b,int length,uint baseAddress,uint target) {
  var hits=new List<int>();
  for(int i=0;i<=length-5;i++) if(b[i]==0xE8) {
   long resolved=(long)baseAddress+i+5+BitConverter.ToInt32(b,i+1);
   if(resolved==target) hits.Add(i);
  }
  return hits.ToArray();
 }
}
'@
$h=[LiveCallerNative]::OpenProcess(0x410,$false,$ProcessId)
if($h-eq[IntPtr]::Zero){throw 'OpenProcess failed'}
try{$b=New-Object byte[] $ModuleSize;$n=[IntPtr]::Zero;if(![LiveCallerNative]::ReadProcessMemory($h,[IntPtr]([int64]$ModuleBase),$b,[IntPtr]$ModuleSize,[ref]$n)){throw "Read failed $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"};$hits=[LiveCallerNative]::Find($b,[int]$n.ToInt64(),$ModuleBase,$TargetAddress);$rows=foreach($i in $hits){$s=[Math]::Max(0,$i-64);$c=[Math]::Min(96,$b.Length-$s);[pscustomobject]@{CallAddress=('0x{0:X8}'-f([uint64]$ModuleBase+$i));WindowAddress=('0x{0:X8}'-f([uint64]$ModuleBase+$s));Hex=([BitConverter]::ToString($b,$s,$c)).Replace('-','')}};$result=[ordered]@{TargetAddress=('0x{0:X8}'-f$TargetAddress);CallerCount=@($rows).Count;Callers=@($rows)};$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $OutputPath -Encoding UTF8;$result|ConvertTo-Json -Depth 5}finally{[void][LiveCallerNative]::CloseHandle($h)}
