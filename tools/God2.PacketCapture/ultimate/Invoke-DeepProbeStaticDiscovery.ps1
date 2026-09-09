[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetExecutable,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ExpectedSHA256 = '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B',
    [string]$ExpectedVersion = '1.0.0.1',
    [ValidateRange(0,2147483647)][int]$TargetProcessId = 0,
    [ValidateRange(1,4)][int]$MaximumCandidatesPerDomain = 2,
    [ValidateRange(1,4)][int]$MaximumGraphDepth = 2,
    [ValidateRange(512,16384)][int]$MaximumFunctionBytes = 4096
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:God2StaticBytes = $null
$script:God2StaticSections = @()

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class God2BoundedImageReader {
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        byte[] buffer, UIntPtr size, out UIntPtr bytesRead);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static byte[] ReadImage(uint processId, long imageBase, int imageSize) {
        if (processId == 0 || imageBase <= 0 || imageSize <= 0 || imageSize > 16 * 1024 * 1024)
            throw new InvalidOperationException("Runtime image request is outside the bounded contract.");
        IntPtr process = OpenProcess(0x0010u | 0x1000u, false, processId);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            byte[] result = new byte[imageSize];
            UIntPtr read;
            if (!ReadProcessMemory(process, new IntPtr(imageBase), result,
                    new UIntPtr((uint)imageSize), out read) || read.ToUInt64() != (ulong)imageSize)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return result;
        } finally { CloseHandle(process); }
    }

    private static bool InRange(uint value, uint[] rvas, int[] sizes) {
        for (int i = 0; i < rvas.Length; ++i) {
            ulong start = rvas[i], end = start + (uint)sizes[i];
            if ((ulong)value >= start && (ulong)value < end) return true;
        }
        return false;
    }

    public static ulong[] ScanDirectCalls(byte[] image, int[] offsets,
            uint[] rvas, int[] sizes) {
        var result = new List<ulong>();
        for (int range = 0; range < offsets.Length; ++range) {
            int start = offsets[range], size = sizes[range];
            if (start < 0 || size < 5 || start + size > image.Length) continue;
            int end = start + size - 5;
            for (int offset = start; offset <= end; ++offset) {
                if (image[offset] != 0xE8) continue;
                uint callsite = rvas[range] + (uint)(offset - start);
                int relative = BitConverter.ToInt32(image, offset + 1);
                long target = (long)callsite + 5L + relative;
                if (target < 0 || target > UInt32.MaxValue ||
                    !InRange((uint)target, rvas, sizes)) continue;
                result.Add(((ulong)callsite << 32) | (uint)target);
            }
        }
        return result.ToArray();
    }

    public static ulong[] ScanVtableRuns(byte[] image, int[] offsets,
            uint[] rvas, int[] sizes, uint imageBase, int maximumRuns) {
        var result = new List<ulong>();
        for (int range = 0; range < offsets.Length && result.Count < maximumRuns; ++range) {
            int start = offsets[range], end = start + sizes[range], runStart = -1, count = 0;
            if (start < 0 || sizes[range] < 16 || end > image.Length) continue;
            for (int offset = start; offset + 4 <= end; offset += 4) {
                uint value = BitConverter.ToUInt32(image, offset);
                long target = (long)value - imageBase;
                bool code = target >= 0 && target <= UInt32.MaxValue &&
                    InRange((uint)target, rvas, sizes);
                if (code) {
                    if (runStart < 0) { runStart = offset; count = 0; }
                    ++count;
                    continue;
                }
                if (runStart >= 0 && count >= 4) {
                    uint runRva = rvas[range] + (uint)(runStart - start);
                    result.Add(((ulong)runRva << 32) | (uint)count);
                    if (result.Count >= maximumRuns) break;
                }
                runStart = -1; count = 0;
            }
        }
        return result.ToArray();
    }
}
'@

function Read-God2UInt16([int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 2 -gt $script:God2StaticBytes.Length) {
        throw "PE read outside file at offset $Offset."
    }
    return [BitConverter]::ToUInt16($script:God2StaticBytes, $Offset)
}

function Read-God2UInt32([int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $script:God2StaticBytes.Length) {
        throw "PE read outside file at offset $Offset."
    }
    return [BitConverter]::ToUInt32($script:God2StaticBytes, $Offset)
}

function Read-God2Int32([int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $script:God2StaticBytes.Length) {
        throw "PE read outside file at offset $Offset."
    }
    return [BitConverter]::ToInt32($script:God2StaticBytes, $Offset)
}

function Convert-God2RvaToOffset([uint32]$Rva) {
    foreach ($section in $script:God2StaticSections) {
        $span = [Math]::Max([uint32]$section.VirtualSize, [uint32]$section.RawSize)
        if ($Rva -lt [uint32]$section.RVA -or $Rva - [uint32]$section.RVA -ge $span) {
            continue
        }
        $relative = [uint64]$Rva - [uint64]$section.RVA
        if ($relative -ge [uint64]$section.RawSize) { return $null }
        $offset = [uint64]$section.RawOffset + $relative
        if ($offset -ge [uint64]$script:God2StaticBytes.Length) { return $null }
        return [int]$offset
    }
    return $null
}

function Get-God2SectionForRva([uint32]$Rva) {
    foreach ($section in $script:God2StaticSections) {
        $span = [Math]::Max([uint32]$section.VirtualSize, [uint32]$section.RawSize)
        if ($Rva -ge [uint32]$section.RVA -and $Rva - [uint32]$section.RVA -lt $span) {
            return $section
        }
    }
    return $null
}

function Test-God2ExecutableRawRva([uint32]$Rva) {
    $section = Get-God2SectionForRva $Rva
    if ($null -eq $section -or -not [bool]$section.Executable) { return $false }
    return $null -ne (Convert-God2RvaToOffset $Rva)
}

function Format-God2Rva([uint32]$Rva) {
    return '0x{0:X8}' -f $Rva
}

function Format-God2Bytes([int]$Offset, [int]$Count) {
    if ($Offset -lt 0 -or $Count -lt 1 -or $Offset -ge $script:God2StaticBytes.Length) {
        return ''
    }
    $available = [Math]::Min($Count, $script:God2StaticBytes.Length - $Offset)
    $parts = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $available; ++$index) {
        [void]$parts.Add(('{0:X2}' -f $script:God2StaticBytes[$Offset + $index]))
    }
    return $parts -join ' '
}

