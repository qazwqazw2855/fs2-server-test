param(
    [Parameter(Mandatory = $true)]
    [int]$ProcessId,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class RadarNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        [Out] byte[] buffer,
        IntPtr size,
        out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualQueryEx(
        IntPtr process,
        IntPtr address,
        out MEMORY_BASIC_INFORMATION information,
        IntPtr informationLength);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    public static int[] Scan(byte[] buffer, int length)
    {
        int[] ids = { 312, 307, 234, 249 };
        int[] xs = { 248, 250, 262, 263 };
        int[] ys = { 442, 441, 439, 424 };
        var result = new List<int>();
        for (int i = 0; i <= length - 21; i++)
        {
            for (int a = 0; a < ids.Length; a++)
            {
                uint packed = ((uint)xs[a] << 2) | ((uint)ys[a] << 17);
                bool exact = buffer[i] == 0x71 &&
                    BitConverter.ToUInt32(buffer, i + 1) == (uint)ids[a] &&
                    BitConverter.ToUInt32(buffer, i + 9) == packed &&
                    BitConverter.ToUInt16(buffer, i + 13) == xs[a] &&
                    BitConverter.ToUInt16(buffer, i + 15) == ys[a];
                if (exact)
                {
                    result.Add(i); result.Add(a); result.Add(1);
                    continue;
                }

                if (BitConverter.ToUInt16(buffer, i) != xs[a] ||
                    BitConverter.ToUInt16(buffer, i + 2) != ys[a])
                    continue;

                int start = Math.Max(0, i - 128);
                int end = Math.Min(length - 4, i + 128);
                bool idFound = false;
                for (int j = start; j <= end; j++)
                {
                    if (BitConverter.ToUInt16(buffer, j) == ids[a] ||
                        BitConverter.ToUInt32(buffer, j) == (uint)ids[a])
                    {
                        idFound = true;
                        break;
                    }
                }
                if (idFound)
                {
                    result.Add(i); result.Add(a); result.Add(2);
                }
            }
        }
        return result.ToArray();
    }

    public static int[] ScanWorldRecords(byte[] buffer, int length)
    {
        var result = new List<int>();
        for (int i = 0; i <= length - 21; i++)
        {
            if (buffer[i] == 0x71)
            {
                uint packed = BitConverter.ToUInt32(buffer, i + 9);
                int x = (int)((packed >> 2) & 0x7fff);
                int y = (int)((packed >> 17) & 0x7fff);
                if (x > 0 && x < 4096 && y > 0 && y < 4096 &&
                    BitConverter.ToUInt16(buffer, i + 13) == x &&
                    BitConverter.ToUInt16(buffer, i + 15) == y)
                {
                    result.Add(i); result.Add(0x71);
                }
            }
            else if (buffer[i] == 0x72)
            {
                uint packed = BitConverter.ToUInt32(buffer, i + 17);
                int x = (int)((packed >> 2) & 0x7fff);
                int y = (int)((packed >> 17) & 0x7fff);
                if (x > 0 && x < 4096 && y > 0 && y < 4096 &&
                    buffer[i + 5] > 0 && buffer[i + 6] < 32)
                {
                    result.Add(i); result.Add(0x72);
                }
            }
        }
        return result.ToArray();
    }
}
'@

$anchors = @(
    [pscustomobject]@{ ObjectId = 312; X = 248; Y = 442 },
    [pscustomobject]@{ ObjectId = 307; X = 250; Y = 441 },
    [pscustomobject]@{ ObjectId = 234; X = 262; Y = 439 },
    [pscustomobject]@{ ObjectId = 249; X = 263; Y = 424 }
)

function Read-U16([byte[]]$Buffer, [int]$Offset) {
    [BitConverter]::ToUInt16($Buffer, $Offset)
}

function Read-U32([byte[]]$Buffer, [int]$Offset) {
    [BitConverter]::ToUInt32($Buffer, $Offset)
}

function Get-HexWindow([byte[]]$Buffer, [int]$Offset) {
    $start = [Math]::Max(0, $Offset - 48)
    $count = [Math]::Min(160, $Buffer.Length - $start)
    ([BitConverter]::ToString($Buffer, $start, $count)).Replace('-', '')
}

$PROCESS_QUERY_INFORMATION = 0x0400
$PROCESS_VM_READ = 0x0010
$MEM_COMMIT = 0x1000
$PAGE_GUARD = 0x100
$PAGE_NOACCESS = 0x01
$handle = [RadarNative]::OpenProcess($PROCESS_QUERY_INFORMATION -bor $PROCESS_VM_READ, $false, $ProcessId)
if ($handle -eq [IntPtr]::Zero) {
    throw "OpenProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
}

$hits = [Collections.Generic.List[object]]::new()
$worldRecords = [Collections.Generic.List[object]]::new()
$regionsRead = 0
$bytesReadTotal = [uint64]0
$address = [uint64]0
$maximumAddress = [uint64]0x7fffffff
$mbiSize = [Runtime.InteropServices.Marshal]::SizeOf([type][RadarNative+MEMORY_BASIC_INFORMATION])

