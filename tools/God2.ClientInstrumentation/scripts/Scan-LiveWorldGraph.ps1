param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [uint32]$RootSingleton = 0x008AD198,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WorldGraphNative {
 [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a,bool i,int p);
 [DllImport("kernel32.dll", SetLastError=true)] public static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,IntPtr s,out IntPtr r);
 [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@
function Read-Block([IntPtr]$Handle,[uint32]$Address,[int]$Length){$b=New-Object byte[] $Length;$n=[IntPtr]::Zero;$ok=[WorldGraphNative]::ReadProcessMemory($Handle,[IntPtr]([int64]$Address),$b,[IntPtr]$Length,[ref]$n);$actual=[int]$n.ToInt64();if($actual-le0){return $null};if($actual-lt$Length){$trim=New-Object byte[] $actual;[Array]::Copy($b,$trim,$actual);return $trim};$b}
function Has-U16([byte[]]$Bytes,[uint16]$Value){$a=[byte]($Value-band0xff);$b=[byte](($Value-shr8)-band0xff);for($i=0;$i-lt$Bytes.Length-1;$i++){if($Bytes[$i]-eq$a-and$Bytes[$i+1]-eq$b){return $true}};return $false}
$h=[WorldGraphNative]::OpenProcess(0x410,$false,$ProcessId);if($h-eq[IntPtr]::Zero){throw 'OpenProcess failed'}
try{
 $slot=[uint32]($RootSingleton+0x460);$slotBytes=Read-Block $h $slot 4;if(!$slotBytes){throw 'world handler slot unreadable'};$handler=[BitConverter]::ToUInt32($slotBytes,0);$manager=[uint32]($handler+0x01499FA0)
 $queue=[Collections.Generic.Queue[object]]::new();$queue.Enqueue([pscustomobject]@{Address=$manager;Depth=0;Path=('0x{0:X8}'-f$manager)});$seen=[Collections.Generic.HashSet[uint32]]::new();$candidates=[Collections.Generic.List[object]]::new();$nodes=0
 $tokens=[ordered]@{Object234=234;Object307=307;Object312=312;Template610=610;Template642=642;Template738=738;X248=248;X250=250;X254=254;X262=262;Y439=439;Y441=441;Y442=442;Y448=448}
 while($queue.Count-gt0-and$nodes-lt200){$node=$queue.Dequeue();if(!$seen.Add([uint32]$node.Address)){continue};$nodes++;$length=if($node.Depth-eq0){16384}elseif($node.Depth-eq1){4096}else{2048};$bytes=Read-Block $h ([uint32]$node.Address) $length;if(!$bytes){continue};$found=[ordered]@{};foreach($kv in $tokens.GetEnumerator()){if(Has-U16 $bytes ([uint16]$kv.Value)){$found[$kv.Key]=$kv.Value}};if($found.Count-ge3){$candidates.Add([pscustomobject]@{Address=('0x{0:X8}'-f[uint32]$node.Address);Depth=$node.Depth;Path=$node.Path;BytesRead=$bytes.Length;Score=$found.Count;Tokens=$found;HexPrefix=([BitConverter]::ToString($bytes,0,[Math]::Min(512,$bytes.Length))).Replace('-','')})};if($node.Depth-lt2){for($i=0;$i-le$bytes.Length-4;$i+=4){$p=[BitConverter]::ToUInt32($bytes,$i);if($p-ge0x01000000-and$p-lt0x70000000-and!$seen.Contains($p)){$queue.Enqueue([pscustomobject]@{Address=$p;Depth=$node.Depth+1;Path=($node.Path+' -> '+('0x{0:X8}'-f$p))});if($queue.Count-gt500){break}}}}
 }
 $result=[ordered]@{SchemaVersion='god2-live-world-graph-v1';ProcessId=$ProcessId;RootSingleton=('0x{0:X8}'-f$RootSingleton);Handler=('0x{0:X8}'-f$handler);Manager=('0x{0:X8}'-f$manager);NodesVisited=$nodes;CandidateCount=$candidates.Count;Candidates=@($candidates|Sort-Object Score -Descending)};$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $OutputPath -Encoding UTF8;$result|ConvertTo-Json -Depth 8
}finally{[void][WorldGraphNative]::CloseHandle($h)}