function Get-God2AsciiZ([int]$Offset, [int]$MaximumBytes = 512) {
    if ($Offset -lt 0 -or $Offset -ge $script:God2StaticBytes.Length) { return $null }
    $length = 0
    while ($length -lt $MaximumBytes -and $Offset + $length -lt $script:God2StaticBytes.Length -and
           $script:God2StaticBytes[$Offset + $length] -ne 0) {
        ++$length
    }
    if ($length -eq 0 -or $length -ge $MaximumBytes) { return $null }
    return [Text.Encoding]::ASCII.GetString($script:God2StaticBytes, $Offset, $length)
}

function Get-God2FunctionBoundary([uint32]$Rva, [int]$MaximumBytes) {
    $section = Get-God2SectionForRva $Rva
    $offset = Convert-God2RvaToOffset $Rva
    if ($null -eq $section -or $null -eq $offset) {
        return [pscustomobject][ordered]@{ Status='EVIDENCE_BLOCKED_NO_RAW_BYTES'; StartRVA=$null; EndRVA=$null; Confidence='NONE' }
    }
    $sectionRawStart = [int]$section.RawOffset
    $sectionRawEnd = [Math]::Min($script:God2StaticBytes.Length,
        [int]$section.RawOffset + [int]$section.RawSize)
    $lower = [Math]::Max($sectionRawStart, [int]$offset - 512)
    $startOffset = [int]$offset
    $confidence = 'ENTRY_ONLY'
    for ($position = [int]$offset; $position -ge $lower; --$position) {
        $remaining = $sectionRawEnd - $position
        if ($remaining -ge 3 -and $script:God2StaticBytes[$position] -eq 0x55 -and
            $script:God2StaticBytes[$position + 1] -eq 0x8B -and
            $script:God2StaticBytes[$position + 2] -eq 0xEC) {
            $startOffset = $position
            $confidence = 'HEURISTIC_FRAME_PROLOGUE'
            break
        }
        if ($remaining -ge 2 -and $script:God2StaticBytes[$position] -eq 0x8B -and
            $script:God2StaticBytes[$position + 1] -eq 0xFF) {
            $startOffset = $position
            $confidence = 'HEURISTIC_HOTPATCH_PROLOGUE'
            break
        }
    }
    $upper = [Math]::Min($sectionRawEnd - 1, $startOffset + $MaximumBytes - 1)
    $endOffset = $upper
    $endKind = 'BOUNDED_WINDOW_END'
    for ($position = [Math]::Max([int]$offset, $startOffset); $position -le $upper; ++$position) {
        $opcode = $script:God2StaticBytes[$position]
        if ($opcode -eq 0xC3) {
            $endOffset = $position
            $endKind = 'RET_NEAR'
            break
        }
        if ($opcode -eq 0xC2 -and $position + 2 -le $upper) {
            $endOffset = $position + 2
            $endKind = 'RET_NEAR_STACK'
            break
        }
    }
    $startRva = [uint32]([uint64]$section.RVA + [uint64]($startOffset - [int]$section.RawOffset))
    $endRva = [uint32]([uint64]$section.RVA + [uint64]($endOffset - [int]$section.RawOffset))
    return [pscustomobject][ordered]@{
        Status='HEURISTIC_NOT_RUNTIME_VERIFIED'
        StartRVA=Format-God2Rva $startRva
        EndRVA=Format-God2Rva $endRva
        StartRvaValue=$startRva
        EndRvaValue=$endRva
        StartOffset=$startOffset
        EndOffset=$endOffset
        Confidence=$confidence
        EndKind=$endKind
    }
}