try {
    while ($address -lt $maximumAddress) {
        $mbi = New-Object RadarNative+MEMORY_BASIC_INFORMATION
        $queried = [RadarNative]::VirtualQueryEx($handle, [IntPtr]([int64]$address), [ref]$mbi, [IntPtr]$mbiSize)
        if ($queried -eq [IntPtr]::Zero) { break }

        $base = [uint64]$mbi.BaseAddress.ToInt64()
        $size = $mbi.RegionSize.ToUInt64()
        if ($size -eq 0) { break }
        $readable = $mbi.State -eq $MEM_COMMIT -and
            ($mbi.Protect -band $PAGE_GUARD) -eq 0 -and
            ($mbi.Protect -band $PAGE_NOACCESS) -eq 0

        if ($readable) {
            $chunkOffset = [uint64]0
            while ($chunkOffset -lt $size) {
                $chunkSize = [int][Math]::Min([uint64](1024 * 1024), $size - $chunkOffset)
                $buffer = New-Object byte[] $chunkSize
                $actual = [IntPtr]::Zero
                $chunkAddress = $base + $chunkOffset
                if ([RadarNative]::ReadProcessMemory($handle, [IntPtr]([int64]$chunkAddress), $buffer, [IntPtr]$chunkSize, [ref]$actual) -and $actual.ToInt64() -gt 0) {
                    $length = [int]$actual.ToInt64()
                    $regionsRead++
                    $bytesReadTotal += [uint64]$length
                    $scanHits = [RadarNative]::Scan($buffer, $length)
                    for ($h = 0; $h -lt $scanHits.Length; $h += 3) {
                        $i = $scanHits[$h]
                        $anchor = $anchors[$scanHits[$h + 1]]
                        $kind = if ($scanHits[$h + 2] -eq 1) { 'ExactMonsterWireRecord' } else { 'RuntimeObjectCandidate' }
                        $hits.Add([pscustomobject]@{
                            Kind = $kind
                            ObjectId = $anchor.ObjectId
                            X = $anchor.X
                            Y = $anchor.Y
                            Address = ('0x{0:X8}' -f ($chunkAddress + [uint64]$i))
                            RegionBase = ('0x{0:X8}' -f $base)
                            HexWindow = Get-HexWindow $buffer $i
                        })
                        if ($hits.Count -ge 512) { break }
                    }
                    $recordHits = [RadarNative]::ScanWorldRecords($buffer, $length)
                    for ($h = 0; $h -lt $recordHits.Length; $h += 2) {
                        $i = $recordHits[$h]
                        $opcode = $recordHits[$h + 1]
                        if ($opcode -eq 0x71) {
                            $packed = Read-U32 $buffer ($i + 9)
                            $worldRecords.Add([pscustomobject]@{
                                Kind = 'Monster'
                                ObjectId = Read-U16 $buffer ($i + 1)
                                TemplateOrdinal = Read-U16 $buffer ($i + 4)
                                X = ($packed -shr 2) -band 0x7fff
                                Y = ($packed -shr 17) -band 0x7fff
                                Address = ('0x{0:X8}' -f ($chunkAddress + [uint64]$i))
                                RecordHex = ([BitConverter]::ToString($buffer, $i, 21)).Replace('-', '')
                            })
                        }
                        else {
                            $packed = Read-U32 $buffer ($i + 17)
                            $worldRecords.Add([pscustomobject]@{
                                Kind = 'NPC'
                                ObjectId = Read-U32 $buffer ($i + 1)
                                ResourceOrdinal = $buffer[$i + 5] - 1
                                Selector = $buffer[$i + 6]
                                X = ($packed -shr 2) -band 0x7fff
                                Y = ($packed -shr 17) -band 0x7fff
                                Address = ('0x{0:X8}' -f ($chunkAddress + [uint64]$i))
                                RecordHex = ([BitConverter]::ToString($buffer, $i, 21)).Replace('-', '')
                            })
                        }
                        if ($worldRecords.Count -ge 4096) { break }
                    }
                }
                if ($hits.Count -ge 512) { break }
                $chunkOffset += [uint64]$chunkSize
            }
        }
        if ($hits.Count -ge 512) { break }
        $address = $base + $size
    }
}
finally {
    [void][RadarNative]::CloseHandle($handle)
}

$result = [ordered]@{
    SchemaVersion = 'god2-live-entity-radar-v1'
    CapturedAt = [DateTimeOffset]::Now.ToString('o')
    ProcessId = $ProcessId
    Mode = 'ReadOnlyExternalMemoryScan'
    RegionsRead = $regionsRead
    BytesRead = $bytesReadTotal
    AnchorCount = $anchors.Count
    HitCount = $hits.Count
    Hits = $hits
    WorldRecordCount = $worldRecords.Count
    WorldRecords = $worldRecords
}

$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$result | ConvertTo-Json -Depth 6
