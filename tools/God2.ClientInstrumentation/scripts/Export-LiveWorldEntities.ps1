param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class WorldEntityNative {
 [DllImport("kernel32.dll",SetLastError=true)] public static extern IntPtr OpenProcess(uint a,bool i,int p);
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,IntPtr s,out IntPtr r);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@
function Read-Exact([IntPtr]$h,[uint32]$a,[int]$n){$b=New-Object byte[] $n;$r=[IntPtr]::Zero;if(![WorldEntityNative]::ReadProcessMemory($h,[IntPtr]([int64]$a),$b,[IntPtr]$n,[ref]$r)-or$r.ToInt64()-lt$n){return $null};$b}
$h=[WorldEntityNative]::OpenProcess(0x410,$false,$ProcessId);if($h-eq[IntPtr]::Zero){throw 'OpenProcess failed'}
try{$slot=Read-Exact $h 0x008AD5F8 4;if(!$slot){throw 'handler slot unreadable'};$handler=[BitConverter]::ToUInt32($slot,0);$manager=[uint32]($handler+0x01499FA0);$rows=[Collections.Generic.List[object]]::new();$stride=0x1D8;for($i=0;$i-lt256;$i++){$a=[uint32]($manager+0x70+$i*$stride);$b=Read-Exact $h $a $stride;if(!$b){continue};$id=[BitConverter]::ToUInt32($b,0);if($id-eq[uint32]::MaxValue){continue};$x=[BitConverter]::ToInt32($b,0x10);$y=[BitConverter]::ToInt32($b,0x14);$rx=[BitConverter]::ToInt32($b,0x180);$ry=[BitConverter]::ToInt32($b,0x184);$rows.Add([pscustomobject]@{Slot=$i;Address=('0x{0:X8}'-f$a);ObjectId=$id;Field04=[BitConverter]::ToUInt16($b,4);Field06=[BitConverter]::ToUInt16($b,6);Field08=[BitConverter]::ToInt16($b,8);Field0A=[BitConverter]::ToUInt16($b,0x0A);Field0C=[BitConverter]::ToUInt16($b,0x0C);Field0E=[BitConverter]::ToUInt16($b,0x0E);X=$x;Y=$y;RuntimeX=$rx;RuntimeY=$ry;Flags34=$b[0x34];Flags35=$b[0x35];State78=$b[0x78];HeadHex=([BitConverter]::ToString($b,0,64)).Replace('-','')})};$result=[ordered]@{SchemaVersion='god2-live-world-entities-v1';CapturedAt=[DateTimeOffset]::Now.ToString('o');ProcessId=$ProcessId;Handler=('0x{0:X8}'-f$handler);Manager=('0x{0:X8}'-f$manager);Capacity=256;Stride='0x1D8';NonEmptyCount=$rows.Count;Entities=$rows};$result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $OutputPath -Encoding UTF8;$result|ConvertTo-Json -Depth 6}finally{[void][WorldEntityNative]::CloseHandle($h)}