function Get-God2FunctionFeatures([object]$Boundary, [hashtable]$CallsitesByRva,
                                  [hashtable]$VtableVirtualAddresses) {
    $start = [int]$Boundary.StartOffset
    $end = [int]$Boundary.EndOffset
    $directCalls = [Collections.Generic.List[object]]::new()
    $conditionalBranches = 0
    $backwardBranches = 0
    $indirectCalls = 0
    $stackArgumentReads = 0
    $thisFieldReads = 0
    $thisFieldWrites = 0
    $arithmeticOperations = 0
    $comparisonOperations = 0
    $immediatePushes = 0
    $absoluteTableReferences = [Collections.Generic.List[string]]::new()
    $objectOffsets = [Collections.Generic.List[string]]::new()
    $stackOffsets = [Collections.Generic.List[string]]::new()
    for ($offset = $start; $offset -le $end; ++$offset) {
        $opcode = $script:God2StaticBytes[$offset]
        $rva = [uint32]([uint64]$Boundary.StartRvaValue + [uint64]($offset - $start))
        $rvaKey = [string]$rva
        if ($CallsitesByRva.ContainsKey($rvaKey)) {
            [void]$directCalls.Add($CallsitesByRva[$rvaKey])
        }
        if (($opcode -ge 0x70 -and $opcode -le 0x7F) -and $offset + 1 -le $end) {
            ++$conditionalBranches
            if ($script:God2StaticBytes[$offset + 1] -ge 0x80) { ++$backwardBranches }
        } elseif ($opcode -eq 0x0F -and $offset + 5 -le $end -and
                  $script:God2StaticBytes[$offset + 1] -ge 0x80 -and
                  $script:God2StaticBytes[$offset + 1] -le 0x8F) {
            ++$conditionalBranches
            if ((Read-God2Int32 ($offset + 2)) -lt 0) { ++$backwardBranches }
        }
        if ($opcode -eq 0xFF -and $offset + 1 -le $end -and
            (($script:God2StaticBytes[$offset + 1] -band 0x38) -eq 0x10)) {
            ++$indirectCalls
        }
        if (($opcode -eq 0x8B -or $opcode -eq 0x89 -or $opcode -eq 0x0F) -and
            $offset + 3 -le $end) {
            $modrmOffset = if ($opcode -eq 0x0F) { $offset + 2 } else { $offset + 1 }
            if ($modrmOffset -le $end) {
                $modrm = $script:God2StaticBytes[$modrmOffset]
                $mod = ($modrm -shr 6) -band 3
                $rm = $modrm -band 7
                if (($mod -eq 1 -or $mod -eq 2) -and $rm -eq 1) {
                    $dispBytes = if ($mod -eq 1) { 1 } else { 4 }
                    if ($modrmOffset + $dispBytes -le $end) {
                        $disp = if ($dispBytes -eq 1) {
                            $rawDisp = [int]$script:God2StaticBytes[$modrmOffset + 1]
                            if ($rawDisp -ge 0x80) { $rawDisp - 0x100 } else { $rawDisp }
                        } else { Read-God2Int32 ($modrmOffset + 1) }
                        [void]$objectOffsets.Add(('ECX{0}{1:X}' -f $(if($disp -lt 0){'-'}else{'+'}),[Math]::Abs($disp)))
                        if ($opcode -eq 0x89) { ++$thisFieldWrites } else { ++$thisFieldReads }
                    }
                }
            }
        }
        if ($offset + 3 -le $end -and $script:God2StaticBytes[$offset] -in @(0x8B,0x89,0x0F) -and
            $script:God2StaticBytes[$offset + 2] -eq 0x24) {
            $modrm = $script:God2StaticBytes[$offset + 1]
            if ((($modrm -shr 6) -band 3) -eq 1) {
                ++$stackArgumentReads
                [void]$stackOffsets.Add(('ESP+{0:X2}' -f $script:God2StaticBytes[$offset + 3]))
            }
        }
        if ($opcode -in @(0x01,0x03,0x29,0x2B,0x31,0x33,0x69,0x6B,0xD1,0xD3)) {
            ++$arithmeticOperations
        }
        if ($opcode -in @(0x39,0x3B,0x3D,0x84,0x85)) { ++$comparisonOperations }
        if ($opcode -eq 0x68 -or $opcode -eq 0x6A) { ++$immediatePushes }
        if ($offset + 3 -le $end) {
            $value = [BitConverter]::ToUInt32($script:God2StaticBytes, $offset)
            if ($VtableVirtualAddresses.ContainsKey([string]$value)) {
                $formatted = '0x{0:X8}' -f $value
                if (-not $absoluteTableReferences.Contains($formatted)) {
                    [void]$absoluteTableReferences.Add($formatted)
                }
            }
        }
    }
    $uniqueObjectOffsets = @($objectOffsets | Select-Object -Unique)
    $uniqueStackOffsets = @($stackOffsets | Select-Object -Unique)
    $returnStackBytes = $null
    if ($end - $start -ge 2 -and $script:God2StaticBytes[$end - 2] -eq 0xC2) {
        $returnStackBytes = [uint16](Read-God2UInt16 ($end - 1))
    }
    $callingConvention = if ($thisFieldReads + $thisFieldWrites -gt 0) {
        if ($null -ne $returnStackBytes) { 'thiscall-or-stdcall-candidate' } else { 'thiscall-or-cdecl-candidate' }
    } elseif ($null -ne $returnStackBytes) { 'stdcall-candidate' } else { 'cdecl-candidate' }
    $arguments = [Collections.Generic.List[object]]::new()
    if ($thisFieldReads + $thisFieldWrites -gt 0) {
        [void]$arguments.Add([pscustomobject][ordered]@{ Location='ECX'; Role='this-pointer-candidate'; Verified=$false })
    }
    foreach ($stackOffset in $uniqueStackOffsets | Select-Object -First 8) {
        [void]$arguments.Add([pscustomobject][ordered]@{ Location=$stackOffset; Role='stack-argument-candidate'; Verified=$false })
    }
    return [pscustomobject][ordered]@{
        FunctionBytes=($end - $start + 1)
        DirectCallCount=$directCalls.Count
        DirectCalls=@($directCalls | ForEach-Object { $_.TargetRVA } | Select-Object -Unique)
        ConditionalBranchCount=$conditionalBranches
        BackwardBranchCount=$backwardBranches
        IndirectCallCount=$indirectCalls
        StackArgumentReadCount=$stackArgumentReads
        StackOffsets=$uniqueStackOffsets
        ThisFieldReadCount=$thisFieldReads
        ThisFieldWriteCount=$thisFieldWrites
        RepeatedObjectOffsets=@($uniqueObjectOffsets)
        ArithmeticOperationCount=$arithmeticOperations
        ComparisonOperationCount=$comparisonOperations
        ImmediatePushCount=$immediatePushes
        VtableLikeReferences=@($absoluteTableReferences)
        CallingConventionCandidate=$callingConvention
        ArgumentCandidates=@($arguments)
        ReturnStackBytes=$returnStackBytes
    }
}

function Get-God2DomainScore([string]$Domain, [object]$Features, [int]$FanIn,
                             [string]$SeedDomain, [int]$Depth) {
    $score = 12 + [Math]::Min(18, $FanIn * 3) + [Math]::Max(0, 8 - ($Depth * 3))
    $objectAccess = [int]$Features.ThisFieldReadCount + [int]$Features.ThisFieldWriteCount
    switch ($Domain) {
        'Object' { $score += [Math]::Min(35,$objectAccess * 4) }
        'Allocation' { $score += [Math]::Min(24,[int]$Features.ImmediatePushCount * 3) + [Math]::Min(12,[int]$Features.DirectCallCount) }
        'VTable' { $score += [Math]::Min(40,@($Features.VtableLikeReferences).Count * 12) + [Math]::Min(12,[int]$Features.IndirectCallCount * 3) }
        'Factory' { $score += [Math]::Min(24,[int]$Features.DirectCallCount * 2) + [Math]::Min(16,$objectAccess * 2) }
        'ManagerLookup' { $score += [Math]::Min(24,[int]$Features.ComparisonOperationCount * 2) + [Math]::Min(18,[int]$Features.BackwardBranchCount * 6) }
        'Registry' { $score += [Math]::Min(30,[int]$Features.BackwardBranchCount * 8) + [Math]::Min(14,$FanIn * 2) }
        'ResourceDecode' { $score += [Math]::Min(25,[int]$Features.ArithmeticOperationCount) + [Math]::Min(20,[int]$Features.BackwardBranchCount * 5) }
        'Mutation' { $score += [Math]::Min(42,[int]$Features.ThisFieldWriteCount * 7) }
        'TaintSeed' { $score += [Math]::Min(24,$objectAccess * 2) + [Math]::Min(18,[int]$Features.ArithmeticOperationCount) }
        'FormulaOperand' { $score += [Math]::Min(38,[int]$Features.ArithmeticOperationCount * 3) + [Math]::Min(12,[int]$Features.StackArgumentReadCount * 2) }
        default { $score += [Math]::Min(20,$objectAccess * 2) + [Math]::Min(20,[int]$Features.ComparisonOperationCount + [int]$Features.ArithmeticOperationCount) }
    }
    if ($Domain -in @('Mutation','FormulaOperand','Monster','Battle','Skill','Pet/Mount') -and $SeedDomain -ceq 'Handler') { $score += 8 }
    if ($Domain -eq 'Inventory' -and $SeedDomain -ceq 'Serializer') { $score += 10 }
    if ($Domain -in @('Object','Allocation','VTable','Factory','ManagerLookup','Registry','ResourceDecode','TaintSeed','Quest','Map','Portal','NPC','Item','Snapshot') -and $SeedDomain -ceq 'Parser') { $score += 6 }
    return [Math]::Min(99, $score)
}

$resolvedTarget = (Resolve-Path -LiteralPath $TargetExecutable).Path
$targetInfo = Get-Item -LiteralPath $resolvedTarget
$targetSha = (Get-FileHash -LiteralPath $resolvedTarget -Algorithm SHA256).Hash.ToUpperInvariant()
$targetVersion = [string]$targetInfo.VersionInfo.FileVersion
if ($targetSha -cne $ExpectedSHA256.ToUpperInvariant()) {
    throw "Exact target SHA-256 mismatch: expected $ExpectedSHA256, observed $targetSha."
}
if ($targetVersion -cne $ExpectedVersion) {
    throw "Exact target version mismatch: expected $ExpectedVersion, observed $targetVersion."
}

$script:God2StaticBytes = [IO.File]::ReadAllBytes($resolvedTarget)
if ($script:God2StaticBytes.Length -lt 512 -or (Read-God2UInt16 0) -ne 0x5A4D) {
    throw 'Target is not a valid DOS/PE image.'
}
$peOffset = [int](Read-God2UInt32 0x3C)
if ($peOffset -lt 64 -or $peOffset + 248 -gt $script:God2StaticBytes.Length -or
    (Read-God2UInt32 $peOffset) -ne 0x00004550) {
    throw 'Target PE signature is invalid.'
}
$machine = Read-God2UInt16 ($peOffset + 4)
$sectionCount = Read-God2UInt16 ($peOffset + 6)
$optionalSize = Read-God2UInt16 ($peOffset + 20)
$optionalOffset = $peOffset + 24
if ($machine -ne 0x014C -or (Read-God2UInt16 $optionalOffset) -ne 0x010B -or
    $sectionCount -lt 1 -or $sectionCount -gt 96 -or $optionalSize -lt 224) {
    throw 'Only bounded PE32/x86 analysis is permitted.'
}
$entryRva = Read-God2UInt32 ($optionalOffset + 16)
$imageBase = Read-God2UInt32 ($optionalOffset + 28)
$sizeOfImage = Read-God2UInt32 ($optionalOffset + 56)
$sectionOffset = $optionalOffset + $optionalSize
$sections = [Collections.Generic.List[object]]::new()
for ($index = 0; $index -lt $sectionCount; ++$index) {
    $offset = $sectionOffset + ($index * 40)
    if ($offset + 40 -gt $script:God2StaticBytes.Length) { throw 'PE section table is truncated.' }
    $name = [Text.Encoding]::ASCII.GetString($script:God2StaticBytes, $offset, 8).Trim([char]0)
    if ([string]::IsNullOrWhiteSpace($name)) { $name = 'SECTION_{0:D2}' -f $index }
    $virtualSize = Read-God2UInt32 ($offset + 8)
    $rva = Read-God2UInt32 ($offset + 12)
    $rawSize = Read-God2UInt32 ($offset + 16)
    $rawOffset = Read-God2UInt32 ($offset + 20)
    $characteristics = Read-God2UInt32 ($offset + 36)
    $rawBacked = $rawSize -gt 0 -and [uint64]$rawOffset + [uint64]$rawSize -le [uint64]$script:God2StaticBytes.Length
    [void]$sections.Add([pscustomobject][ordered]@{
        Index=$index; Name=$name; RVA=$rva; VirtualSize=$virtualSize
        RawOffset=$rawOffset; RawSize=$rawSize
        Characteristics=('0x{0:X8}' -f $characteristics)
        Executable=(($characteristics -band 0x20000000) -ne 0)
        Writable=(($characteristics -band 0x80000000) -ne 0)
        Readable=(($characteristics -band 0x40000000) -ne 0)
        RawBacked=$rawBacked
    })
}
$script:God2StaticSections = @($sections)
$runtimeImageObserved = $false
$runtimeProcessCreationTime = $null
if ($TargetProcessId -ne 0) {
    $process = Get-Process -Id $TargetProcessId -ErrorAction Stop
    $process.Refresh()
    if ($process.HasExited) { throw 'Target process exited before bounded image acquisition.' }
    $processPath = [IO.Path]::GetFullPath([string]$process.MainModule.FileName)
    if ($processPath -cne [IO.Path]::GetFullPath($resolvedTarget)) {
        throw "Runtime process image path does not match the exact target: $processPath"
    }
    $moduleBase = [int64]$process.MainModule.BaseAddress
    $moduleSize = [int]$process.MainModule.ModuleMemorySize
    if ($moduleBase -ne [int64]$imageBase -or $moduleSize -lt [int]$sizeOfImage -or
        $moduleSize -gt 16MB) {
        throw "Runtime image bounds do not match the PE contract: base=$moduleBase size=$moduleSize."
    }
    $runtimeProcessCreationTime = $process.StartTime.ToUniversalTime().ToString('o')
    $script:God2StaticBytes = [God2BoundedImageReader]::ReadImage(
        [uint32]$TargetProcessId, $moduleBase, [int]$sizeOfImage)
    if ((Read-God2UInt16 0) -ne 0x5A4D -or (Read-God2UInt32 $peOffset) -ne 0x00004550) {
        throw 'Bounded runtime image did not preserve the exact PE identity headers.'
    }
    foreach ($section in $script:God2StaticSections) {
        $section.RawOffset = [uint32]$section.RVA
        $available = [Math]::Max([uint32]$section.VirtualSize, [uint32]$section.RawSize)
        if ([uint64]$section.RVA + [uint64]$available -gt [uint64]$script:God2StaticBytes.Length) {
            $available = [uint32]([uint64]$script:God2StaticBytes.Length - [uint64]$section.RVA)
        }
        $section.RawSize = $available
        $section.RawBacked = $available -gt 0
    }
    $runtimeImageObserved = $true
}

$imports = [Collections.Generic.List[object]]::new()
$iatEntries = [Collections.Generic.List[object]]::new()
$importRva = Read-God2UInt32 ($optionalOffset + 104)
$importSize = Read-God2UInt32 ($optionalOffset + 108)
$importOffset = Convert-God2RvaToOffset $importRva
if ($importRva -ne 0 -and $null -ne $importOffset) {
    for ($descriptorIndex = 0; $descriptorIndex -lt 512; ++$descriptorIndex) {
        $descriptor = [int]$importOffset + ($descriptorIndex * 20)
        if ($descriptor + 20 -gt $script:God2StaticBytes.Length) { break }
        $originalThunk = Read-God2UInt32 $descriptor
        $nameRva = Read-God2UInt32 ($descriptor + 12)
        $firstThunk = Read-God2UInt32 ($descriptor + 16)
        if ($originalThunk -eq 0 -and $nameRva -eq 0 -and $firstThunk -eq 0) { break }
        $nameOffset = Convert-God2RvaToOffset $nameRva
        $module = if ($null -eq $nameOffset) { 'INVALID_IMPORT_NAME' } else { Get-God2AsciiZ $nameOffset 260 }
        if ([string]::IsNullOrWhiteSpace($module)) { $module = 'INVALID_IMPORT_NAME' }
        $symbols = [Collections.Generic.List[object]]::new()
        $thunkRva = if ($originalThunk -ne 0) { $originalThunk } else { $firstThunk }
        $thunkOffset = Convert-God2RvaToOffset $thunkRva
        if ($null -ne $thunkOffset) {
            for ($thunkIndex = 0; $thunkIndex -lt 4096; ++$thunkIndex) {
                $entryOffset = [int]$thunkOffset + ($thunkIndex * 4)
                if ($entryOffset + 4 -gt $script:God2StaticBytes.Length) { break }
                $value = Read-God2UInt32 $entryOffset
                if ($value -eq 0) { break }
                $iatRva = [uint32]([uint64]$firstThunk + [uint64]($thunkIndex * 4))
                if (($value -band 0x80000000) -ne 0) {
                    $symbol = [pscustomobject][ordered]@{ Name=$null; Ordinal=($value -band 0xFFFF); IATRVA=Format-God2Rva $iatRva }
                } else {
                    $hintNameOffset = Convert-God2RvaToOffset $value
                    $symbolName = if ($null -eq $hintNameOffset) { $null } else { Get-God2AsciiZ ([int]$hintNameOffset + 2) 512 }
                    $symbol = [pscustomobject][ordered]@{ Name=$symbolName; Ordinal=$null; IATRVA=Format-God2Rva $iatRva }
                }
                [void]$symbols.Add($symbol)
                [void]$iatEntries.Add([pscustomobject][ordered]@{ Module=$module; Name=$symbol.Name; Ordinal=$symbol.Ordinal; IATRVA=$symbol.IATRVA })
            }
        }
        [void]$imports.Add([pscustomobject][ordered]@{ Module=$module; SymbolCount=$symbols.Count; Symbols=@($symbols) })
    }
}

$directCalls = [Collections.Generic.List[object]]::new()
$callsitesByRva = @{}
$callsByTarget = @{}
$scanSections = @($script:God2StaticSections | Where-Object {
    [bool]$_.Executable -and [bool]$_.RawBacked -and [uint32]$_.RawSize -ge 5
})
$scanOffsets = [int[]]@($scanSections | ForEach-Object { [int]$_.RawOffset })
$scanRvas = [uint32[]]@($scanSections | ForEach-Object { [uint32]$_.RVA })
$scanSizes = [int[]]@($scanSections | ForEach-Object { [int]$_.RawSize })
foreach ($encodedCall in [God2BoundedImageReader]::ScanDirectCalls(
        $script:God2StaticBytes, $scanOffsets, $scanRvas, $scanSizes)) {
    $callsiteRva = [uint32]($encodedCall -shr 32)
    $targetRva = [uint32]($encodedCall -band [uint64]4294967295)
    $callsiteSection = Get-God2SectionForRva $callsiteRva
    $row = [pscustomobject][ordered]@{
        CallsiteRVA=Format-God2Rva $callsiteRva; CallsiteRvaValue=$callsiteRva
        TargetRVA=Format-God2Rva $targetRva; TargetRvaValue=$targetRva
        Section=if($null -eq $callsiteSection){$null}else{[string]$callsiteSection.Name}
        Encoding='E8 rel32'
    }
    [void]$directCalls.Add($row)
    $callsitesByRva[[string]$callsiteRva] = $row
    $targetKey = [string]$targetRva
    if (-not $callsByTarget.ContainsKey($targetKey)) { $callsByTarget[$targetKey] = [Collections.Generic.List[object]]::new() }
    [void]$callsByTarget[$targetKey].Add($row)
}

$vtableRuns = [Collections.Generic.List[object]]::new()
$vtableVirtualAddresses = @{}
foreach ($encodedRun in [God2BoundedImageReader]::ScanVtableRuns(
        $script:God2StaticBytes, $scanOffsets, $scanRvas, $scanSizes,
        [uint32]$imageBase, 256)) {
    $runRva = [uint32]($encodedRun -shr 32)
    $entryCount = [uint32]($encodedRun -band [uint64]4294967295)
    $runSection = Get-God2SectionForRva $runRva
    $runOffset = Convert-God2RvaToOffset $runRva
    $entries = [Collections.Generic.List[string]]::new()
    for ($entryIndex = 0; $entryIndex -lt $entryCount; ++$entryIndex) {
        $entryValue = Read-God2UInt32 ([int]$runOffset + ($entryIndex * 4))
        [void]$entries.Add((Format-God2Rva ([uint32]([uint64]$entryValue - [uint64]$imageBase))))
    }
    $virtualAddress = [uint32]([uint64]$imageBase + [uint64]$runRva)
    [void]$vtableRuns.Add([pscustomobject][ordered]@{
        RVA=Format-God2Rva $runRva; VirtualAddress=('0x{0:X8}' -f $virtualAddress)
        Section=if($null -eq $runSection){$null}else{[string]$runSection.Name}
        EntryCount=$entryCount; Entries=@($entries)
    })
    $vtableVirtualAddresses[[string]$virtualAddress] = $true
}

$seedDefinitions = @(
    [ordered]@{ Domain='Parser'; RVA=[uint32]0x00078D70; ExpectedCallsites=@([uint32]0x00078A48) },
    [ordered]@{ Domain='Serializer'; RVA=[uint32]0x0007FC10; ExpectedCallsites=@() },
    [ordered]@{ Domain='Handler'; RVA=[uint32]0x0007F940; ExpectedCallsites=@([uint32]0x00147096,[uint32]0x001470AE) }
)
$seedRows = [Collections.Generic.List[object]]::new()
$pool = @{}
foreach ($seed in $seedDefinitions) {
    $seedBoundary = Get-God2FunctionBoundary $seed.RVA $MaximumFunctionBytes
    $exactCallsites = [Collections.Generic.List[string]]::new()
    foreach ($expectedCallsite in $seed.ExpectedCallsites) {
        $key = [string]$expectedCallsite
        if ($callsitesByRva.ContainsKey($key) -and [uint32]$callsitesByRva[$key].TargetRvaValue -eq [uint32]$seed.RVA) {
            [void]$exactCallsites.Add((Format-God2Rva $expectedCallsite))
        }
    }
    $seedSection = Get-God2SectionForRva $seed.RVA
    $seedOffset = Convert-God2RvaToOffset $seed.RVA
    [void]$seedRows.Add([pscustomobject][ordered]@{
        Domain=$seed.Domain; RVA=Format-God2Rva $seed.RVA
        Section=if($null -eq $seedSection){$null}else{[string]$seedSection.Name}
        Bytes=if($null -eq $seedOffset){''}else{Format-God2Bytes $seedOffset 16}
        ExpectedCallsites=@($seed.ExpectedCallsites | ForEach-Object { Format-God2Rva $_ })
        ExactCallsitesVerified=@($exactCallsites)
        ExactBytesPresent=($null -ne $seedOffset)
        FunctionBoundary=$seedBoundary
    })
    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{ RVA=[uint32]$seed.RVA; Depth=0; SeedDomain=[string]$seed.Domain })
    $neighborhoodStart = [uint32]([Math]::Max(0,[int64]$seed.RVA - 512))
    $neighborhoodEnd = [uint32]([Math]::Min([uint32]::MaxValue,[int64]$seed.RVA + 512))
    for ($neighborRva = $neighborhoodStart; $neighborRva -le $neighborhoodEnd; ++$neighborRva) {
        $neighborKey = [string]$neighborRva
        if (-not $callsitesByRva.ContainsKey($neighborKey)) { continue }
        $neighborCall = $callsitesByRva[$neighborKey]
        $targetKey = [string][uint32]$neighborCall.TargetRvaValue
        if (-not $pool.ContainsKey($targetKey)) {
            $pool[$targetKey] = [pscustomobject][ordered]@{
                RVA=[uint32]$neighborCall.TargetRvaValue
                SeedDomains=[Collections.Generic.List[string]]::new()
                MinimumDepth=1
            }
        }
        if (-not $pool[$targetKey].SeedDomains.Contains([string]$seed.Domain)) {
            [void]$pool[$targetKey].SeedDomains.Add([string]$seed.Domain)
        }
        $queue.Enqueue([pscustomobject]@{
            RVA=[uint32]$neighborCall.TargetRvaValue; Depth=1; SeedDomain=[string]$seed.Domain
        })
    }
    $visited = @{}
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        $nodeKey = '{0}:{1}' -f $node.SeedDomain,$node.RVA
        if ($visited.ContainsKey($nodeKey)) { continue }
        $visited[$nodeKey] = $true
        $boundary = Get-God2FunctionBoundary ([uint32]$node.RVA) $MaximumFunctionBytes
        if ($null -eq $boundary.StartRvaValue) { continue }
        for ($rvaValue = [uint32]$boundary.StartRvaValue; $rvaValue -le [uint32]$boundary.EndRvaValue; ++$rvaValue) {
            $callKey = [string]$rvaValue
            if (-not $callsitesByRva.ContainsKey($callKey)) { continue }
            $call = $callsitesByRva[$callKey]
            $targetKey = [string]$call.TargetRvaValue
            if (-not $pool.ContainsKey($targetKey)) {
                $pool[$targetKey] = [pscustomobject][ordered]@{
                    RVA=[uint32]$call.TargetRvaValue
                    SeedDomains=[Collections.Generic.List[string]]::new()
                    MinimumDepth=[int]$node.Depth + 1
                }
            }
            $poolRow = $pool[$targetKey]
            if (-not $poolRow.SeedDomains.Contains([string]$node.SeedDomain)) { [void]$poolRow.SeedDomains.Add([string]$node.SeedDomain) }
            if ([int]$node.Depth + 1 -lt [int]$poolRow.MinimumDepth) { $poolRow.MinimumDepth = [int]$node.Depth + 1 }
            if ([int]$node.Depth -lt $MaximumGraphDepth) {
                $queue.Enqueue([pscustomobject]@{ RVA=[uint32]$call.TargetRvaValue; Depth=[int]$node.Depth + 1; SeedDomain=[string]$node.SeedDomain })
            }
        }
        $reverseKey = [string][uint32]$node.RVA
        if ($callsByTarget.ContainsKey($reverseKey)) {
            foreach ($caller in @($callsByTarget[$reverseKey] | Select-Object -First 32)) {
                $callerBoundary = Get-God2FunctionBoundary ([uint32]$caller.CallsiteRvaValue) $MaximumFunctionBytes
                if ($null -eq $callerBoundary.StartRvaValue) { continue }
                $callerRva = [uint32]$callerBoundary.StartRvaValue
                $callerKey = [string]$callerRva
                if (-not $pool.ContainsKey($callerKey)) {
                    $pool[$callerKey] = [pscustomobject][ordered]@{
                        RVA=$callerRva; SeedDomains=[Collections.Generic.List[string]]::new(); MinimumDepth=[int]$node.Depth + 1
                    }
                }
                if (-not $pool[$callerKey].SeedDomains.Contains([string]$node.SeedDomain)) {
                    [void]$pool[$callerKey].SeedDomains.Add([string]$node.SeedDomain)
                }
            }
        }
    }
}

$poolDetails = [Collections.Generic.List[object]]::new()
foreach ($poolRow in $pool.Values) {
    $boundary = Get-God2FunctionBoundary ([uint32]$poolRow.RVA) $MaximumFunctionBytes
    if ($null -eq $boundary.StartRvaValue) { continue }
    $features = Get-God2FunctionFeatures $boundary $callsitesByRva $vtableVirtualAddresses
    $targetKey = [string][uint32]$poolRow.RVA
    $fanin = if ($callsByTarget.ContainsKey($targetKey)) { $callsByTarget[$targetKey].Count } else { 0 }
    [void]$poolDetails.Add([pscustomobject][ordered]@{
        RVA=[uint32]$poolRow.RVA; Boundary=$boundary; Features=$features
        SeedDomains=@($poolRow.SeedDomains); MinimumDepth=[int]$poolRow.MinimumDepth; FanIn=$fanin
    })
}
if ($poolDetails.Count -lt 21) {
    throw "Static graph produced only $($poolDetails.Count) bounded function candidates; refusing an incomplete 21-domain map."
}

$domainDefinitions = @(
    [ordered]@{ Domain='Object'; Seed='Parser' }, [ordered]@{ Domain='Allocation'; Seed='Parser' },
    [ordered]@{ Domain='VTable'; Seed='Parser' }, [ordered]@{ Domain='Factory'; Seed='Parser' },
    [ordered]@{ Domain='ManagerLookup'; Seed='Parser' }, [ordered]@{ Domain='Registry'; Seed='Parser' },
    [ordered]@{ Domain='ResourceDecode'; Seed='Parser' }, [ordered]@{ Domain='Mutation'; Seed='Handler' },
    [ordered]@{ Domain='TaintSeed'; Seed='Parser' }, [ordered]@{ Domain='FormulaOperand'; Seed='Handler' },
    [ordered]@{ Domain='Quest'; Seed='Parser' }, [ordered]@{ Domain='Map'; Seed='Parser' },
    [ordered]@{ Domain='Portal'; Seed='Parser' }, [ordered]@{ Domain='NPC'; Seed='Parser' },
    [ordered]@{ Domain='Monster'; Seed='Handler' }, [ordered]@{ Domain='Battle'; Seed='Handler' },
    [ordered]@{ Domain='Inventory'; Seed='Serializer' }, [ordered]@{ Domain='Item'; Seed='Parser' },
    [ordered]@{ Domain='Skill'; Seed='Handler' }, [ordered]@{ Domain='Pet/Mount'; Seed='Handler' },
    [ordered]@{ Domain='Snapshot'; Seed='Parser' }
)
$candidateRows = [Collections.Generic.List[object]]::new()
$assignedPrimary = @{}
foreach ($definition in $domainDefinitions) {
    $ranked = [Collections.Generic.List[object]]::new()
    foreach ($detail in $poolDetails) {
        $seedDomain = if (@($detail.SeedDomains) -contains $definition.Seed) { $definition.Seed } else { [string](@($detail.SeedDomains)[0]) }
        $score = Get-God2DomainScore $definition.Domain $detail.Features ([int]$detail.FanIn) $seedDomain ([int]$detail.MinimumDepth)
        if ($seedDomain -cne $definition.Seed) { $score = [Math]::Max(0,$score - 12) }
        [void]$ranked.Add([pscustomobject]@{ Detail=$detail; Score=$score; SeedDomain=$seedDomain })
    }
    $selected = [Collections.Generic.List[object]]::new()
    foreach ($rank in @($ranked | Sort-Object @{Expression='Score';Descending=$true}, @{Expression={$_.Detail.RVA};Descending=$false})) {
        $rvaKey = [string][uint32]$rank.Detail.RVA
        if ($selected.Count -eq 0 -and $assignedPrimary.ContainsKey($rvaKey)) { continue }
        if (@($selected | Where-Object { [uint32]$_.Detail.RVA -eq [uint32]$rank.Detail.RVA }).Count -gt 0) { continue }
        [void]$selected.Add($rank)
        if ($selected.Count -ge $MaximumCandidatesPerDomain) { break }
    }
    if ($selected.Count -eq 0) {
        [void]$selected.Add(($ranked | Sort-Object @{Expression='Score';Descending=$true}, @{Expression={$_.Detail.RVA};Descending=$false} | Select-Object -First 1))
    }
    $candidateIndex = 0
    foreach ($rank in $selected) {
        $detail = $rank.Detail
        $rvaKey = [string][uint32]$detail.RVA
        if ($candidateIndex -eq 0) { $assignedPrimary[$rvaKey] = $definition.Domain }
        $section = Get-God2SectionForRva ([uint32]$detail.RVA)
        $offset = Convert-God2RvaToOffset ([uint32]$detail.RVA)
        $callsiteRows = if ($callsByTarget.ContainsKey($rvaKey)) { @($callsByTarget[$rvaKey]) } else { @() }
        $callsiteRvas = @($callsiteRows | ForEach-Object { $_.CallsiteRVA } | Sort-Object -Unique | Select-Object -First 64)
        $features = $detail.Features
        $prologue = Format-God2Bytes ([int]$detail.Boundary.StartOffset) ([Math]::Min(8,[int]$features.FunctionBytes))
        $epilogueOffset = [Math]::Max([int]$detail.Boundary.StartOffset,[int]$detail.Boundary.EndOffset - 7)
        $epilogue = Format-God2Bytes $epilogueOffset ([int]$detail.Boundary.EndOffset - $epilogueOffset + 1)
        $staticScore = [Math]::Round([double]$rank.Score,2)
        $candidateId = '{0}-{1}-{2:D2}' -f $definition.Domain.Replace('/','-'),(Format-God2Rva ([uint32]$detail.RVA)).Substring(2),$candidateIndex
        [void]$candidateRows.Add([pscustomobject][ordered]@{
            Domain=$definition.Domain; CandidateId=$candidateId; Module='God2_opt.exe'
            RVA=Format-God2Rva ([uint32]$detail.RVA); CallsiteRVAs=$callsiteRvas
            ExecutableSection=[string]$section.Name
            Bytes=Format-God2Bytes ([int]$offset) 16
            ByteMask='FF FF FF FF FF FF FF FF FF FF FF FF FF FF FF FF'
            FunctionBoundary=[pscustomobject][ordered]@{
                Status=[string]$detail.Boundary.Status; StartRVA=[string]$detail.Boundary.StartRVA
                EndRVA=[string]$detail.Boundary.EndRVA; Confidence=[string]$detail.Boundary.Confidence
                EndKind=[string]$detail.Boundary.EndKind
            }
            PrologueEpilogue=[pscustomobject][ordered]@{ Prologue=$prologue; Epilogue=$epilogue; RuntimeVerified=$false }
            CallingConventionCandidate=[string]$features.CallingConventionCandidate
            ArgumentCandidates=@($features.ArgumentCandidates)
            ReturnCandidate=[pscustomobject][ordered]@{ Register='EAX'; StackBytes=$features.ReturnStackBytes; LifetimeVerified=$false }
            ThreadContextCandidate='UNKNOWN_RUNTIME_OBSERVATION_REQUIRED'
            ReentrancyRisk='UNKNOWN_RUNTIME_OBSERVATION_REQUIRED'
            ReaderWriterRelation=[pscustomobject][ordered]@{
                ThisFieldReads=[int]$features.ThisFieldReadCount; ThisFieldWrites=[int]$features.ThisFieldWriteCount
                RepeatedObjectOffsets=@($features.RepeatedObjectOffsets); StackArgumentReads=[int]$features.StackArgumentReadCount
            }
            ConsumerRelation=[pscustomobject][ordered]@{
                SeedDomain=[string]$rank.SeedDomain; GraphDepth=[int]$detail.MinimumDepth
                DirectCalls=@($features.DirectCalls); FanIn=[int]$detail.FanIn
                Classification='HYPOTHESIS_ONLY_CONSUMER_SHAPE'; RuntimeVerified=$false
            }
            ControlFlowSummary=[pscustomobject][ordered]@{
                ConditionalBranches=[int]$features.ConditionalBranchCount; BackwardBranches=[int]$features.BackwardBranchCount
                DirectCalls=[int]$features.DirectCallCount; IndirectCalls=[int]$features.IndirectCallCount
            }
            DataFlowSummary=[pscustomobject][ordered]@{
                ArithmeticOperations=[int]$features.ArithmeticOperationCount
                Comparisons=[int]$features.ComparisonOperationCount
                ImmediatePushes=[int]$features.ImmediatePushCount
                VtableLikeReferences=@($features.VtableLikeReferences)
            }
            StaticScore=$staticScore; RuntimeObservationCount=0
            Contradictions=@('LinearSweepMayContainEmbeddedData','FunctionBoundaryHeuristicNotRuntimeVerified','SemanticDomainClassificationUnconfirmed')
            PromotionGateStatus='EVIDENCE_BLOCKED_RUNTIME_AND_ABI_GATES'
            PromotionGates=[pscustomobject][ordered]@{
                ExactTargetIdentity=$true; ExecutableSection=$true; ExactCandidateBytes=$true
                CallingConventionVerified=$false; TypedRuntimeEvidence=$false; RepeatedCausalObservation=$false
                ContradictionsResolved=$false; StableObjectOrContext=$false; VerifiedConsumerOrMutation=$false
                SensitiveMaskContract=$false; ArgumentContractVerified=$false; ReturnValueLifetimeVerified=$false
                ThreadContextVerified=$false; ReentrancyRiskVerified=$false
            }
            ActivationAllowed=$false; PromotionEligible=$false
        })
        ++$candidateIndex
    }
}

$generatedAt = [DateTime]::UtcNow.ToString('o')
$sessionId = 'static-{0}-{1}' -f $targetSha.Substring(0,12),[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$authority = [pscustomobject][ordered]@{
    AuthorityProfile='ExactBinaryStaticAnalysis'; SessionId=$sessionId
    SourceArtifact=if($runtimeImageObserved){'God2_opt.exe:bounded-runtime-image'}else{'God2_opt.exe:raw-file'}
    SourceSHA256=$targetSha
    TargetExecutable='God2_opt.exe'; TargetVersion=$targetVersion; TargetSHA256=$targetSha
    TargetProcessId=if($runtimeImageObserved){[uint32]$TargetProcessId}else{$null}
    TargetProcessCreationTime=$runtimeProcessCreationTime
    CurrentSessionObserved=$false; HistoricalEvidenceReused=$false; FixtureOnly=$false
    PromotionEligible=$false; StaticAnalysisOnly=$true
}
$result = [pscustomobject][ordered]@{
    SchemaVersion='god2-deep-probe-candidate-map-v3'; GeneratedAtUtc=$generatedAt
    Authority=$authority; TargetExecutable='God2_opt.exe'; TargetVersion=$targetVersion
    TargetSHA256=$targetSha; Architecture='x86'; FileSize=[uint64]$script:God2StaticBytes.Length
    ImageBase=('0x{0:X8}' -f $imageBase); EntryRVA=Format-God2Rva $entryRva; SizeOfImage=[uint64]$sizeOfImage
    DiscoveryPolicy=if($runtimeImageObserved){
        'ExactRawPEIdentity;BoundedReadOnlyRuntimeImage;LinearSweepCallGraph;HeuristicCFGAndX86DataFlow;NoMemoryWrite;NoAutomaticActivation'
    }else{
        'ReadOnlyRawPE;BoundedLinearSweepCallGraph;HeuristicCFGAndX86DataFlow;NoProcessAttach;NoWritableMemoryScan;NoAutomaticActivation'
    }
    AnalysisBounds=[pscustomobject][ordered]@{
        MaximumCandidatesPerDomain=$MaximumCandidatesPerDomain; MaximumGraphDepth=$MaximumGraphDepth
        MaximumFunctionBytes=$MaximumFunctionBytes; FileBytesRead=[uint64]$script:God2StaticBytes.Length
        ProcessOpenedReadOnly=[bool]$runtimeImageObserved; RuntimeImageObserved=[bool]$runtimeImageObserved
        MemoryWritten=$false; TargetExecutedByAnalyzer=$false; NetworkTrafficGeneratedByAnalyzer=$false
    }
    PE=[pscustomobject][ordered]@{
        Machine='0x014C'; OptionalMagic='0x010B'; SectionCount=$sections.Count; Sections=@($sections)
        ImportDirectoryRVA=Format-God2Rva $importRva; ImportDirectorySize=[uint64]$importSize
        ImportModuleCount=$imports.Count; IATEntryCount=$iatEntries.Count; Imports=@($imports)
        DirectRelativeCallCount=$directCalls.Count; VtableLikeTableCount=$vtableRuns.Count
        VtableLikeTables=@($vtableRuns)
    }
    SeedFunctions=@($seedRows); CandidateDomainCount=$domainDefinitions.Count
    CandidateCount=$candidateRows.Count; Candidates=@($candidateRows)
    ActivationAllowedCount=0; PromotionEligibleCount=0
    Status='EVIDENCE_BLOCKED_STATIC_HYPOTHESES_REQUIRE_CONTRACT_ACQUISITION'
}

$parent = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent -PathType Container)) {
    [void](New-Item -ItemType Directory -Path $parent)
}
$utf8 = [Text.UTF8Encoding]::new($false, $true)
[IO.File]::WriteAllText($OutputPath, (($result | ConvertTo-Json -Depth 20) + "`n"), $utf8)
$outputSha = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash
[pscustomobject]@{
    Passed=$true; OutputPath=(Resolve-Path -LiteralPath $OutputPath).Path; OutputSHA256=$outputSha
    TargetSHA256=$targetSha; CandidateCount=$candidateRows.Count; ActivationAllowedCount=0
    DirectRelativeCallCount=$directCalls.Count; ImportModuleCount=$imports.Count; IATEntryCount=$iatEntries.Count
}
