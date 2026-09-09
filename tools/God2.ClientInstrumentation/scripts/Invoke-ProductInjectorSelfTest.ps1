param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $RunId = "product-injector-" + (Get-Date -Format "yyyyMMdd-HHmmss"),
    [string] $BuildRoot = "",
    [string] $OutputRoot = "",
    [string] $ReleaseExecutable = "",
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
function Write-Utf8NoBomAtomic {
    param([Parameter(Mandatory=$true)][string]$Path,
          [Parameter(Mandatory=$true)][string]$Content)
    if (Test-Path -LiteralPath $Path) {
        throw "refusing to overwrite an existing evidence artifact: $Path"
    }
    $temporary = $Path + ".tmp-" + [guid]::NewGuid().ToString("N")
    $encoding = [Text.UTF8Encoding]::new($false, $true)
    $bytes = $encoding.GetBytes($Content)
    $committed = $false
    try {
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write, [IO.FileShare]::None, 4096,
            [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        Move-Item -LiteralPath $temporary -Destination $Path -ErrorAction Stop
        $committed = $true
    } finally {
        if (-not $committed) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
    }
}

function Read-StrictUtf8Text {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM is forbidden in typed evidence: $Path"
    }
    return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
}

function Read-StrictUtf8Artifact {
    param([Parameter(Mandatory=$true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw "UTF-8 BOM is forbidden in typed evidence: $Path"
    }
    $raw = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $hasher.ComputeHash($bytes)
    } finally {
        $hasher.Dispose()
    }
    return [pscustomobject][ordered]@{
        Raw = $raw
        SHA256 = ([BitConverter]::ToString($digest)).Replace('-', '')
        ByteLength = [uint64]$bytes.LongLength
    }
}

function Test-ExactJsonPropertyOccurrences {
    param([Parameter(Mandatory=$true)][string]$Raw,
          [Parameter(Mandatory=$true)]$ExpectedCounts)
    foreach ($entry in $ExpectedCounts.GetEnumerator()) {
        $pattern = '(?<!\\)"' + [regex]::Escape([string]$entry.Key) +
            '"[ \t\r\n]*:'
        if ([regex]::Matches($Raw, $pattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant).Count -ne
            [int]$entry.Value) {
            return $false
        }
    }
    return $true
}

function Test-ExactObjectProperties {
    param($Object, [Parameter(Mandatory=$true)][string[]]$Expected)
    if ($null -eq $Object) { return $false }
    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    return ($actual -join "`n") -ceq ($wanted -join "`n")
}

function ConvertTo-ExactUInt32OrNull {
    param($Value)
    if (-not ($Value -is [int] -or $Value -is [long] -or
        $Value -is [uint32] -or $Value -is [uint64])) { return $null }
    $number = [uint64]$Value
    if ($number -gt [uint32]::MaxValue) { return $null }
    return [uint32]$number
}

function Test-SensitiveOutboundIntegrationIdentity {
    param($Identity,
          [Parameter(Mandatory=$true)][int]$ExpectedSegmentCount,
          [Parameter(Mandatory=$true)][bool]$RequireOverlapped,
          [Parameter(Mandatory=$true)][bool]$RequireBridgeGeneration)
    $identityProperties = @(
        "ExpectedBridgeGeneration","ExpectedCallerReturnAddress",
        "ExpectedFirstBuffer","ExpectedFrameFingerprint","ExpectedOverlapped",
        "ExpectedProducerGuard","ExpectedSegmentCount","ExpectedSocket",
        "ExpectedThreadId","ExpectedTransactionId","Matched",
        "ObservedBridgeGeneration","ObservedCallerReturnAddress",
        "ObservedFirstBuffer","ObservedFrameFingerprint","ObservedOverlapped",
        "ObservedProducerGuard","ObservedSegmentCount","ObservedSocket",
        "ObservedThreadId","ObservedTransactionId")
    if (-not (Test-ExactObjectProperties $Identity $identityProperties) -or
        -not ($Identity.Matched -is [bool]) -or
        -not [bool]$Identity.Matched) {
        return $false
    }
    foreach ($name in $identityProperties) {
        if ($name -eq "Matched") { continue }
        if ($null -eq (ConvertTo-ExactUInt32OrNull $Identity.$name)) {
            return $false
        }
    }
    foreach ($stem in @(
        "BridgeGeneration","CallerReturnAddress","FirstBuffer",
        "FrameFingerprint","Overlapped","ProducerGuard","SegmentCount",
        "Socket","ThreadId","TransactionId")) {
        if ([uint32]$Identity.("Expected" + $stem) -ne
            [uint32]$Identity.("Observed" + $stem)) {
            return $false
        }
    }
    if ([uint32]$Identity.ExpectedThreadId -eq 0 -or
        [uint32]$Identity.ExpectedSocket -eq 0 -or
        [uint32]$Identity.ExpectedTransactionId -eq 0 -or
        [uint32]$Identity.ExpectedProducerGuard -eq 0 -or
        [uint32]$Identity.ExpectedFirstBuffer -eq 0 -or
        [uint32]$Identity.ExpectedCallerReturnAddress -eq 0 -or
        [uint32]$Identity.ExpectedSegmentCount -ne
            [uint32]$ExpectedSegmentCount -or
        [uint32]$Identity.ExpectedFrameFingerprint -ne
            ([uint32]$Identity.ExpectedProducerGuard -bxor
             [uint32]$Identity.ExpectedTransactionId)) {
        return $false
    }
    if ($RequireOverlapped -ne ([uint32]$Identity.ExpectedOverlapped -ne 0) -or
        $RequireBridgeGeneration -ne
            ([uint32]$Identity.ExpectedBridgeGeneration -ne 0)) {
        return $false
    }
    return $true
}

function ConvertFrom-StrictInjectorResultV2 {
    param([Parameter(Mandatory=$true)][string]$Raw)
    $fields = @(
        '"schemaVersion":(?<schemaVersion>2)',
        '"status":"(?<status>[A-Z0-9_]+)"',
        '"code":(?<code>[0-9]+)',
        '"injectionAttempted":(?<injectionAttempted>true|false)',
        '"moduleWasEverLoaded":(?<moduleWasEverLoaded>true|false)',
        '"moduleLoadStateVerified":(?<moduleLoadStateVerified>true|false)',
        '"moduleSnapshotVerified":(?<moduleSnapshotVerified>true|false)',
        '"moduleAbsent":(?<moduleAbsent>true|false)',
        '"targetProcessExited":(?<targetProcessExited>true|false)',
        '"targetIdentityVerified":(?<targetIdentityVerified>true|false)',
        '"probeReady":(?<probeReady>true|false)',
        '"stopSucceeded":(?<stopSucceeded>true|false)',
        '"unloadSafe":(?<unloadSafe>true|false)',
        '"moduleUnloaded":(?<moduleUnloaded>true|false)',
        '"moduleResidentInactive":(?<moduleResidentInactive>true|false)',
        '"strictUnloadVerified":(?<strictUnloadVerified>true|false)',
        '"cleanupRetriable":(?<cleanupRetriable>true|false)',
        '"injectionMode":"(?<injectionMode>[A-Za-z0-9]+)"',
        '"suspendedThreadCount":(?<suspendedThreadCount>[0-9]+)',
        '"primaryThreadId":(?<primaryThreadId>[0-9]+)',
        '"extraReferenceRequested":(?<extraReferenceRequested>true|false)',
        '"extraReferenceLoaded":(?<extraReferenceLoaded>true|false)',
        '"extraReferenceNegativeFirstFreeLibraryStillPresent":(?<extraReferenceNegativeFirstFreeLibraryStillPresent>true|false)',
        '"extraReferenceNegativeNotClaimedUnloaded":(?<extraReferenceNegativeNotClaimedUnloaded>true|false)',
        '"extraReferenceReleasedThenModuleAbsent":(?<extraReferenceReleasedThenModuleAbsent>true|false)'
    )
    $pattern = '^\{' + ($fields -join ',') + '\}\r\n$'
    $match = [regex]::Match($Raw, $pattern,
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) { return $null }
    try { $record = $Raw | ConvertFrom-Json } catch { return $null }
    $loaded = [bool]$record.moduleWasEverLoaded
    $loadState = [bool]$record.moduleLoadStateVerified
    $snapshot = [bool]$record.moduleSnapshotVerified
    $absent = [bool]$record.moduleAbsent
    $exited = [bool]$record.targetProcessExited
    $strict = [bool]$record.strictUnloadVerified
    $retriable = [bool]$record.cleanupRetriable
    $knownMode = [string]$record.injectionMode -ceq 'PausePrimaryRemoteThreadResume' -or
        [string]$record.injectionMode -ceq 'PauseResumeThenRemoteThreadComplete'
    $threadBound = [int]$record.suspendedThreadCount -eq 1 -and
        [int64]$record.primaryThreadId -gt 0
    switch ([string]$record.status) {
        'EVIDENCE_BLOCKED_BUILD_MISMATCH' {
            $coherent = [int64]$record.code -eq 193 -and
                -not [bool]$record.injectionAttempted -and -not $loaded -and
                $loadState -and $snapshot -and $absent -and -not $exited -and
                -not [bool]$record.targetIdentityVerified -and
                -not [bool]$record.probeReady -and
                -not [bool]$record.stopSucceeded -and
                -not [bool]$record.unloadSafe -and
                -not [bool]$record.moduleUnloaded -and
                -not [bool]$record.moduleResidentInactive -and $strict -and
                -not $retriable -and [string]$record.injectionMode -ceq 'None' -and
                [int]$record.suspendedThreadCount -eq 0 -and
                [int64]$record.primaryThreadId -eq 0 -and
                -not [bool]$record.extraReferenceRequested -and
                -not [bool]$record.extraReferenceLoaded -and
                -not [bool]$record.extraReferenceNegativeFirstFreeLibraryStillPresent -and
                -not [bool]$record.extraReferenceNegativeNotClaimedUnloaded -and
                -not [bool]$record.extraReferenceReleasedThenModuleAbsent
        }
        'ATTACHED' {
            $coherent = [int64]$record.code -eq 0 -and
                [bool]$record.injectionAttempted -and $loaded -and $loadState -and
                -not $snapshot -and -not $absent -and -not $exited -and
                [bool]$record.targetIdentityVerified -and
                [bool]$record.probeReady -and
                -not [bool]$record.stopSucceeded -and
                -not [bool]$record.unloadSafe -and
                -not [bool]$record.moduleUnloaded -and
                -not [bool]$record.moduleResidentInactive -and
                -not $strict -and $retriable -and $knownMode -and $threadBound -and
                [bool]$record.extraReferenceRequested -and
                [bool]$record.extraReferenceLoaded -and
                -not [bool]$record.extraReferenceNegativeFirstFreeLibraryStillPresent -and
                -not [bool]$record.extraReferenceNegativeNotClaimedUnloaded -and
                -not [bool]$record.extraReferenceReleasedThenModuleAbsent
        }
        'DETACHED' {
            $coherent = [int64]$record.code -eq 0 -and
                [bool]$record.injectionAttempted -and $loaded -and $loadState -and
                $snapshot -and $absent -and -not $exited -and
                [bool]$record.targetIdentityVerified -and
                -not [bool]$record.probeReady -and [bool]$record.stopSucceeded -and
                [bool]$record.unloadSafe -and [bool]$record.moduleUnloaded -and
                -not [bool]$record.moduleResidentInactive -and $strict -and
                -not $retriable -and $knownMode -and $threadBound -and
                [bool]$record.extraReferenceRequested -and
                [bool]$record.extraReferenceLoaded -and
                [bool]$record.extraReferenceNegativeFirstFreeLibraryStillPresent -and
                [bool]$record.extraReferenceNegativeNotClaimedUnloaded -and
                [bool]$record.extraReferenceReleasedThenModuleAbsent
        }
        default {
            $coherent = $false
        }
    }
    if (-not $coherent) { return $null }
    return $record
}

if ($null -eq ('God2StrictJson' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;

public static class God2StrictJson
{
    public static bool ValidateNoDuplicateProperties(string text)
    {
        try { new Parser(text).ParseDocument(); return true; }
        catch { return false; }
    }

    private sealed class Parser
    {
        private readonly string text;
        private int position;

        internal Parser(string value)
        {
            if (value == null) throw new FormatException();
            text = value;
        }

        internal void ParseDocument()
        {
            SkipWhite();
            ParseValue();
            SkipWhite();
            if (position != text.Length) throw new FormatException();
        }

        private void ParseValue()
        {
            SkipWhite();
            if (position >= text.Length) throw new FormatException();
            switch (text[position])
            {
                case '{': ParseObject(); return;
                case '[': ParseArray(); return;
                case '"': ParseString(); return;
                case 't': Match("true"); return;
                case 'f': Match("false"); return;
                case 'n': Match("null"); return;
                default: ParseNumber(); return;
            }
        }

        private void ParseObject()
        {
            position++;
            SkipWhite();
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (Take('}')) return;
            while (true)
            {
                string name = ParseString();
                if (!names.Add(name)) throw new FormatException();
                SkipWhite();
                Require(':');
                ParseValue();
                SkipWhite();
                if (Take('}')) return;
                Require(',');
                SkipWhite();
            }
        }

        private void ParseArray()
        {
            position++;
            SkipWhite();
            if (Take(']')) return;
            while (true)
            {
                ParseValue();
                SkipWhite();
                if (Take(']')) return;
                Require(',');
                SkipWhite();
            }
        }

        private string ParseString()
        {
            Require('"');
            var value = new StringBuilder();
            while (position < text.Length)
            {
                char current = text[position++];
                if (current == '"') return value.ToString();
                if (current < 0x20) throw new FormatException();
                if (current != '\\') { value.Append(current); continue; }
                if (position >= text.Length) throw new FormatException();
                char escaped = text[position++];
                switch (escaped)
                {
                    case '"': value.Append('"'); break;
                    case '\\': value.Append('\\'); break;
                    case '/': value.Append('/'); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u': value.Append(ParseUnicode()); break;
                    default: throw new FormatException();
                }
            }
            throw new FormatException();
        }

        private char ParseUnicode()
        {
            if (position + 4 > text.Length) throw new FormatException();
            int result = 0;
            for (int index = 0; index < 4; ++index)
            {
                char digit = text[position++];
                int value = digit >= '0' && digit <= '9' ? digit - '0' :
                    digit >= 'A' && digit <= 'F' ? digit - 'A' + 10 :
                    digit >= 'a' && digit <= 'f' ? digit - 'a' + 10 : -1;
                if (value < 0) throw new FormatException();
                result = (result << 4) | value;
            }
            return (char)result;
        }

        private void ParseNumber()
        {
            if (Take('-') && position >= text.Length) throw new FormatException();
            if (Take('0'))
            {
                if (position < text.Length && Char.IsDigit(text[position]))
                    throw new FormatException();
            }
            else
            {
                int start = position;
                while (position < text.Length && Char.IsDigit(text[position])) position++;
                if (start == position) throw new FormatException();
            }
            if (Take('.'))
            {
                int start = position;
                while (position < text.Length && Char.IsDigit(text[position])) position++;
                if (start == position) throw new FormatException();
            }
            if (position < text.Length && (text[position] == 'e' || text[position] == 'E'))
            {
                position++;
                if (position < text.Length && (text[position] == '+' || text[position] == '-'))
                    position++;
                int start = position;
                while (position < text.Length && Char.IsDigit(text[position])) position++;
                if (start == position) throw new FormatException();
            }
        }

        private void Match(string expected)
        {
            if (position + expected.Length > text.Length ||
                String.CompareOrdinal(text, position, expected, 0, expected.Length) != 0)
                throw new FormatException();
            position += expected.Length;
        }

        private bool Take(char expected)
        {
            if (position >= text.Length || text[position] != expected) return false;
            position++;
            return true;
        }

        private void Require(char expected)
        {
            if (!Take(expected)) throw new FormatException();
        }

        private void SkipWhite()
        {
            while (position < text.Length)
            {
                char value = text[position];
                if (value != ' ' && value != '\t' && value != '\r' && value != '\n') return;
                position++;
            }
        }
    }
}
'@
}

function Test-StrictJsonNoDuplicateProperties {
    param([Parameter(Mandatory=$true)][string]$Raw)
    return [God2StrictJson]::ValidateNoDuplicateProperties($Raw)
}

if ($null -eq ('God2MappedInterlocked' -as [type])) {
    $mappedInterlockedCompiler = [CodeDom.Compiler.CompilerParameters]::new()
    $mappedInterlockedCompiler.GenerateInMemory = $true
    $mappedInterlockedCompiler.CompilerOptions = '/unsafe'
    [void]$mappedInterlockedCompiler.ReferencedAssemblies.Add('System.dll')
    [void]$mappedInterlockedCompiler.ReferencedAssemblies.Add('System.Core.dll')
    Add-Type -Language CSharp -CompilerParameters $mappedInterlockedCompiler -TypeDefinition @'
using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

public static class God2MappedInterlocked
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process,
        out FileTime created, out FileTime exited,
        out FileTime kernel, out FileTime user);

    public static ulong ProcessCreationTime(IntPtr process)
    {
        FileTime created, exited, kernel, user;
        if (!GetProcessTimes(process, out created, out exited,
                out kernel, out user))
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error());
        return ((ulong)created.High << 32) | created.Low;
    }

    public static unsafe int Decrement(MemoryMappedViewAccessor accessor, long offset)
    {
        byte* pointer = null;
        accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        try
        {
            pointer += accessor.PointerOffset;
            return Interlocked.Decrement(ref *(int*)(pointer + offset));
        }
        finally
        {
            accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }
}
'@
}

function Receive-God2SharedSemanticSlot {
    param(
        [Parameter(Mandatory=$true)]$Accessor,
        [Parameter(Mandatory=$true)][int]$ReadSequence,
        [Parameter(Mandatory=$true)][int]$HeaderBytes,
        [Parameter(Mandatory=$true)][int]$SlotBytes,
        [Parameter(Mandatory=$true)][Text.UTF8Encoding]$StrictUtf8
    )
    $expectedSequence = $ReadSequence + 1
    $selectedPriority = -1
    $selectedLaneRead = 0
    $slotOffset = 0L
    for ($candidatePriority = 0; $candidatePriority -lt 4; $candidatePriority++) {
        $candidateLaneOffset = 88L + ([long]$candidatePriority * 36L)
        $candidateRead = $Accessor.ReadInt32($candidateLaneOffset + 4L)
        $candidateWrite = $Accessor.ReadInt32($candidateLaneOffset)
        if ($candidateRead -ge $candidateWrite) { continue }
        $candidateSlotOffset = [long]$HeaderBytes +
            (([long]$candidatePriority * 64L + [long]($candidateRead % 64)) * $SlotBytes)
        if ($Accessor.ReadInt32($candidateSlotOffset) -eq 2 -and
            $Accessor.ReadUInt32($candidateSlotOffset + 4L) -eq
                [uint32]$expectedSequence) {
            $selectedPriority = $candidatePriority
            $selectedLaneRead = $candidateRead
            $slotOffset = $candidateSlotOffset
            break
        }
    }
    if ($selectedPriority -lt 0) {
        return [pscustomobject]@{
            Ready = $false; Parsed = $null; Wire = ""; Sequence = 0
            Domain = -1; Priority = -1; PayloadBytes = 0
            PayloadSHA256 = ""; WireSHA256 = ""; InvalidCount = 0
        }
    }
    $state = $Accessor.ReadInt32($slotOffset)
    $payloadBytes = [int]$Accessor.ReadUInt32($slotOffset + 16)
    $priority = [int]$Accessor.ReadUInt32($slotOffset + 12)
    $domain = [int]$Accessor.ReadUInt32($slotOffset + 8)
    $sequence = $Accessor.ReadUInt32($slotOffset + 4)
    $invalid = 0
    $parsed = $null
    $wire = ""
    $payloadSHA256 = ""
    $wireSHA256 = ""
    if ($state -ne 2 -or $payloadBytes -le 0 -or $payloadBytes -ge 8192) {
        $invalid++
    } else {
        $payload = [byte[]]::new($payloadBytes)
        [void]$Accessor.ReadArray($slotOffset + 32, $payload, 0, $payloadBytes)
        try {
            $payloadHasher = [Security.Cryptography.SHA256]::Create()
            try {
                $payloadSHA256 = [BitConverter]::ToString(
                    $payloadHasher.ComputeHash($payload)).Replace('-', '')
            } finally {
                $payloadHasher.Dispose()
            }
            $withDelimiter = $StrictUtf8.GetString($payload)
            if ($withDelimiter.Length -lt 3 -or $withDelimiter[0] -ne '{' -or
                $withDelimiter[$withDelimiter.Length - 1] -ne "`n" -or
                $withDelimiter.IndexOf("`r") -ge 0 -or
                $withDelimiter.IndexOf([char]0) -ge 0 -or
                $withDelimiter.IndexOf("`n") -ne $withDelimiter.Length - 1) {
                throw "shared semantic payload was not exact single-line UTF-8 JSON plus one LF"
            }
            $wire = $withDelimiter.Substring(0, $withDelimiter.Length - 1)
            if ($wire[$wire.Length - 1] -ne '}') {
                throw "shared semantic payload had trailing whitespace before LF"
            }
            $wireHasher = [Security.Cryptography.SHA256]::Create()
            try {
                $wireSHA256 = [BitConverter]::ToString(
                    $wireHasher.ComputeHash($StrictUtf8.GetBytes($wire))).Replace('-', '')
            } finally {
                $wireHasher.Dispose()
            }
            $parsed = $wire | ConvertFrom-Json
            if ($priority -lt 0 -or $priority -ge 4) {
                $invalid++
            } elseif ($null -ne $parsed.PriorityExpected -and
                [int]$parsed.PriorityExpected -ne $priority) {
                $invalid++
            }
        } catch {
            $parsed = $null
            $wire = ""
            $invalid++
        }
    }
    if ($domain -ge 0 -and $domain -lt 25) {
        $domainLagOffset = 232L + ([long]$domain * 32L) + 24L
        if ([God2MappedInterlocked]::Decrement($Accessor, $domainLagOffset) -lt 0) {
            $invalid++
        }
    } else {
        $invalid++
    }
    $Accessor.Write($slotOffset, [int]0)
    $Accessor.Write(36L, [int]$expectedSequence)
    $Accessor.Write(56L, [int](
        $Accessor.ReadInt32(32L) - $Accessor.ReadInt32(36L)))
    $selectedLaneOffset = 88L + ([long]$selectedPriority * 36L)
    $Accessor.Write($selectedLaneOffset + 4L, [int]($selectedLaneRead + 1))
    $Accessor.Write($selectedLaneOffset + 32L, [int](
        $Accessor.ReadInt32($selectedLaneOffset) -
        $Accessor.ReadInt32($selectedLaneOffset + 4L)))
    return [pscustomobject]@{
        Ready = $true
        Parsed = $parsed
        Wire = $wire
        Sequence = $sequence
        Domain = $domain
        Priority = $priority
        PayloadBytes = $payloadBytes
        PayloadSHA256 = $payloadSHA256
        WireSHA256 = $wireSHA256
        InvalidCount = $invalid
    }
}

function Add-God2SharedSemanticConsumption {
    param(
        [Parameter(Mandatory=$true)][hashtable]$State,
        [Parameter(Mandatory=$true)]$Consumed
    )
    $State.InvalidPayloads = [int]$State.InvalidPayloads +
        [int]$Consumed.InvalidCount
    if ($null -eq $Consumed.Parsed) { return }

    $State.AcceptedPayloads.Add($Consumed.Parsed)
    $State.AcceptedWireRows.Add([pscustomobject]@{
        Parsed = $Consumed.Parsed
        Wire = $Consumed.Wire
        Sequence = $Consumed.Sequence
        Domain = $Consumed.Domain
        Priority = $Consumed.Priority
        PayloadBytes = $Consumed.PayloadBytes
        PayloadSHA256 = $Consumed.PayloadSHA256
        WireSHA256 = $Consumed.WireSHA256
    })
    $State.Sequences.Add([uint32]$Consumed.Sequence)
    if ([int]$Consumed.Priority -ge 0 -and [int]$Consumed.Priority -lt 4) {
        $priority = [int]$Consumed.Priority
        $State.PriorityCounts[$priority] =
            [int]$State.PriorityCounts[$priority] + 1
    } else {
        $State.PriorityMismatches = [int]$State.PriorityMismatches + 1
    }
    if ($null -ne $Consumed.Parsed.PriorityExpected -and
        [int]$Consumed.Parsed.PriorityExpected -ne [int]$Consumed.Priority) {
        $State.PriorityMismatches = [int]$State.PriorityMismatches + 1
    }
}

function Receive-God2SharedSemanticAvailable {
    param(
        [Parameter(Mandatory=$true)]$Accessor,
        [Parameter(Mandatory=$true)][int]$HeaderBytes,
        [Parameter(Mandatory=$true)][int]$SlotBytes,
        [Parameter(Mandatory=$true)][Text.UTF8Encoding]$StrictUtf8,
        [Parameter(Mandatory=$true)][hashtable]$State
    )
    $consumedCount = 0
    while ($true) {
        $readSequence = $Accessor.ReadInt32(36L)
        $writeSequence = $Accessor.ReadInt32(32L)
        if ($readSequence -ge $writeSequence) { break }

        $consumed = Receive-God2SharedSemanticSlot -Accessor $Accessor `
            -ReadSequence $readSequence -HeaderBytes $HeaderBytes `
            -SlotBytes $SlotBytes -StrictUtf8 $StrictUtf8
        if (-not [bool]$consumed.Ready) { break }
        Add-God2SharedSemanticConsumption -State $State -Consumed $consumed
        $consumedCount++
    }
    return [pscustomobject]@{
        Consumed = $consumedCount
        ReadSequence = $Accessor.ReadInt32(36L)
        WriteSequence = $Accessor.ReadInt32(32L)
    }
}
$toolRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
if ($BuildRoot.Length -eq 0) { $BuildRoot = Join-Path $repoRoot "Build\ClientInstrumentation" }
if ($OutputRoot.Length -eq 0) { $OutputRoot = Join-Path $repoRoot "Validation\ClientInstrumentation\LoginTrial" }
if ($ReleaseExecutable.Length -eq 0) { $ReleaseExecutable = Join-Path $repoRoot "Release\God2SemanticRecoveryEngine.exe" }
if (-not $SkipBuild) {
    $buildParameters = @{ Configuration = $Configuration }
    $buildParameters.OutRoot = $BuildRoot
    & (Join-Path $toolRoot "scripts\Build-Instrumentation.ps1") @buildParameters | Out-Host
}

$buildDir = Join-Path ([IO.Path]::GetFullPath($BuildRoot)) $Configuration
$runDir = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $RunId
function Convert-ToRunEvidencePath {
    param([Parameter(Mandatory=$true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($runDir).TrimEnd('\') + '\'
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence path is outside the current validation run: $full"
    }
    return ('Validation/ClientInstrumentation/LoginTrial/' + $RunId + '/' +
        $full.Substring($prefix.Length).Replace('\','/'))
}
$payloadDir = Join-Path $runDir "payload"
$rawDir = Join-Path $runDir "raw"
$analysisDir = Join-Path $runDir "analysis"
New-Item -ItemType Directory -Force -Path $payloadDir,$rawDir,$analysisDir | Out-Null

$game = Join-Path $payloadDir "God2_opt.exe"
$dll = Join-Path $payloadDir "God2PacketCaptureProbe.dll"
$productionDll = Join-Path $payloadDir "God2PacketCaptureProbe.production.dll"
$injector = Join-Path $payloadDir "God2PacketCaptureInjector.exe"
$productionInjector = Join-Path $payloadDir "God2PacketCaptureInjectorProduction.exe"
Copy-Item -LiteralPath (Join-Path $buildDir "God2TraceSelfTestClient.exe") -Destination $game -Force
Copy-Item -LiteralPath (Join-Path $buildDir "God2ClientTraceProbeTest.dll") -Destination $dll -Force
Copy-Item -LiteralPath (Join-Path $buildDir "God2ClientTraceProbe.dll") -Destination $productionDll -Force
$testProbeSHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$productionProbeSHA256 = (Get-FileHash -LiteralPath $productionDll -Algorithm SHA256).Hash
# The lifecycle fixture uses a separately compiled test helper. The production
# injector embedded by God2 Semantic Recovery Engine has no identity bypass.
Copy-Item -LiteralPath (Join-Path $buildDir "God2PacketCaptureInjectorTest.exe") -Destination $injector -Force
Copy-Item -LiteralPath (Join-Path $buildDir "God2PacketCaptureInjector.exe") -Destination $productionInjector -Force
$testInjectorSHA256 = (Get-FileHash -LiteralPath $injector -Algorithm SHA256).Hash
$productionInjectorSHA256 = (Get-FileHash -LiteralPath $productionInjector -Algorithm SHA256).Hash

$metadata = Join-Path $rawDir "injected-packets.jsonl"
$traceDir = Join-Path $rawDir "trace"
$nativeReportPath = Join-Path $traceDir "native-probe-selftest.json"
$deepProbeCandidateMapPath = Join-Path $traceDir "deep-probe-candidate-map.json"
$nativeSemanticWirePath = Join-Path $analysisDir "dll-native-semantic-event-v2.jsonl"
$faultDiagnosticConsumerAckPath = Join-Path $analysisDir "fault-diagnostic-consumer-ack.json"
$nativeSemanticWireVerificationPath = Join-Path $analysisDir "native-semantic-wire-verification.json"
$ultimateNativeWireReportPath = Join-Path $analysisDir "u.json"
$identityGateEvidencePath = Join-Path $analysisDir "production-identity-gate-result-v2.json"
$attachEvidencePath = Join-Path $analysisDir "attach-result-v2.json"
$detachEvidencePath = Join-Path $analysisDir "detach-result-v2.json"
$targetStdoutPath = Join-Path $analysisDir "selftest-target.stdout.txt"
$targetStderrPath = Join-Path $analysisDir "selftest-target.stderr.txt"
$blockingPostUnloadPath = Join-Path $analysisDir "blocking-post-unload-results.json"
$wrongIdentityNoHookSnapshotPath = Join-Path $analysisDir "wrong-identity-no-hook-snapshot.json"
$probeDomainWirePath = Join-Path $analysisDir "probe-domain-diagnostics-v2.jsonl"
# Ultimate bundle fixtures create a deep deterministic tree; keep this root
# short enough for the supported pre-long-path Windows baseline.
$ultimateNativeWireArtifacts = Join-Path $runDir "u"
$recoveryEngine = (Resolve-Path -LiteralPath $ReleaseExecutable).Path
$generalLog = Join-Path $runDir "enhanced-capture.log"
$ownedRemoteReportPath = Join-Path $analysisDir "owned-remote-thread-selftest.json"
$ownedRemoteResultPath = Join-Path $analysisDir "owned-remote-thread-injector-result.json"
New-Item -ItemType Directory -Force -Path $traceDir | Out-Null
$sharedMappingName = "Local\God2SemanticRecovery.ProductSelfTest." + [guid]::NewGuid().ToString("N")
$sharedEventName = "Local\God2SemanticRecovery.ProductSelfTestEvent." + [guid]::NewGuid().ToString("N")
$sharedBytes = 2106376L
$sharedHeaderBytes = 1032
$sharedSlotBytes = 8224
$sharedMmf = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew(
    $sharedMappingName, $sharedBytes, [IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite)
$sharedAccessor = $sharedMmf.CreateViewAccessor(
    0, $sharedBytes, [IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite)
$sharedAccessor.Write(0L, [uint32]0x34525347)
$sharedAccessor.Write(4L, [uint32]4)
$sharedAccessor.Write(8L, [uint32]$sharedHeaderBytes)
$sharedAccessor.Write(12L, [uint32]$sharedSlotBytes)
$sharedAccessor.Write(16L, [uint32]256)
$sharedAccessor.Write(20L, [uint32]25)
$sharedAccessor.Write(24L, [uint32]64)
$sharedAccessor.Write(28L, [uint32]4)
$sharedAccessor.Write(68L, [int]1)
$sharedEventCreated = $false
$sharedEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::AutoReset, $sharedEventName, [ref]$sharedEventCreated)
$sharedFixtureCompleteEventName = "Local\God2SemanticRecovery.ProductFixtureComplete." +
    [guid]::NewGuid().ToString("N")
$sharedFixtureCompleteCreated = $false
$sharedFixtureCompleteEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $sharedFixtureCompleteEventName, [ref]$sharedFixtureCompleteCreated)
$sharedFixtureDrainedEventName = "Local\God2SemanticRecovery.ProductFixtureDrained." +
    [guid]::NewGuid().ToString("N")
$sharedFixtureDrainedCreated = $false
$sharedFixtureDrainedEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $sharedFixtureDrainedEventName, [ref]$sharedFixtureDrainedCreated)
$runtimeFixturesReadyEventName = "Local\God2SemanticRecovery.RuntimeFixturesReady." +
    [guid]::NewGuid().ToString("N")
$runtimeFixturesReadyCreated = $false
$runtimeFixturesReadyEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $runtimeFixturesReadyEventName, [ref]$runtimeFixturesReadyCreated)
$blockingReleaseEventName = "Local\God2SemanticRecovery.BlockingRelease." +
    [guid]::NewGuid().ToString("N")
$blockingReleaseCreated = $false
$blockingReleaseEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $blockingReleaseEventName, [ref]$blockingReleaseCreated)
$blockingAllReturnedEventName = "Local\God2SemanticRecovery.BlockingAllReturned." +
    [guid]::NewGuid().ToString("N")
$blockingAllReturnedCreated = $false
$blockingAllReturnedEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $blockingAllReturnedEventName, [ref]$blockingAllReturnedCreated)
$identitySnapshotRequestEventName = "Local\God2SemanticRecovery.IdentitySnapshotRequest." +
    [guid]::NewGuid().ToString("N")
$identitySnapshotRequestCreated = $false
$identitySnapshotRequestEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $identitySnapshotRequestEventName, [ref]$identitySnapshotRequestCreated)
$identitySnapshotCompleteEventName = "Local\God2SemanticRecovery.IdentitySnapshotComplete." +
    [guid]::NewGuid().ToString("N")
$identitySnapshotCompleteCreated = $false
$identitySnapshotCompleteEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $identitySnapshotCompleteEventName, [ref]$identitySnapshotCompleteCreated)
$identitySnapshotBaselineReadyEventName = "Local\God2SemanticRecovery.IdentitySnapshotBaselineReady." +
    [guid]::NewGuid().ToString("N")
$identitySnapshotBaselineReadyCreated = $false
$identitySnapshotBaselineReadyEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset,
    $identitySnapshotBaselineReadyEventName,
    [ref]$identitySnapshotBaselineReadyCreated)
$attachConfigPath = Join-Path $payloadDir "God2ClientTraceProbe.attach.env"
@(
    "traceDir=$traceDir"
    "generalLog=$generalLog"
    "metadata=$metadata"
    "sessionId=product-injector-shared-transport-selftest"
    "sharedMemory=$sharedMappingName"
    "sharedEvent=$sharedEventName"
    "clientBuildVerified=0"
    "enableInline=auto"
    "enableInputInline=0"
    "enableVersionProbe=0"
    "networkOnly=1"
) | Set-Content -LiteralPath $attachConfigPath -Encoding ASCII

$eventName = "Local\God2PacketCapture.ProductSelfTest." + [guid]::NewGuid().ToString("N")
$createdNew = $false
$stopEvent = [Threading.EventWaitHandle]::new(
    $false, [Threading.EventResetMode]::ManualReset, $eventName, [ref] $createdNew)
$resultPath = Join-Path $runDir "injector-result.txt"
$oldCase = $env:GOD2_TRACE_SELFTEST_CASE
$oldStartDelay = $env:GOD2_TRACE_SELFTEST_START_DELAY_MS
$oldHoldDelay = $env:GOD2_TRACE_SELFTEST_HOLD_MS
$oldSharedRingSelfTest = $env:GOD2_TRACE_SHARED_RING_SELFTEST
$oldSharedFixtureCompleteEvent = $env:GOD2_TRACE_SHARED_RING_COMPLETE_EVENT
$oldSharedFixtureDrainedEvent = $env:GOD2_TRACE_SHARED_RING_DRAINED_EVENT
$oldRuntimeFixturesReadyEvent = $env:GOD2_TRACE_RUNTIME_FIXTURES_READY_FOR_STOP_EVENT
$oldBlockingReleaseEvent = $env:GOD2_TRACE_BLOCKING_RELEASE_EVENT
$oldBlockingAllReturnedEvent = $env:GOD2_TRACE_BLOCKING_ALL_RETURNED_EVENT
$oldIdentitySnapshotPath = $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_PATH
$oldIdentitySnapshotRequestEvent = $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_REQUEST_EVENT
$oldIdentitySnapshotCompleteEvent = $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_COMPLETE_EVENT
$oldIdentitySnapshotBaselineReadyEvent = $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_BASELINE_READY_EVENT
$oldIdentitySnapshotProbePath = $env:GOD2_TRACE_IDENTITY_NEGATIVE_PROBE_PATH
$gameProcess = $null
$injectorProcess = $null
$failures = [Collections.Generic.List[string]]::new()
$sensitiveOutboundPrivacyRecord = $null
$sensitiveOutboundPrivacyPassed = $false
$nativeReportDuplicateNegativeCount = 0

try {
    $ownedRemoteArguments = @(
        "--self-test", "--dll", ('"' + $dll + '"'),
        "--result", ('"' + $ownedRemoteResultPath + '"'),
        "--owned-remote-timeout-self-test",
        "--owned-remote-report", ('"' + $ownedRemoteReportPath + '"')
    )
    $ownedRemoteProcess = Start-Process -FilePath $injector `
        -ArgumentList $ownedRemoteArguments -WorkingDirectory $payloadDir `
        -WindowStyle Hidden -PassThru
    if (-not $ownedRemoteProcess.WaitForExit(35000)) {
        $failures.Add("owned remote-thread timeout fixture did not finish within 35 seconds")
        Stop-Process -Id $ownedRemoteProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $ownedRemoteReport = if (Test-Path -LiteralPath $ownedRemoteReportPath) {
        Get-Content -LiteralPath $ownedRemoteReportPath -Raw | ConvertFrom-Json
    } else { $null }
    if ($null -eq $ownedRemoteReport -or
        [string]$ownedRemoteReport.SchemaId -cne "God2OwnedRemoteThreadSelfTest" -or
        [int]$ownedRemoteReport.SchemaVersion -ne 1 -or
        [int64]$ownedRemoteReport.RequestedDelayMs -le 20000 -or
        [int64]$ownedRemoteReport.ElapsedMs -lt [int64]$ownedRemoteReport.RequestedDelayMs -or
        -not [bool]$ownedRemoteReport.InitialTimeoutObserved -or
        -not [bool]$ownedRemoteReport.ThreadSignaled -or
        -not [bool]$ownedRemoteReport.HandleRetainedUntilSignal -or
        -not [bool]$ownedRemoteReport.DelayedLoadCompleted -or
        -not [bool]$ownedRemoteReport.ModuleReferenceReleased) {
        $failures.Add("owned remote-thread delayed-load lifecycle was not proven by measured native evidence")
    }
    $env:GOD2_TRACE_SELFTEST_CASE = "send"
    $env:GOD2_TRACE_SELFTEST_START_DELAY_MS = "3000"
    $env:GOD2_TRACE_SELFTEST_HOLD_MS = "20000"
    $env:GOD2_TRACE_SHARED_RING_SELFTEST = "1"
    $env:GOD2_TRACE_SHARED_RING_COMPLETE_EVENT = $sharedFixtureCompleteEventName
    $env:GOD2_TRACE_SHARED_RING_DRAINED_EVENT = $sharedFixtureDrainedEventName
    $env:GOD2_TRACE_RUNTIME_FIXTURES_READY_FOR_STOP_EVENT = $runtimeFixturesReadyEventName
    $env:GOD2_TRACE_BLOCKING_RELEASE_EVENT = $blockingReleaseEventName
    $env:GOD2_TRACE_BLOCKING_ALL_RETURNED_EVENT = $blockingAllReturnedEventName
    $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_PATH = $wrongIdentityNoHookSnapshotPath
    $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_REQUEST_EVENT = $identitySnapshotRequestEventName
    $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_COMPLETE_EVENT = $identitySnapshotCompleteEventName
    $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_BASELINE_READY_EVENT = $identitySnapshotBaselineReadyEventName
    $env:GOD2_TRACE_IDENTITY_NEGATIVE_PROBE_PATH = $productionDll
    $gameProcess = Start-Process -FilePath $game -WorkingDirectory $payloadDir `
        -WindowStyle Hidden -RedirectStandardOutput $targetStdoutPath `
        -RedirectStandardError $targetStderrPath -PassThru
    if (-not $identitySnapshotBaselineReadyEvent.WaitOne(10000)) {
        $failures.Add("wrong-identity target did not publish its pre-injector memory baseline")
    }
    $identityGateResultPath = Join-Path $runDir "production-identity-gate-result.txt"
    $identityGateArguments = @(
        "--monitor", "--self-test-target", "--pid", $gameProcess.Id,
        "--expected-exe", ('"' + $game + '"'),
        "--dll", ('"' + $productionDll + '"'),
        "--stop-event", ('"' + $eventName + '"'),
        "--result", ('"' + $identityGateResultPath + '"')
    )
    $identityGateProcess = Start-Process -FilePath $productionInjector -ArgumentList $identityGateArguments `
        -WorkingDirectory $payloadDir -WindowStyle Hidden -PassThru
    if (-not $identityGateProcess.WaitForExit(15000)) {
        $failures.Add("production injector identity gate did not return within 15 seconds")
        Stop-Process -Id $identityGateProcess.Id -Force -ErrorAction SilentlyContinue
    }
    $identityGateResult = if (Test-Path -LiteralPath $identityGateResultPath) {
        try { Read-StrictUtf8Text -Path $identityGateResultPath } catch { "" }
    } else { "" }
    $identityGateRecord = if ($identityGateResult.Length -gt 0) {
        ConvertFrom-StrictInjectorResultV2 -Raw $identityGateResult
    } else { $null }
    if ($null -ne $identityGateRecord) {
        Write-Utf8NoBomAtomic -Path $identityGateEvidencePath -Content $identityGateResult
    }
    $identityGateResultSHA256 = if (Test-Path -LiteralPath $identityGateEvidencePath) {
        (Get-FileHash -LiteralPath $identityGateEvidencePath -Algorithm SHA256).Hash
    } else { "" }
    if ($null -eq $identityGateRecord -or
        [int]$identityGateRecord.schemaVersion -ne 2 -or
        [string]$identityGateRecord.status -cne "EVIDENCE_BLOCKED_BUILD_MISMATCH" -or
        [bool]$identityGateRecord.injectionAttempted -or
        [bool]$identityGateRecord.moduleWasEverLoaded -or
        -not [bool]$identityGateRecord.moduleLoadStateVerified -or
        -not [bool]$identityGateRecord.moduleSnapshotVerified -or
        -not [bool]$identityGateRecord.moduleAbsent -or
        -not [bool]$identityGateRecord.strictUnloadVerified) {
        $failures.Add("production injector accepted a wrong-hash/version x86 target: $identityGateResult")
    }
    $wrongIdentityNoHookSnapshot = $null
    $wrongIdentityNoHookSnapshotSHA256 = ""
    $wrongIdentitySnapshotDuplicateNegativeCount = 0
    if (-not $identitySnapshotRequestEvent.Set() -or
        -not $identitySnapshotCompleteEvent.WaitOne(20000)) {
        $failures.Add("wrong-identity target memory snapshot did not complete")
    } elseif (-not (Test-Path -LiteralPath $wrongIdentityNoHookSnapshotPath)) {
        $failures.Add("wrong-identity no-hook snapshot artifact was not created")
    } else {
        try {
            $wrongIdentityNoHookSnapshotRaw = Read-StrictUtf8Text `
                -Path $wrongIdentityNoHookSnapshotPath
            if ($wrongIdentityNoHookSnapshotRaw.IndexOf("`r") -ge 0 -or
                $wrongIdentityNoHookSnapshotRaw.IndexOf([char]0) -ge 0 -or
                $wrongIdentityNoHookSnapshotRaw.IndexOf("`n") -ne
                    $wrongIdentityNoHookSnapshotRaw.Length - 1) {
                throw "snapshot was not exact UTF-8 JSON plus one LF"
            }
            $snapshotPropertyCounts = @{
                SchemaId=1; SchemaVersion=1; ProcessId=1
                ContinuousSampleCount=1; TransientMutationObserved=1
                ProbeModuleAbsentBefore=1; ProbeModuleAbsentAfter=1
                ProbePathAbsentBefore=1; ProbePathAbsentAfter=1
                HotpatchRows=1; IatRows=1; AllUnchanged=1
                Api=7; Before=7; After=7; Canonical=7; Unchanged=7
                Owner=3; SlotRva=3; Expected=3
            }
            if (-not (Test-ExactJsonPropertyOccurrences `
                    -Raw $wrongIdentityNoHookSnapshotRaw `
                    -ExpectedCounts $snapshotPropertyCounts)) {
                throw "snapshot contained missing or duplicate JSON properties"
            }
            $snapshotDuplicateNegatives = @(
                $wrongIdentityNoHookSnapshotRaw.Replace(
                    '"AllUnchanged":true',
                    '"AllUnchanged":false,"AllUnchanged":true'),
                $wrongIdentityNoHookSnapshotRaw.Replace(
                    '"Canonical":true',
                    '"Canonical":false,"Canonical":true')
            )
            foreach ($negative in $snapshotDuplicateNegatives) {
                if (-not (Test-ExactJsonPropertyOccurrences -Raw $negative `
                        -ExpectedCounts $snapshotPropertyCounts)) {
                    $wrongIdentitySnapshotDuplicateNegativeCount++
                }
            }
            if ($wrongIdentitySnapshotDuplicateNegativeCount -ne 2) {
                throw "snapshot duplicate-key negatives were accepted"
            }
            $wrongIdentityNoHookSnapshot =
                $wrongIdentityNoHookSnapshotRaw | ConvertFrom-Json
            $wrongIdentityNoHookSnapshotSHA256 = (Get-FileHash -LiteralPath `
                $wrongIdentityNoHookSnapshotPath -Algorithm SHA256).Hash
            $snapshotRootNames = @(
                $wrongIdentityNoHookSnapshot.PSObject.Properties.Name |
                    Sort-Object)
            $expectedSnapshotRootNames = @(
                "AllUnchanged","ContinuousSampleCount","HotpatchRows","IatRows",
                "ProbePathAbsentAfter","ProbePathAbsentBefore",
                "ProbeModuleAbsentAfter","ProbeModuleAbsentBefore","ProcessId",
                "SchemaId","SchemaVersion","TransientMutationObserved"
            ) | Sort-Object
            $hotpatchRows = @($wrongIdentityNoHookSnapshot.HotpatchRows)
            $iatRows = @($wrongIdentityNoHookSnapshot.IatRows)
            $hotpatchApis = @("send","WSASend","recv","WSARecv")
            $iatApis = @("WSAGetOverlappedResult","GetQueuedCompletionStatus",
                "GetQueuedCompletionStatusEx")
            $iatOwners = @("WS2_32.dll","KERNEL32.dll","KERNEL32.dll")
            $hotpatchRowNames = @("After","Api","Before","Canonical","Unchanged") |
                Sort-Object
            $iatRowNames = @("After","Api","Before","Canonical","Expected","Owner","SlotRva","Unchanged") |
                Sort-Object
            $snapshotValid = ($snapshotRootNames -join "`n") -ceq
                    ($expectedSnapshotRootNames -join "`n") -and
                [string]$wrongIdentityNoHookSnapshot.SchemaId -ceq
                    "God2WrongIdentityNoHookSnapshot" -and
                $wrongIdentityNoHookSnapshot.SchemaVersion -is [int] -and
                [int]$wrongIdentityNoHookSnapshot.SchemaVersion -eq 1 -and
                $wrongIdentityNoHookSnapshot.ProcessId -is [int] -and
                [int]$wrongIdentityNoHookSnapshot.ProcessId -eq $gameProcess.Id -and
                $wrongIdentityNoHookSnapshot.ProbeModuleAbsentBefore -is [bool] -and
                [bool]$wrongIdentityNoHookSnapshot.ProbeModuleAbsentBefore -and
                $wrongIdentityNoHookSnapshot.ProbeModuleAbsentAfter -is [bool] -and
                [bool]$wrongIdentityNoHookSnapshot.ProbeModuleAbsentAfter -and
                $wrongIdentityNoHookSnapshot.ProbePathAbsentBefore -is [bool] -and
                [bool]$wrongIdentityNoHookSnapshot.ProbePathAbsentBefore -and
                $wrongIdentityNoHookSnapshot.ProbePathAbsentAfter -is [bool] -and
                [bool]$wrongIdentityNoHookSnapshot.ProbePathAbsentAfter -and
                $wrongIdentityNoHookSnapshot.AllUnchanged -is [bool] -and
                [bool]$wrongIdentityNoHookSnapshot.AllUnchanged -and
                $wrongIdentityNoHookSnapshot.ContinuousSampleCount -is [int] -and
                [int]$wrongIdentityNoHookSnapshot.ContinuousSampleCount -gt 0 -and
                $wrongIdentityNoHookSnapshot.TransientMutationObserved -is [bool] -and
                -not [bool]$wrongIdentityNoHookSnapshot.TransientMutationObserved -and
                $hotpatchRows.Count -eq 4 -and $iatRows.Count -eq 3
            for ($index = 0; $index -lt $hotpatchRows.Count; $index++) {
                $row = $hotpatchRows[$index]
                $snapshotValid = $snapshotValid -and
                    (@($row.PSObject.Properties.Name | Sort-Object) -join "`n") -ceq
                        ($hotpatchRowNames -join "`n") -and
                    [string]$row.Api -ceq $hotpatchApis[$index] -and
                    [string]$row.Before -ceq "CCCCCCCCCC8BFF" -and
                    [string]$row.After -ceq [string]$row.Before -and
                    $row.Canonical -is [bool] -and [bool]$row.Canonical -and
                    $row.Unchanged -is [bool] -and [bool]$row.Unchanged
            }
            for ($index = 0; $index -lt $iatRows.Count; $index++) {
                $row = $iatRows[$index]
                $snapshotValid = $snapshotValid -and
                    (@($row.PSObject.Properties.Name | Sort-Object) -join "`n") -ceq
                        ($iatRowNames -join "`n") -and
                    [string]$row.Api -ceq $iatApis[$index] -and
                    [string]$row.Owner -ceq $iatOwners[$index] -and
                    $row.SlotRva -is [int] -and [int]$row.SlotRva -gt 0 -and
                    [string]$row.Before -cmatch '^0x[0-9A-F]{8}$' -and
                    [string]$row.Expected -ceq [string]$row.Before -and
                    [string]$row.After -ceq [string]$row.Before -and
                    $row.Canonical -is [bool] -and [bool]$row.Canonical -and
                    $row.Unchanged -is [bool] -and [bool]$row.Unchanged
            }
            if (-not $snapshotValid) {
                $failures.Add("wrong-identity target hotpatch/IAT/module snapshot was not exact and unchanged")
            }
        } catch {
            $failures.Add("wrong-identity no-hook snapshot was invalid: $($_.Exception.Message)")
        }
    }
    $strictInjectorResultNegativeCount = 0
    if ($null -ne $identityGateRecord) {
        $strictNegativeWires = @(
            $identityGateResult.Replace('"injectionAttempted":false', '"injectionAttempted":"false"'),
            $identityGateResult.Replace('"schemaVersion":2', '"schemaVersion":"2"'),
            $identityGateResult.Replace('{"schemaVersion":2,', '{"schemaVersion":2,"Unknown":false,'),
            $identityGateResult.Replace('{"schemaVersion":2,', '{"schemaVersion":2,"schemaVersion":2,'),
            $identityGateResult.Replace('"status":"EVIDENCE_BLOCKED_BUILD_MISMATCH"', '"status":"ATTACHED"'),
            $identityGateResult.Replace('"targetIdentityVerified":false', '"targetIdentityVerified":true'),
            $identityGateResult.Replace('"stopSucceeded":false', '"stopSucceeded":true'),
            $identityGateResult.Replace('"moduleUnloaded":false', '"moduleUnloaded":true'),
            $identityGateResult.Replace('"extraReferenceRequested":false', '"extraReferenceRequested":true'),
            $identityGateResult.Replace('"injectionMode":"None"', '"injectionMode":"PausePrimaryRemoteThreadResume"')
        )
        foreach ($wire in $strictNegativeWires) {
            if ($null -eq (ConvertFrom-StrictInjectorResultV2 -Raw $wire)) {
                $strictInjectorResultNegativeCount++
            }
        }
        if ($strictInjectorResultNegativeCount -ne 10) {
            $failures.Add("strict injector-result parser accepted a quoted/unknown/duplicate/contradictory negative")
        }
    }
    # The production helper above must reject this deliberately wrong-hash
    # executable without loading the DLL.  The positive lifecycle that follows
    # uses the separately compiled self-test helper and an explicit verified
    # fixture identity; it is not authority for the official client hash.
    $testClientSHA256 = (Get-FileHash -LiteralPath $game -Algorithm SHA256).Hash
    $testClientCreationTime = [God2MappedInterlocked]::ProcessCreationTime(
        $gameProcess.Handle)
    @(
        "traceDir=$traceDir"
        "generalLog=$generalLog"
        "metadata=$metadata"
        "sessionId=product-injector-shared-transport-selftest"
        "sharedMemory=$sharedMappingName"
        "sharedEvent=$sharedEventName"
        "clientBuildVerified=1"
        "clientVersion=God2TraceSelfTestClient/1"
        "clientSha256=$testClientSHA256"
        "clientProcessId=$($gameProcess.Id)"
        "clientProcessCreationTime=$testClientCreationTime"
        "enableInline=auto"
        "enableInputInline=0"
        "enableVersionProbe=0"
        "networkOnly=1"
    ) | Set-Content -LiteralPath $attachConfigPath -Encoding ASCII
    $arguments = @(
        "--monitor", "--self-test-target", "--self-test-extra-reference",
        "--pid", $gameProcess.Id,
        "--expected-exe", ('"' + $game + '"'),
        "--dll", ('"' + $dll + '"'),
        "--stop-event", ('"' + $eventName + '"'),
        "--result", ('"' + $resultPath + '"')
    )
    $injectorProcess = Start-Process -FilePath $injector -ArgumentList $arguments -WorkingDirectory $payloadDir -WindowStyle Hidden -PassThru

    $attachDeadline = (Get-Date).AddSeconds(45)
    $attachResult = ""
    $attachRecord = $null
    do {
        if (Test-Path -LiteralPath $resultPath) {
            $attachResult = try { Read-StrictUtf8Text -Path $resultPath } catch { "" }
            if ($attachResult.Length -gt 0) {
                $attachRecord = ConvertFrom-StrictInjectorResultV2 -Raw $attachResult
                if ($null -ne $attachRecord) { break }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $attachDeadline -and -not $injectorProcess.HasExited)
    if ($null -eq $attachRecord -or
        [int]$attachRecord.schemaVersion -ne 2 -or
        [string]$attachRecord.status -cne "ATTACHED" -or
        -not [bool]$attachRecord.injectionAttempted -or
        -not [bool]$attachRecord.moduleWasEverLoaded -or
        -not [bool]$attachRecord.moduleLoadStateVerified -or
        -not [bool]$attachRecord.probeReady) {
        $failures.Add("injector did not report ATTACHED: $attachResult")
    }
    if ($null -ne $attachRecord) {
        Write-Utf8NoBomAtomic -Path $attachEvidencePath -Content $attachResult
    }
    $attachResultSHA256 = if (Test-Path -LiteralPath $attachEvidencePath) {
        (Get-FileHash -LiteralPath $attachEvidencePath -Algorithm SHA256).Hash
    } else { "" }
    if ($null -eq $attachRecord -or
        -not ([string]$attachRecord.injectionMode).StartsWith("Pause")) {
        $failures.Add("attach result did not prove pause/inject/resume mode")
    }

    # GSR4 P0/P1 producers apply real backpressure.  Keep the x64 consumer live
    # while the x86 fixture publishes instead of waiting for fixture completion.
    $sharedAcceptedPayloads = [Collections.Generic.List[object]]::new()
    $sharedAcceptedWireRows = [Collections.Generic.List[object]]::new()
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $sharedSequences = [Collections.Generic.List[uint32]]::new()
    $sharedConsumerState = @{
        AcceptedPayloads = $sharedAcceptedPayloads
        AcceptedWireRows = $sharedAcceptedWireRows
        Sequences = $sharedSequences
        InvalidPayloads = 0
        PriorityCounts = @(0, 0, 0, 0)
        PriorityMismatches = 0
    }
    $sharedFixtureDeadline = (Get-Date).AddSeconds(30)
    $sharedFixtureSignaled = $false
    $sharedFixtureDrained = $false
    while ((Get-Date) -lt $sharedFixtureDeadline -and -not $gameProcess.HasExited) {
        $drain = Receive-God2SharedSemanticAvailable -Accessor $sharedAccessor `
            -HeaderBytes $sharedHeaderBytes -SlotBytes $sharedSlotBytes `
            -StrictUtf8 $strictUtf8 -State $sharedConsumerState
        if (-not $sharedFixtureSignaled -and $sharedFixtureCompleteEvent.WaitOne(0)) {
            $sharedFixtureSignaled = $true
        }
        if ($sharedFixtureSignaled -and
            [int]$drain.ReadSequence -eq [int]$drain.WriteSequence) {
            $sharedFixtureDrained = $true
            break
        }
        [void]$sharedEvent.WaitOne(10)
    }
    if (-not $sharedFixtureSignaled) {
        $failures.Add("shared-ring native export did not signal its out-of-ring completion event")
    }
    if (-not $sharedFixtureDrained) {
        $failures.Add("shared-ring native export did not drain while the GSR4 producer was active")
    }
    $sharedProducerReady = $sharedAccessor.ReadInt32(60L)
    $sharedAttempted = $sharedAccessor.ReadInt32(40L)
    $sharedWrite = $sharedAccessor.ReadInt32(32L)
    $sharedDropped = $sharedAccessor.ReadInt32(44L)
    # Native contract evidence is written by the DLL to a dedicated versioned
    # JSON report at Stop. Offset 60 remains the ring lifecycle field and is
    # never repurposed as packed self-test flags.
    $nativeSemanticTypeCount = 0
    $nativeBatchMaximumItems = 0
    $nativeBatchAccepted = 0
    $nativeBatchCount = 0
    $nativeBatchEventSignalDelta = 0
    $nativeBatchLockAcquisitionDelta = 0
    $nativeBatchContractPassed = $false
    $sharedInvalidPayloads = [int]$sharedConsumerState.InvalidPayloads
    $sharedPriorityCounts = $sharedConsumerState.PriorityCounts
    $sharedPriorityMismatches = [int]$sharedConsumerState.PriorityMismatches
    $sharedSequenceOrdered = $true
    for ($index = 1; $index -lt $sharedSequences.Count; $index++) {
        if ($sharedSequences[$index] -ne $sharedSequences[$index - 1] + 1) {
            $sharedSequenceOrdered = $false
            break
        }
    }
    $domainDroppedTotal = 0
    $domainWriteFailureTotal = 0
    $domainWithDropAccounting = 0
    $domainPendingTotal = 0
    $domainsWithAcceptedHighWater = 0
    for ($domain = 0; $domain -lt 25; $domain++) {
        $domainOffset = 232L + ([long]$domain * 32L)
        $domainDropped = $sharedAccessor.ReadInt32($domainOffset + 4)
        $domainDroppedTotal += $domainDropped
        $domainWriteFailureTotal += $sharedAccessor.ReadInt32($domainOffset + 28)
        $domainPendingTotal += $sharedAccessor.ReadInt32($domainOffset + 24)
        if ($sharedAccessor.ReadInt32($domainOffset) -gt 0 -and
            $sharedAccessor.ReadInt32($domainOffset + 20) -gt 0) {
            $domainsWithAcceptedHighWater++
        }
        if ($domainDropped -gt 0 -and
            $sharedAccessor.ReadInt32($domainOffset + 8) -gt 0 -and
            $sharedAccessor.ReadInt32($domainOffset + 12) -ge
                $sharedAccessor.ReadInt32($domainOffset + 8) -and
            $sharedAccessor.ReadInt32($domainOffset + 16) -ne 0) {
            $domainWithDropAccounting++
        }
    }
    if ($sharedProducerReady -ne 1) { $failures.Add("x86 shared-ring producer did not become ready") }
    if ($sharedAttempted -lt 257) { $failures.Add("shared-ring producer attempted only $sharedAttempted events") }
    if ($sharedWrite -le 0 -or $sharedWrite -gt $sharedAttempted) { $failures.Add("shared-ring accepted count is outside bounds: $sharedWrite") }
    if ($domainDroppedTotal -ne $sharedDropped -or
        $sharedAttempted -ne $sharedWrite + $sharedDropped) {
        $failures.Add("shared-ring global/domain drop accounting mismatch: global=$sharedDropped domains=$domainDroppedTotal")
    }
    if ($sharedDropped -gt 0 -and $domainWithDropAccounting -eq 0) {
        $failures.Add("shared-ring per-domain first/last/reason accounting was absent")
    }
    if ($domainWriteFailureTotal -ne 2) {
        $failures.Add("shared-ring per-domain write failure accounting mismatch: $domainWriteFailureTotal")
    }
    if ($domainPendingTotal -ne 0) {
        $failures.Add("shared-ring per-domain pending depth did not drain to zero: $domainPendingTotal")
    }
    if ($domainsWithAcceptedHighWater -eq 0) {
        $failures.Add("shared-ring per-domain high-water accounting was absent")
    }
    if ($sharedPriorityMismatches -ne 0 -or $sharedPriorityCounts[0] -eq 0 -or
        $sharedPriorityCounts[1] -eq 0 -or $sharedPriorityCounts[2] -eq 0 -or
        $sharedPriorityCounts[3] -eq 0) {
        $failures.Add("shared-ring wire priority preservation failed: counts=$($sharedPriorityCounts -join ',') mismatches=$sharedPriorityMismatches")
    }
    if (-not $sharedSequenceOrdered -or $sharedInvalidPayloads -ne 0 -or
        $sharedAcceptedPayloads.Count -ne $sharedWrite -or
        $sharedAccessor.ReadInt32(36L) -ne $sharedWrite) {
        $failures.Add("shared-ring x64 consumer ordering/payload validation failed")
    }
    $nativeWireRows = @($sharedAcceptedWireRows | Where-Object {
        $_.Parsed.SourceToken -eq "NativeSemanticTypeFixture" -and
        $_.Parsed.Payload.FixtureOnly -eq $true -and
        $_.Parsed.Payload.RuntimeObservation -eq $false
    })
    $nativeTypeFixtures = @($nativeWireRows | ForEach-Object { $_.Parsed })
    $nativeTypeNames = @($nativeTypeFixtures | ForEach-Object { [string]$_.EventType } |
        Sort-Object -Unique)
    $nativeTypeDigestInput = ($nativeTypeNames -join "`n")
    $nativeTypeDigestBytes = [Text.Encoding]::UTF8.GetBytes($nativeTypeDigestInput)
    $nativeTypeHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $nativeTypeDigest = [BitConverter]::ToString(
            $nativeTypeHasher.ComputeHash($nativeTypeDigestBytes)).Replace('-', '')
    } finally {
        $nativeTypeHasher.Dispose()
    }
    if ($nativeTypeFixtures.Count -ne 25 -or $nativeTypeNames.Count -ne 25) {
        $failures.Add("native semantic type producer matrix incomplete: fixtures=$($nativeTypeFixtures.Count) distinct=$($nativeTypeNames.Count)")
    } else {
        $nativeWireText = (($nativeWireRows | ForEach-Object { [string]$_.Wire }) -join "`n") + "`n"
        Write-Utf8NoBomAtomic -Path $nativeSemanticWirePath -Content $nativeWireText
    }
    $faultDiagnosticConsumerRows = @($sharedAcceptedWireRows | Where-Object {
        $_.Parsed.SourceToken -ceq "PerDomainFaultIsolation" -and
        $_.Parsed.Payload.FixtureOnly -is [bool] -and
        [bool]$_.Parsed.Payload.FixtureOnly -and
        $_.Parsed.Payload.RuntimeObservation -is [bool] -and
        -not [bool]$_.Parsed.Payload.RuntimeObservation -and
        $_.Parsed.Payload.ReservePressure -is [bool] -and
        [bool]$_.Parsed.Payload.ReservePressure
    })
    $faultDiagnosticRow = if ($faultDiagnosticConsumerRows.Count -eq 1) {
        $faultDiagnosticConsumerRows[0]
    } else { $null }
    $faultProducerPid = [uint32]$sharedAccessor.ReadUInt32(80L)
    $faultConsumerPid = [uint32]$PID
    $faultConsumerIs64Bit = [Environment]::Is64BitProcess -and
        [IntPtr]::Size -eq 8
    $faultWireProcessId = if ($null -ne $faultDiagnosticRow) {
        ConvertTo-ExactUInt32OrNull $faultDiagnosticRow.Parsed.ProcessId
    } else { $null }
    $faultSemanticSequence = if ($null -ne $faultDiagnosticRow) {
        ConvertTo-ExactUInt32OrNull $faultDiagnosticRow.Parsed.Sequence
    } else { $null }
    $faultSchemaVersion = if ($null -ne $faultDiagnosticRow) {
        ConvertTo-ExactUInt32OrNull $faultDiagnosticRow.Parsed.SchemaVersion
    } else { $null }
    $faultExpectedEventId = if ($null -ne $faultSemanticSequence) {
        "GE-$faultProducerPid-$([uint32]$faultSemanticSequence)-24"
    } else { "" }
    $faultDiagnosticConsumerPassed = $null -ne $faultDiagnosticRow -and
        $faultProducerPid -ne 0 -and $faultProducerPid -eq [uint32]$gameProcess.Id -and
        $faultConsumerPid -ne $faultProducerPid -and
        $faultConsumerIs64Bit -and
        $null -ne $faultWireProcessId -and
        [uint32]$faultWireProcessId -eq $faultProducerPid -and
        $null -ne $faultSemanticSequence -and
        [uint32]$faultSemanticSequence -gt 0 -and
        [uint32]$faultDiagnosticRow.Sequence -gt 0 -and
        $null -ne $faultSchemaVersion -and [uint32]$faultSchemaVersion -eq 2 -and
        [string]$faultDiagnosticRow.Parsed.SchemaId -ceq "God2SemanticEvent" -and
        [string]$faultDiagnosticRow.Parsed.EventType -ceq "ProbeDiagnostic" -and
        [string]$faultDiagnosticRow.Parsed.EventId -ceq $faultExpectedEventId -and
        [string]$faultDiagnosticRow.Parsed.AuthorityHint -ceq "UNKNOWN" -and
        [string]$faultDiagnosticRow.Parsed.SensitiveMaskStatus -ceq "NotSensitive" -and
        [int]$faultDiagnosticRow.Domain -eq 0 -and
        [int]$faultDiagnosticRow.Priority -eq 0 -and
        [int]$faultDiagnosticRow.PayloadBytes -gt 0 -and
        [string]$faultDiagnosticRow.PayloadSHA256 -cmatch '^[0-9A-F]{64}$' -and
        [string]$faultDiagnosticRow.WireSHA256 -cmatch '^[0-9A-F]{64}$' -and
        [string]$faultDiagnosticRow.PayloadSHA256 -cne
            [string]$faultDiagnosticRow.WireSHA256
    $faultDiagnosticConsumerAck = [ordered]@{
        schemaId = "God2FaultDiagnosticConsumerAck"
        schemaVersion = 1
        passed = [bool]$faultDiagnosticConsumerPassed
        producerProcessId = [uint32]$faultProducerPid
        consumerProcessId = [uint32]$faultConsumerPid
        consumerPointerSize = [int][IntPtr]::Size
        consumerIs64Bit = [bool][Environment]::Is64BitProcess
        crossProcess = [bool]($faultProducerPid -ne 0 -and
            $faultConsumerPid -ne $faultProducerPid)
        slotSequence = if ($null -ne $faultDiagnosticRow) {
            [uint32]$faultDiagnosticRow.Sequence
        } else { [uint32]0 }
        semanticSequence = if ($null -ne $faultSemanticSequence) {
            [uint32]$faultSemanticSequence
        } else { [uint32]0 }
        eventId = if ($null -ne $faultDiagnosticRow) {
            [string]$faultDiagnosticRow.Parsed.EventId
        } else { "" }
        domain = if ($null -ne $faultDiagnosticRow) {
            [int]$faultDiagnosticRow.Domain
        } else { -1 }
        priority = if ($null -ne $faultDiagnosticRow) {
            [int]$faultDiagnosticRow.Priority
        } else { -1 }
        payloadBytes = if ($null -ne $faultDiagnosticRow) {
            [int]$faultDiagnosticRow.PayloadBytes
        } else { 0 }
        payloadSHA256 = if ($null -ne $faultDiagnosticRow) {
            [string]$faultDiagnosticRow.PayloadSHA256
        } else { "" }
        wireSHA256 = if ($null -ne $faultDiagnosticRow) {
            [string]$faultDiagnosticRow.WireSHA256
        } else { "" }
        sourceToken = if ($null -ne $faultDiagnosticRow) {
            [string]$faultDiagnosticRow.Parsed.SourceToken
        } else { "" }
        fixtureOnly = if ($null -ne $faultDiagnosticRow) {
            [bool]$faultDiagnosticRow.Parsed.Payload.FixtureOnly
        } else { $false }
        runtimeObservation = if ($null -ne $faultDiagnosticRow) {
            [bool]$faultDiagnosticRow.Parsed.Payload.RuntimeObservation
        } else { $true }
        reservePressure = if ($null -ne $faultDiagnosticRow) {
            [bool]$faultDiagnosticRow.Parsed.Payload.ReservePressure
        } else { $false }
    }
    $faultDiagnosticConsumerAckJson =
        $faultDiagnosticConsumerAck | ConvertTo-Json -Depth 4 -Compress
    Write-Utf8NoBomAtomic -Path $faultDiagnosticConsumerAckPath -Content (
        $faultDiagnosticConsumerAckJson + "`n")
    $faultDiagnosticConsumerAckSHA256 = (Get-FileHash -LiteralPath $faultDiagnosticConsumerAckPath -Algorithm SHA256).Hash
    $faultDiagnosticConsumerAckRecord = $null
    $faultDiagnosticConsumerArtifactVerified = $false
    try {
        $faultDiagnosticConsumerAckRaw = Read-StrictUtf8Text -Path $faultDiagnosticConsumerAckPath
        if ($faultDiagnosticConsumerAckRaw -cne
                ($faultDiagnosticConsumerAckJson + "`n") -or
            $faultDiagnosticConsumerAckRaw.IndexOf("`r") -ge 0 -or
            $faultDiagnosticConsumerAckRaw.IndexOf([char]0) -ge 0 -or
            $faultDiagnosticConsumerAckRaw.IndexOf("`n") -ne
                $faultDiagnosticConsumerAckRaw.Length - 1) {
            throw "fault diagnostic consumer ack was not canonical exact UTF-8 JSON plus one LF"
        }
        $faultDiagnosticConsumerAckRecord =
            $faultDiagnosticConsumerAckRaw | ConvertFrom-Json
        $faultDiagnosticAckSchemaVersion = ConvertTo-ExactUInt32OrNull `
            -Value $faultDiagnosticConsumerAckRecord.schemaVersion
        $faultDiagnosticConsumerArtifactVerified =
            ($faultDiagnosticConsumerAckRecord | ConvertTo-Json -Depth 4 -Compress) -ceq
                $faultDiagnosticConsumerAckJson -and
            [string]$faultDiagnosticConsumerAckRecord.schemaId -ceq
                "God2FaultDiagnosticConsumerAck" -and
            $null -ne $faultDiagnosticAckSchemaVersion -and
            [uint32]$faultDiagnosticAckSchemaVersion -eq 1 -and
            $faultDiagnosticConsumerAckRecord.passed -is [bool] -and
            [bool]$faultDiagnosticConsumerAckRecord.passed -eq
                [bool]$faultDiagnosticConsumerPassed
    } catch {
        $failures.Add("fault diagnostic consumer ack re-read failed: $($_.Exception.Message)")
    }
    if (-not $faultDiagnosticConsumerPassed) {
        $failures.Add("fault diagnostic P0 row was not uniquely observed by the x64 shared-ring consumer")
    }
    if (-not $faultDiagnosticConsumerArtifactVerified) {
        $failures.Add("fault diagnostic consumer ack was not canonical/type-preserving on re-read")
    }
    $sharedFixtureDrainedEvent.Set() | Out-Null

    $metadataDeadline = (Get-Date).AddSeconds(15)
    do {
        [void](Receive-God2SharedSemanticAvailable -Accessor $sharedAccessor `
            -HeaderBytes $sharedHeaderBytes -SlotBytes $sharedSlotBytes `
            -StrictUtf8 $strictUtf8 -State $sharedConsumerState)
        if ((Test-Path -LiteralPath $metadata) -and (Get-Item -LiteralPath $metadata).Length -gt 0) { break }
        [void]$sharedEvent.WaitOne(20)
    } while ((Get-Date) -lt $metadataDeadline -and -not $gameProcess.HasExited)
    if (-not (Test-Path -LiteralPath $metadata)) {
        $failures.Add("injected metadata was not created")
    }

    $metadataReadableWhileAttached = $false
    if (Test-Path -LiteralPath $metadata) {
        try {
            $liveReader = [IO.File]::Open($metadata, [IO.FileMode]::Open, [IO.FileAccess]::Read,
                [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            try {
                $probeBuffer = [byte[]]::new(1)
                [void] $liveReader.Read($probeBuffer, 0, 1)
                $metadataReadableWhileAttached = $true
            }
            finally { $liveReader.Dispose() }
        }
        catch {
            $failures.Add("injected metadata cannot be tailed while the probe is attached: $($_.Exception.Message)")
        }
    }

    $runtimeFixturesDeadline = (Get-Date).AddSeconds(20)
    $runtimeFixturesReady = $false
    while ((Get-Date) -lt $runtimeFixturesDeadline -and -not $gameProcess.HasExited) {
        [void](Receive-God2SharedSemanticAvailable -Accessor $sharedAccessor `
            -HeaderBytes $sharedHeaderBytes -SlotBytes $sharedSlotBytes `
            -StrictUtf8 $strictUtf8 -State $sharedConsumerState)
        if ($runtimeFixturesReadyEvent.WaitOne(0)) {
            $runtimeFixturesReady = $true
            break
        }
        [void]$sharedEvent.WaitOne(20)
    }
    if (-not $runtimeFixturesReady) {
        $failures.Add("runtime completion/pending fixtures did not signal ready-for-stop")
    }
    $stopEvent.Set() | Out-Null
    $stopDrainDeadline = (Get-Date).AddSeconds(30)
    $stopDrainComplete = $false
    do {
        [void](Receive-God2SharedSemanticAvailable -Accessor $sharedAccessor `
            -HeaderBytes $sharedHeaderBytes -SlotBytes $sharedSlotBytes `
            -StrictUtf8 $strictUtf8 -State $sharedConsumerState)
        $producerClosedNow = $sharedAccessor.ReadInt32(64L)
        $injectorExitedNow = $injectorProcess.HasExited
        if ($producerClosedNow -eq 1 -and
            $sharedAccessor.ReadInt32(36L) -eq
                $sharedAccessor.ReadInt32(32L) -and $injectorExitedNow) {
            $stopDrainComplete = $true
            break
        }
        [void]$sharedEvent.WaitOne(20)
    } while ((Get-Date) -lt $stopDrainDeadline)
    if (-not $injectorProcess.HasExited) {
        $failures.Add("injector did not stop within 30 seconds")
    }
    if (-not $stopDrainComplete) {
        $failures.Add("shared semantic consumer did not remain active through producer_closed and final drain")
    }
    $sharedAccessor.Write(68L, [int]0)
    $sharedAccessor.Write(72L, [int]1)
    $detachResult = if (Test-Path -LiteralPath $resultPath) {
        try { Read-StrictUtf8Text -Path $resultPath } catch { "" }
    } else { "" }
    $detachRecord = if ($detachResult.Length -gt 0) {
        ConvertFrom-StrictInjectorResultV2 -Raw $detachResult
    } else { $null }
    if ($null -ne $detachRecord) {
        Write-Utf8NoBomAtomic -Path $detachEvidencePath -Content $detachResult
    }
    $detachResultSHA256 = if (Test-Path -LiteralPath $detachEvidencePath) {
        (Get-FileHash -LiteralPath $detachEvidencePath -Algorithm SHA256).Hash
    } else { "" }
    if ($null -eq $detachRecord -or
        [int]$detachRecord.schemaVersion -ne 2 -or
        [string]$detachRecord.status -cne "DETACHED") {
        $failures.Add("injector did not report DETACHED: $detachResult")
    }
    $moduleUnloaded = $null -ne $detachRecord -and [bool]$detachRecord.moduleUnloaded
    $moduleResidentInactive = $null -ne $detachRecord -and [bool]$detachRecord.moduleResidentInactive
    $moduleSnapshotVerified = $null -ne $detachRecord -and [bool]$detachRecord.moduleSnapshotVerified
    $moduleAbsent = $null -ne $detachRecord -and [bool]$detachRecord.moduleAbsent
    $unloadHandshakeSafe = $null -ne $detachRecord -and [bool]$detachRecord.unloadSafe
    $extraReferenceNegativeFirstFreeLibraryStillPresent = $null -ne $detachRecord -and
        [bool]$detachRecord.extraReferenceNegativeFirstFreeLibraryStillPresent
    $extraReferenceNegativeNotClaimedUnloaded = $null -ne $detachRecord -and
        [bool]$detachRecord.extraReferenceNegativeNotClaimedUnloaded
    $extraReferenceReleasedThenModuleAbsent = $null -ne $detachRecord -and
        [bool]$detachRecord.extraReferenceReleasedThenModuleAbsent
    $unloadSafe = $moduleUnloaded -and -not $moduleResidentInactive -and
        $moduleSnapshotVerified -and $moduleAbsent -and $unloadHandshakeSafe -and
        $null -ne $detachRecord -and [bool]$detachRecord.strictUnloadVerified -and
        -not [bool]$detachRecord.cleanupRetriable -and
        $extraReferenceNegativeFirstFreeLibraryStillPresent -and
        $extraReferenceNegativeNotClaimedUnloaded -and
        $extraReferenceReleasedThenModuleAbsent
    if (-not $unloadSafe) {
        $failures.Add("probe did not complete the quiescent FreeLibrary unload handshake: $detachResult")
    }
    $blockingDetachObservedUtc = [DateTime]::UtcNow.ToString("o")
    $blockingTargetAliveAtDetach = -not $gameProcess.HasExited
    $blockingAllReturnedAtDetach = $blockingAllReturnedEvent.WaitOne(0)
    $blockingModuleAbsentBeforeRelease = $unloadSafe -and
        $blockingTargetAliveAtDetach -and -not $blockingAllReturnedAtDetach
    if (-not $blockingModuleAbsentBeforeRelease) {
        $failures.Add("blocking-call unload ordering failed: module absence was not verified while the target was alive and all four original calls were still pending")
    }
    $blockingReleaseIssuedUtc = [DateTime]::UtcNow.ToString("o")
    $blockingReleaseIssued = $blockingReleaseEvent.Set()
    $blockingAllReturnedAfterRelease = $blockingAllReturnedEvent.WaitOne(15000)
    $blockingAllReturnedObservedUtc = if ($blockingAllReturnedAfterRelease) {
        [DateTime]::UtcNow.ToString("o")
    } else { "" }
    if (-not $blockingReleaseIssued -or -not $blockingAllReturnedAfterRelease) {
        $failures.Add("blocking calls did not return after the external post-unload release barrier")
    }
    $nativeReport = $null
    $nativeReportSHA256 = ""
    if (Test-Path -LiteralPath $nativeReportPath) {
        try {
            $nativeReportArtifact = Read-StrictUtf8Artifact -Path $nativeReportPath
            $nativeReportRaw = [string]$nativeReportArtifact.Raw
            if (-not (Test-StrictJsonNoDuplicateProperties -Raw $nativeReportRaw)) {
                throw "native report contained invalid JSON or duplicate object properties"
            }
            $nativeDuplicateNegatives = @(
                $nativeReportRaw.Replace(
                    '"SensitiveOutboundPrivacy":{',
                    '"SensitiveOutboundPrivacy":null,"SensitiveOutboundPrivacy":{'),
                $nativeReportRaw.Replace(
                    '"FixturePassed":true',
                    '"FixturePassed":false,"FixturePassed":true'),
                $nativeReportRaw.Replace(
                    '"FixturePassed":true',
                    '"\u0046ixturePassed":false,"FixturePassed":true')
            )
            foreach ($negative in $nativeDuplicateNegatives) {
                if (-not (Test-StrictJsonNoDuplicateProperties -Raw $negative)) {
                    $nativeReportDuplicateNegativeCount++
                }
            }
            if ($nativeReportDuplicateNegativeCount -ne 3) {
                throw "native report duplicate-key negatives were accepted"
            }
            $nativeReport = $nativeReportRaw | ConvertFrom-Json
            $nativeReportSHA256 = [string]$nativeReportArtifact.SHA256
        }
        catch { $failures.Add("native probe self-test report is invalid JSON: $($_.Exception.Message)") }
    } else {
        $failures.Add("native probe self-test report was not created: $nativeReportPath")
    }
    if ($null -ne $nativeReport) {
        if ($nativeReport.SchemaId -ne "God2NativeProbeSelfTest" -or
            [int]$nativeReport.SchemaVersion -ne 1 -or
            [string]$nativeReport.IdentityProfile -cne
                "TestOnlyExactFixture") {
            $failures.Add("native probe self-test report schema mismatch")
        }
        $sensitiveOutboundPrivacyRecord = $nativeReport.SensitiveOutboundPrivacy
        $sensitiveFixtureCases = @(
            "ErrorZeroRetry","MultiWsabufPartial","PendingCrossThread",
            "CancelRetainsIntent","SameAddressMutation","UnboundMultipleIntents",
            "CapacityAndEpoch","StaleCompletionAfterReuse",
            "FaultBeforeAndAfterCommit","NestedSameLengthBeforeReal",
            "CrossThreadProducerTokenGate","PendingPublicationOrdering",
            "EarlyIocpCompletionBeforePost",
            "GqcsExInternalFailureRetainsIntent",
            "CompletingOwnershipNotStolen"
        )
        $sensitiveFixtureRows = @($sensitiveOutboundPrivacyRecord.FixtureResults)
        $sensitiveLiveIntegration =
            $sensitiveOutboundPrivacyRecord.TestOnlyLiveIntegration
        $sensitiveLiveIntegrationPassed =
            (Test-ExactObjectProperties $sensitiveLiveIntegration @(
                "ActiveFinal","ActualPartialObserved","CompletedDelta",
                "CompletionObserved","CreatedDelta",
                "DeterministicPartialFixturePassed","Invoked","LastError",
                "OfficialRuntime","PartialDelta","Passed","PendingFinal",
                "PendingObserved","RetryDelta","RetryObserved","SchemaId",
                "SchemaVersion","SendBridgeObserved","SerializerAdapterObserved",
                "SerializerEntryObserved","SinkAdmissionObserved","Stage",
                "SuccessfulRetryCapturedLength","SuccessfulRetrySensitivePayloadSuppressed",
                "SuccessfulRetryIdentity","SuccessfulRetrySameShapeDecoyRejected",
                "SuccessfulRetrySameShapeDecoySequence","SuccessfulRetrySinkAdmitted",
                "SuccessfulRetrySinkSequence","DecoyCannotSubstituteDroppedReal",
                "TestOnlyExactFixture","WSASendBridgeObserved",
                "WsaSendPendingIdentity","WsaSendPendingSameShapeDecoyRejected",
                "WsaSendPendingSameShapeDecoySequence",
                "WsaSendPendingCapturedLength","WsaSendPendingSensitivePayloadSuppressed",
                "WsaSendPendingSinkAdmitted","WsaSendPendingSinkSequence")) -and
            [string]$sensitiveLiveIntegration.SchemaId -ceq
                "God2SensitiveOutboundIntegration" -and
            $sensitiveLiveIntegration.SchemaVersion -is [int] -and
            [int]$sensitiveLiveIntegration.SchemaVersion -eq 1 -and
            $sensitiveLiveIntegration.Invoked -is [bool] -and
            [bool]$sensitiveLiveIntegration.Invoked -and
            $sensitiveLiveIntegration.Passed -is [bool] -and
            [bool]$sensitiveLiveIntegration.Passed -and
            $sensitiveLiveIntegration.Stage -is [int] -and
            [int]$sensitiveLiveIntegration.Stage -eq 5 -and
            $sensitiveLiveIntegration.LastError -is [int] -and
            [int]$sensitiveLiveIntegration.LastError -eq 0 -and
            $sensitiveLiveIntegration.TestOnlyExactFixture -is [bool] -and
            [bool]$sensitiveLiveIntegration.TestOnlyExactFixture -and
            $sensitiveLiveIntegration.OfficialRuntime -is [bool] -and
            -not [bool]$sensitiveLiveIntegration.OfficialRuntime -and
            $sensitiveLiveIntegration.SerializerAdapterObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.SerializerAdapterObserved -and
            $sensitiveLiveIntegration.SerializerEntryObserved -is [bool] -and
            -not [bool]$sensitiveLiveIntegration.SerializerEntryObserved -and
            $sensitiveLiveIntegration.SendBridgeObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.SendBridgeObserved -and
            $sensitiveLiveIntegration.WSASendBridgeObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.WSASendBridgeObserved -and
            $sensitiveLiveIntegration.RetryObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.RetryObserved -and
            $sensitiveLiveIntegration.ActualPartialObserved -is [bool] -and
            -not [bool]$sensitiveLiveIntegration.ActualPartialObserved -and
            $sensitiveLiveIntegration.DeterministicPartialFixturePassed -is [bool] -and
            [bool]$sensitiveLiveIntegration.DeterministicPartialFixturePassed -and
            $sensitiveLiveIntegration.PendingObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.PendingObserved -and
            $sensitiveLiveIntegration.CompletionObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.CompletionObserved -and
            $sensitiveLiveIntegration.CreatedDelta -is [int] -and
            [int]$sensitiveLiveIntegration.CreatedDelta -gt 0 -and
            $sensitiveLiveIntegration.CompletedDelta -is [int] -and
            [int]$sensitiveLiveIntegration.CompletedDelta -eq
                [int]$sensitiveLiveIntegration.CreatedDelta -and
            $sensitiveLiveIntegration.RetryDelta -is [int] -and
            [int]$sensitiveLiveIntegration.RetryDelta -ge 1 -and
            $sensitiveLiveIntegration.PartialDelta -is [int] -and
            [int]$sensitiveLiveIntegration.PartialDelta -eq 0 -and
            $sensitiveLiveIntegration.SinkAdmissionObserved -is [bool] -and
            [bool]$sensitiveLiveIntegration.SinkAdmissionObserved -and
            $sensitiveLiveIntegration.SuccessfulRetrySinkAdmitted -is [bool] -and
            [bool]$sensitiveLiveIntegration.SuccessfulRetrySinkAdmitted -and
            $sensitiveLiveIntegration.SuccessfulRetrySinkSequence -is [int] -and
            [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence -gt 0 -and
            $sensitiveLiveIntegration.SuccessfulRetryCapturedLength -is [int] -and
            [int]$sensitiveLiveIntegration.SuccessfulRetryCapturedLength -eq 0 -and
            $sensitiveLiveIntegration.SuccessfulRetrySensitivePayloadSuppressed -is [bool] -and
            [bool]$sensitiveLiveIntegration.SuccessfulRetrySensitivePayloadSuppressed -and
            $sensitiveLiveIntegration.WsaSendPendingSinkAdmitted -is [bool] -and
            [bool]$sensitiveLiveIntegration.WsaSendPendingSinkAdmitted -and
            $sensitiveLiveIntegration.WsaSendPendingSinkSequence -is [int] -and
            [int]$sensitiveLiveIntegration.WsaSendPendingSinkSequence -gt
                [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence -and
            $sensitiveLiveIntegration.WsaSendPendingCapturedLength -is [int] -and
            [int]$sensitiveLiveIntegration.WsaSendPendingCapturedLength -eq 0 -and
            $sensitiveLiveIntegration.WsaSendPendingSensitivePayloadSuppressed -is [bool] -and
            [bool]$sensitiveLiveIntegration.WsaSendPendingSensitivePayloadSuppressed -and
            $sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoyRejected -is [bool] -and
            [bool]$sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoyRejected -and
            $sensitiveLiveIntegration.WsaSendPendingSameShapeDecoyRejected -is [bool] -and
            [bool]$sensitiveLiveIntegration.WsaSendPendingSameShapeDecoyRejected -and
            $sensitiveLiveIntegration.DecoyCannotSubstituteDroppedReal -is [bool] -and
            [bool]$sensitiveLiveIntegration.DecoyCannotSubstituteDroppedReal -and
            $sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoySequence -is [int] -and
            [int]$sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoySequence -gt 0 -and
            [int]$sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoySequence -lt
                [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence -and
            $sensitiveLiveIntegration.WsaSendPendingSameShapeDecoySequence -is [int] -and
            [int]$sensitiveLiveIntegration.WsaSendPendingSameShapeDecoySequence -gt
                [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence -and
            [int]$sensitiveLiveIntegration.WsaSendPendingSameShapeDecoySequence -lt
                [int]$sensitiveLiveIntegration.WsaSendPendingSinkSequence -and
            $(Test-SensitiveOutboundIntegrationIdentity `
                -Identity $sensitiveLiveIntegration.SuccessfulRetryIdentity `
                -ExpectedSegmentCount 1 -RequireOverlapped $false `
                -RequireBridgeGeneration $false) -and
            $(Test-SensitiveOutboundIntegrationIdentity `
                -Identity $sensitiveLiveIntegration.WsaSendPendingIdentity `
                -ExpectedSegmentCount 3 -RequireOverlapped $true `
                -RequireBridgeGeneration $true) -and
            $sensitiveLiveIntegration.PendingFinal -is [int] -and
            [int]$sensitiveLiveIntegration.PendingFinal -eq 0 -and
            $sensitiveLiveIntegration.ActiveFinal -is [int] -and
            [int]$sensitiveLiveIntegration.ActiveFinal -eq 0
        $sensitiveOutboundPrivacyPassed =
            (Test-ExactObjectProperties $sensitiveOutboundPrivacyRecord @(
                "AllContextPointersNonAliasing","AmbiguousCount","Capacity",
                "CapacityDrops","CompletedTransactions","CreatedTransactions",
                "CrossThreadCompletionCount","EpochExhaustedCount","FixtureCount",
                "FixturePassed","FixturePassedCount","FixturePassedMask",
                "FixtureResults","EarlyCompletionCapturedLength",
                "EarlyCompletionPayloadBytesPersisted",
                "EarlyCompletionPrivateSinkNonAliasing",
                "EarlyCompletionRedactionCarryPassed",
                "EarlyCompletionSensitivePayloadSuppressed",
                "LiveActiveTransactions","LivePendingTransactions",
                "LiveSnapshotUnchanged","LiveStateUntouched","OpaqueFailClosed",
                "PartialCount","PendingAtDisarm","PointerAliasNegativePassed",
                "PrivateNonAliasing","PurgedAtDisarm","RetryCount","SchemaId",
                "SchemaVersion","StaleCompletionCount","TestOnlyLiveIntegration",
                "UnverifiableCount")) -and
            [string]$sensitiveOutboundPrivacyRecord.SchemaId -ceq
                "God2SensitiveOutboundPrivacy" -and
            $sensitiveOutboundPrivacyRecord.SchemaVersion -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.SchemaVersion -eq 1 -and
            $sensitiveOutboundPrivacyRecord.Capacity -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.Capacity -eq 256 -and
            $sensitiveOutboundPrivacyRecord.FixtureCount -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.FixtureCount -eq 15 -and
            $sensitiveOutboundPrivacyRecord.FixturePassedCount -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.FixturePassedCount -eq 15 -and
            $sensitiveOutboundPrivacyRecord.FixturePassedMask -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.FixturePassedMask -eq 32767 -and
            $sensitiveOutboundPrivacyRecord.FixturePassed -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.FixturePassed -and
            $sensitiveOutboundPrivacyRecord.PrivateNonAliasing -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.PrivateNonAliasing -and
            $sensitiveOutboundPrivacyRecord.AllContextPointersNonAliasing -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.AllContextPointersNonAliasing -and
            $sensitiveOutboundPrivacyRecord.PointerAliasNegativePassed -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.PointerAliasNegativePassed -and
            $sensitiveOutboundPrivacyRecord.EarlyCompletionRedactionCarryPassed -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.EarlyCompletionRedactionCarryPassed -and
            $sensitiveOutboundPrivacyRecord.EarlyCompletionPrivateSinkNonAliasing -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.EarlyCompletionPrivateSinkNonAliasing -and
            $sensitiveOutboundPrivacyRecord.EarlyCompletionSensitivePayloadSuppressed -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.EarlyCompletionSensitivePayloadSuppressed -and
            $sensitiveOutboundPrivacyRecord.EarlyCompletionCapturedLength -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.EarlyCompletionCapturedLength -eq 0 -and
            $sensitiveOutboundPrivacyRecord.EarlyCompletionPayloadBytesPersisted -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.EarlyCompletionPayloadBytesPersisted -eq 0 -and
            $sensitiveOutboundPrivacyRecord.LiveSnapshotUnchanged -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.LiveSnapshotUnchanged -and
            $sensitiveOutboundPrivacyRecord.LiveStateUntouched -is [bool] -and
            [bool]$sensitiveOutboundPrivacyRecord.LiveStateUntouched -and
            $sensitiveOutboundPrivacyRecord.LiveActiveTransactions -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.LiveActiveTransactions -eq 0 -and
            $sensitiveOutboundPrivacyRecord.LivePendingTransactions -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.LivePendingTransactions -eq 0 -and
            $sensitiveOutboundPrivacyRecord.CreatedTransactions -is [int] -and
            $sensitiveOutboundPrivacyRecord.CompletedTransactions -is [int] -and
            $sensitiveOutboundPrivacyRecord.PendingAtDisarm -is [int] -and
            $sensitiveOutboundPrivacyRecord.PurgedAtDisarm -is [int] -and
            [int]$sensitiveOutboundPrivacyRecord.PendingAtDisarm -eq
                [int]$sensitiveOutboundPrivacyRecord.PurgedAtDisarm -and
            [int]$sensitiveOutboundPrivacyRecord.CreatedTransactions -eq
                ([int]$sensitiveOutboundPrivacyRecord.CompletedTransactions +
                 [int]$sensitiveOutboundPrivacyRecord.PurgedAtDisarm) -and
            [int]$sensitiveOutboundPrivacyRecord.CreatedTransactions -eq
                [int]$sensitiveLiveIntegration.CreatedDelta -and
            [int]$sensitiveOutboundPrivacyRecord.CompletedTransactions -eq
                [int]$sensitiveLiveIntegration.CompletedDelta -and
            [int]$sensitiveOutboundPrivacyRecord.RetryCount -eq
                [int]$sensitiveLiveIntegration.RetryDelta -and
            [int]$sensitiveOutboundPrivacyRecord.PartialCount -eq
                [int]$sensitiveLiveIntegration.PartialDelta -and
            $sensitiveFixtureRows.Count -eq 15 -and
            $sensitiveLiveIntegrationPassed
        foreach ($countName in @(
            "CreatedTransactions","CompletedTransactions","RetryCount",
            "PartialCount","CrossThreadCompletionCount","AmbiguousCount",
            "UnverifiableCount","CapacityDrops","EpochExhaustedCount",
            "StaleCompletionCount","PendingAtDisarm","PurgedAtDisarm")) {
            $countValue = $sensitiveOutboundPrivacyRecord.$countName
            $sensitiveOutboundPrivacyPassed = $sensitiveOutboundPrivacyPassed -and
                $countValue -is [int] -and [int]$countValue -ge 0
        }
        $sensitiveOutboundPrivacyPassed = $sensitiveOutboundPrivacyPassed -and
            $sensitiveOutboundPrivacyRecord.OpaqueFailClosed -is [bool]
        for ($sensitiveIndex = 0; $sensitiveIndex -lt
                $sensitiveFixtureRows.Count; $sensitiveIndex++) {
            $sensitiveRow = $sensitiveFixtureRows[$sensitiveIndex]
            $sensitiveOutboundPrivacyPassed = $sensitiveOutboundPrivacyPassed -and
                (Test-ExactObjectProperties $sensitiveRow @(
                    "Case","Index","Passed")) -and
                $sensitiveRow.Index -is [int] -and
                [int]$sensitiveRow.Index -eq $sensitiveIndex -and
                [string]$sensitiveRow.Case -ceq
                    $sensitiveFixtureCases[$sensitiveIndex] -and
                $sensitiveRow.Passed -is [bool] -and [bool]$sensitiveRow.Passed
        }
        if (-not $sensitiveOutboundPrivacyPassed) {
            $failures.Add("sensitive outbound transaction privacy contract was not exact 15/15 with non-aliasing, reconciled Stop purge, and bound test-only live send/WSASend integration")
        }
        $nativeDomainRows = @($nativeReport.SharedTransportDomainResults)
        if (-not [bool]$nativeReport.SharedTransportConsumerDrainVerified -or
            $nativeDomainRows.Count -ne 25 -or
            @($nativeDomainRows | Where-Object { [int]$_.Pending -ne 0 }).Count -ne 0) {
            $failures.Add("native report was frozen before the bound consumer drained all 25 domains")
        }
        $nativeSemanticTypeCount = [int]$nativeReport.SemanticEventProducerFixtureCount
        $nativeBatchMaximumItems = [int]$nativeReport.SharedTransportBatchMaximumItems
        $nativeBatchAccepted = [int]$nativeReport.SharedTransportBatchAccepted
        $nativeBatchCount = [int]$nativeReport.SharedTransportBatchCount
        $nativeBatchEventSignalDelta = [int]$nativeReport.SharedTransportBatchEventSignalDelta
        $nativeBatchLockAcquisitionDelta = [int]$nativeReport.SharedTransportBatchLockAcquisitionDelta
        $nativeBatchContractPassed = [bool]$nativeReport.SharedTransportBatchContractPassed
        if ($nativeSemanticTypeCount -ne 25 -or $nativeTypeFixtures.Count -ne 25 -or
            $nativeTypeNames.Count -ne 25) {
            $failures.Add("native semantic type producer matrix incomplete: native=$nativeSemanticTypeCount fixtures=$($nativeTypeFixtures.Count) distinct=$($nativeTypeNames.Count)")
        }
        if (-not $nativeBatchContractPassed -or $nativeBatchMaximumItems -ne 8 -or
            $nativeBatchAccepted -le 1 -or $nativeBatchCount -le 0 -or
            $nativeBatchEventSignalDelta -ne $nativeBatchCount -or
            $nativeBatchLockAcquisitionDelta -le 0 -or
            $nativeBatchLockAcquisitionDelta -gt 256 -or
            -not [bool]$nativeReport.SharedTransportPriorityContractPassed) {
            $failures.Add("native measured batch/priority contract counters failed")
        }
        if (-not [bool]$nativeReport.FaultDiagnosticPriorityPolicyPassed -or
            [int]$nativeReport.FaultDiagnosticSelectedPriority -ne 0 -or
            [int]$nativeReport.StartupProbeDiagnosticSelectedPriority -ne 3 -or
            -not [bool]$nativeReport.FaultDiagnosticAcceptedAfterLowPriorityCeiling -or
            -not [bool]$nativeReport.FaultDiagnosticAcceptedBeyondLegacyHardLimit -or
            [int]$nativeReport.FaultDiagnosticBeyondLegacyLimitDropReason -ne 0 -or
            -not [bool]$nativeReport.FaultDiagnosticAdmissionLiveStateUntouched -or
            -not [bool]$nativeReport.FaultDiagnosticProducerPublishReturned -or
            -not $faultDiagnosticConsumerPassed) {
            $failures.Add("fault diagnostic priority/admission/producer-to-x64-consumer binding failed")
        }
        if (-not [bool]$nativeReport.HookThreadRingNullFallbackPassed -or
            [int]$nativeReport.HookThreadRingNullSemanticLossCount -ne 1 -or
            [int]$nativeReport.HookThreadFileIoOperations -ne 0) {
            $failures.Add("hook-thread ring-null fail-closed/nonblocking fixture failed")
        }
        if (-not [bool]$nativeReport.IatOwnerLifecycleSelfTestPassed -or
            -not [bool]$nativeReport.IatOwnerGoneRetired -or
            -not [bool]$nativeReport.IatAddressReuseNotClobbered -or
            -not [bool]$nativeReport.IatReplacementCasRestored -or
            -not [bool]$nativeReport.IatOriginalNotClobbered -or
            -not [bool]$nativeReport.IatThirdPartyNotClobbered) {
            $failures.Add("IAT owner unload/address-reuse/CAS lifecycle fixture failed")
        }
        if ([int]$nativeReport.SharedTransportBatchValidationWriteFailureCount -ne 2 -or
            [int]$nativeReport.SharedTransportDomainWriteFailureTotal -ne 2) {
            $failures.Add("native shared-ring write-failure attribution was not exactly the two injected invalid batch items")
        }
        if ([int]$nativeReport.CompletionRoutineObserved -le 0 -or
            [int]$nativeReport.WSAGetOverlappedResultObserved -le 0 -or
            [int]$nativeReport.GetQueuedCompletionStatusObserved -le 0 -or
            [int]$nativeReport.GetQueuedCompletionStatusExObserved -le 0) {
            $failures.Add("native runtime did not observe all four overlapped completion mechanisms")
        }
        $bridgeReport = $nativeReport.UnloadNeutralBridge
        $bridgeCoverage = @($bridgeReport.BridgeCoverageResults)
        $blockingCoverageIndices = @(2,4,5,6)
        $blockingCoverageApis = @(
            "recv","WSAGetOverlappedResult","GetQueuedCompletionStatus",
            "GetQueuedCompletionStatusEx")
        $bridgeBlockingCoveragePassed = $bridgeCoverage.Count -eq 11
        for ($blockingCoverageIndex = 0; $blockingCoverageIndex -lt
                $blockingCoverageIndices.Count; $blockingCoverageIndex++) {
            $coverageRow = if ($bridgeCoverage.Count -eq 11) {
                $bridgeCoverage[$blockingCoverageIndices[$blockingCoverageIndex]]
            } else { $null }
            $bridgeBlockingCoveragePassed = $bridgeBlockingCoveragePassed -and
                $null -ne $coverageRow -and
                [string]$coverageRow.Api -ceq
                    $blockingCoverageApis[$blockingCoverageIndex] -and
                $coverageRow.InstalledToBridge -is [bool] -and
                [bool]$coverageRow.InstalledToBridge -and
                $coverageRow.PreObserverObserved -is [int] -and
                $coverageRow.PostObserverObserved -is [int] -and
                [int]$coverageRow.PreObserverObserved -gt
                    [int]$coverageRow.PostObserverObserved
        }
        if ($null -eq $bridgeReport -or
            [string]$bridgeReport.Phase -cne "Detached" -or
            -not [bool]$bridgeReport.ObserverPointerNull -or
            [int]$bridgeReport.ObserverRundown -ne 0 -or
            [bool]$bridgeReport.DllPointersRemaining -or
            [int]$bridgeReport.DllPointerCount -ne 0 -or
            -not [bool]$bridgeReport.IatRestored -or
            -not [bool]$bridgeReport.HotpatchRestored -or
            [int]$bridgeReport.BridgeFrames -lt 4 -or
            -not $bridgeBlockingCoveragePassed -or
            $bridgeCoverage.Count -ne 11 -or
            @($bridgeCoverage | Select-Object -First 8 | Where-Object {
                -not ($_.InstalledToBridge -is [bool]) -or
                -not [bool]$_.InstalledToBridge
            }).Count -ne 0 -or
            -not [bool]$bridgeReport.WSARecvFinalPurgeRacePassed -or
            -not [bool]$bridgeReport.WSARecvFinalPurgeLateRegistrationObserved -or
            [int]$bridgeReport.WSARecvFinalPurgePurgedCount -le 0 -or
            [int]$bridgeReport.PendingNeutralRetirements -le 0 -or
            [int]$bridgeReport.PendingPurgedAtDisarm -le 0) {
            $failures.Add("unload-neutral bridge did not preserve external blocking frames with detached observer and exact final pending purge")
        }
        if ([int]$nativeReport.PendingAtStopDeferredCount -ne 0 -or
            [int]$nativeReport.PendingAtStopRetryDrainedCount -ne 0 -or
            [int]$nativeReport.PendingAtStopDeferredAttempts -ne 0) {
            $failures.Add("removed live pending-at-stop fixture unexpectedly mutated production counters")
        }
        $admissionRows = @($nativeReport.SemanticAdmissionCapResults)
        if (-not [bool]$nativeReport.SemanticAdmissionCapContractPassed -or
            -not [bool]$nativeReport.SemanticAdmissionCapSharedLedgerPassed -or
            -not [bool]$nativeReport.SemanticAdmissionCapLiveStateUntouched -or
            [int]$nativeReport.SemanticAdmissionHardLimit -ne 2000000 -or
            [int]$nativeReport.SemanticAdmissionLowPriorityCeiling -ge
                [int]$nativeReport.SemanticAdmissionHardLimit -or
            $admissionRows.Count -ne 2 -or
            [int]$admissionRows[0].Priority -ne 3 -or
            [int]$admissionRows[0].AcceptedAtBoundary -ne 1 -or
            [int]$admissionRows[0].DroppedAtBoundary -ne 1 -or
            [int]$admissionRows[0].DropReason -ne 8 -or
            [int]$admissionRows[0].FirstDroppedSequence -le 0 -or
            [int]$admissionRows[0].LastDroppedSequence -ne
                [int]$admissionRows[0].FirstDroppedSequence -or
            [int]$admissionRows[1].Priority -ne 0 -or
            [int]$admissionRows[1].AcceptedBeyondLegacyHardLimit -ne 1 -or
            [int]$admissionRows[1].DroppedBeyondLegacyHardLimit -ne 0 -or
            [int]$admissionRows[1].DropReason -ne 0 -or
            [int]$admissionRows[1].FirstDroppedSequence -ne 0 -or
            [int]$admissionRows[1].LastDroppedSequence -ne 0) {
            $failures.Add("semantic process admission cap did not preserve zero-loss priority-0 admission beyond the legacy cap")
        }
        $priorityRows = @($nativeReport.SharedTransportPriorityResults)
        if ($priorityRows.Count -ne 4) {
            $failures.Add("native priority result row count was $($priorityRows.Count), expected 4")
        } else {
            foreach ($row in $priorityRows) {
                $priority = [int]$row.Priority
                $laneExpected = [int]$row.LaneAccepted -eq 1 -and
                    [int]$row.LaneDropped -eq 0 -and
                    [int]$row.LaneDropReason -eq 0
                $samplingExpected = if ($priority -lt 2) {
                    [int]$row.SamplingProbeDropped -eq 0 -and
                        [int]$row.SamplingProbeDropReason -eq 0
                } else {
                    [int]$row.SamplingProbeDropped -eq 1 -and
                        [int]$row.SamplingProbeDropReason -eq 10
                }
                if (-not $laneExpected -or -not $samplingExpected) {
                    $failures.Add("native priority row failed for priority $priority")
                }
            }
        }
        $faultDomainNames = @(
            "Network","Parser","Serializer","Handler","Object","Allocation",
            "VTable","Factory","ManagerLookup","Registry","ResourceDecode",
            "Mutation","TaintSeed","FormulaOperand","Quest","Map","Portal",
            "NPC","Monster","Battle","Inventory","Item","Skill","Pet/Mount",
            "Snapshot"
        )
        $faultRows = @($nativeReport.FaultIsolationResults)
        if ($faultRows.Count -ne 25 -or @($faultRows | Where-Object {
            -not (Test-ExactObjectProperties $_ @(
                "AffectedDomainDisabled","Domain","DomainIndex","FaultInjected",
                "OtherDomainContinued","OtherDomainsEnabledAtFault",
                "OtherDomainsEnabledMask","OtherDomainsRemainEnabledMeasured",
                "PrivateDiagnosticAttributed","PrivateDiagnosticSinkWritten",
                "RuntimeDiagnosticEmitted")) -or
            -not ($_.DomainIndex -is [int]) -or
            [int]$_.DomainIndex -lt 0 -or [int]$_.DomainIndex -ge 25 -or
            [string]$_.Domain -cne $faultDomainNames[[int]$_.DomainIndex] -or
            -not ($_.FaultInjected -is [bool]) -or -not [bool]$_.FaultInjected -or
            -not ($_.AffectedDomainDisabled -is [bool]) -or
            -not [bool]$_.AffectedDomainDisabled -or
            -not ($_.PrivateDiagnosticAttributed -is [bool]) -or
            -not [bool]$_.PrivateDiagnosticAttributed -or
            -not ($_.PrivateDiagnosticSinkWritten -is [bool]) -or
            -not [bool]$_.PrivateDiagnosticSinkWritten -or
            -not ($_.RuntimeDiagnosticEmitted -is [bool]) -or
            [bool]$_.RuntimeDiagnosticEmitted -or
            -not ($_.OtherDomainsEnabledAtFault -is [int]) -or
            [int]$_.OtherDomainsEnabledAtFault -le 0 -or
            [string]$_.OtherDomainsEnabledMask -cnotmatch '^0x[0-9A-F]{8}$' -or
            -not ($_.OtherDomainsRemainEnabledMeasured -is [bool]) -or
            -not [bool]$_.OtherDomainsRemainEnabledMeasured -or
            -not ($_.OtherDomainContinued -is [bool]) -or
            -not [bool]$_.OtherDomainContinued
        }).Count -ne 0 -or
            @($faultRows | Select-Object -ExpandProperty DomainIndex -Unique).Count -ne 25) {
            $failures.Add("native per-domain fault isolation matrix was not 25/25")
        }
        $asyncRows = @($nativeReport.AsyncDiagnosticDomainResults)
        $asyncDropSum = ($asyncRows | Measure-Object -Property Dropped -Sum).Sum
        $asyncWriteFailureSum = ($asyncRows | Measure-Object -Property WriteFailures -Sum).Sum
        if ($asyncRows.Count -ne 25 -or @($asyncRows | Where-Object {
            [int]$_.Dropped -le 0 -or [int]$_.WriteFailures -le 0 -or
            -not $_.EvidenceIncomplete
        }).Count -ne 0 -or
            [int]$nativeReport.AsyncDiagnosticDropCount -ne $asyncDropSum -or
            [int]$nativeReport.AsyncDiagnosticWriteFailureCount -ne 0 -or
            $asyncWriteFailureSum -ne 25 -or
            -not [bool]$nativeReport.AsyncDiagnosticPrivateNonAliasing -or
            [int]$nativeReport.AsyncDiagnosticPending -ne 0 -or
            [int]$nativeReport.HookThreadFileIoOperations -ne 0) {
            $failures.Add("native bounded async diagnostic queue matrix/counters failed")
        }
    }
    $sharedProducerClosed = $sharedAccessor.ReadInt32(64L)
    if ($sharedProducerClosed -ne 1) {
        $failures.Add("x86 shared-ring producer did not publish producer_closed on detach")
    }
    $sharedSequenceOrdered = $true
    for ($index = 1; $index -lt $sharedSequences.Count; $index++) {
        if ($sharedSequences[$index] -ne $sharedSequences[$index - 1] + 1) {
            $sharedSequenceOrdered = $false
            break
        }
    }
    # Re-read immutable final counters after producer_closed so the summary is
    # bound to the complete runtime, including the pending-at-stop completion.
    $sharedAttempted = $sharedAccessor.ReadInt32(40L)
    $sharedWrite = $sharedAccessor.ReadInt32(32L)
    $sharedDropped = $sharedAccessor.ReadInt32(44L)
    $sharedSampled = $sharedAccessor.ReadInt32(48L)
    $sharedInvalidPayloads = [int]$sharedConsumerState.InvalidPayloads
    $sharedPriorityCounts = $sharedConsumerState.PriorityCounts
    $sharedPriorityMismatches = [int]$sharedConsumerState.PriorityMismatches
    $domainDroppedTotal = 0
    $domainAcceptedTotal = 0
    $domainWriteFailureTotal = 0
    $domainPendingTotal = 0
    for ($domain = 0; $domain -lt 25; $domain++) {
        $domainOffset = 232L + ([long]$domain * 32L)
        $domainAcceptedTotal += $sharedAccessor.ReadInt32($domainOffset)
        $domainDroppedTotal += $sharedAccessor.ReadInt32($domainOffset + 4)
        $domainPendingTotal += $sharedAccessor.ReadInt32($domainOffset + 24)
        $domainWriteFailureTotal += $sharedAccessor.ReadInt32($domainOffset + 28)
    }
    $laneRows = [Collections.Generic.List[object]]::new()
    $laneAcceptedTotal = 0
    $laneReadTotal = 0
    $laneDroppedTotal = 0
    $laneSampledTotal = 0
    $laneContractPassed = $true
    for ($priority = 0; $priority -lt 4; $priority++) {
        $laneOffset = 88L + ([long]$priority * 36L)
        $laneAccepted = $sharedAccessor.ReadInt32($laneOffset)
        $laneRead = $sharedAccessor.ReadInt32($laneOffset + 4L)
        $laneDropped = $sharedAccessor.ReadInt32($laneOffset + 8L)
        $laneSampled = $sharedAccessor.ReadInt32($laneOffset + 12L)
        $laneFirstDropped = $sharedAccessor.ReadInt32($laneOffset + 16L)
        $laneLastDropped = $sharedAccessor.ReadInt32($laneOffset + 20L)
        $laneDropReason = $sharedAccessor.ReadInt32($laneOffset + 24L)
        $laneHighWater = $sharedAccessor.ReadInt32($laneOffset + 28L)
        $laneLag = $sharedAccessor.ReadInt32($laneOffset + 32L)
        $laneAcceptedTotal += $laneAccepted
        $laneReadTotal += $laneRead
        $laneDroppedTotal += $laneDropped
        $laneSampledTotal += $laneSampled
        $priorityLossValid = if ($priority -lt 2) {
            $laneDropped -eq 0 -and $laneSampled -eq 0 -and
                $laneFirstDropped -eq 0 -and $laneLastDropped -eq 0 -and
                $laneDropReason -eq 0
        } elseif ($laneDropped -eq 0) {
            $laneSampled -eq 0 -and $laneFirstDropped -eq 0 -and
                $laneLastDropped -eq 0 -and $laneDropReason -eq 0
        } else {
            $laneSampled -eq $laneDropped -and $laneFirstDropped -gt 0 -and
                $laneLastDropped -ge $laneFirstDropped -and
                $laneDropReason -eq 10
        }
        $rowPassed = $laneAccepted -eq $laneRead -and $laneLag -eq 0 -and
            $laneHighWater -ge 0 -and $laneHighWater -le 64 -and
            $priorityLossValid
        $laneContractPassed = $laneContractPassed -and $rowPassed
        $laneRows.Add([pscustomobject]@{
            Priority = $priority
            Accepted = $laneAccepted
            Consumed = $laneRead
            Dropped = $laneDropped
            Sampled = $laneSampled
            FirstDroppedSequence = $laneFirstDropped
            LastDroppedSequence = $laneLastDropped
            LastDropReason = $laneDropReason
            HighWaterMark = $laneHighWater
            ConsumerLag = $laneLag
            Passed = [bool]$rowPassed
        })
    }
    if ($sharedDropped -ne $domainDroppedTotal -or $domainWriteFailureTotal -ne 2 -or
        $domainPendingTotal -ne 0 -or
        $sharedAttempted -ne $sharedWrite + $sharedDropped -or
        $sharedSampled -ne $sharedDropped -or
        $domainAcceptedTotal -ne $sharedWrite -or
        $laneAcceptedTotal -ne $sharedWrite -or
        $laneReadTotal -ne $sharedWrite -or
        $laneDroppedTotal -ne $sharedDropped -or
        $laneSampledTotal -ne $sharedSampled -or
        -not $laneContractPassed -or
        $sharedAccessor.ReadInt32(36L) -ne $sharedWrite -or
        $sharedAccessor.ReadInt32(56L) -ne 0 -or
        $sharedAccessor.ReadInt32(52L) -gt 256 -or
        $sharedAccessor.ReadInt32(68L) -ne 0 -or
        $sharedAccessor.ReadInt32(72L) -ne 1 -or
        $sharedAccessor.ReadInt32(76L) -ne 0 -or
        $sharedAcceptedPayloads.Count -ne $sharedWrite -or
        -not $sharedSequenceOrdered -or $sharedInvalidPayloads -ne 0 -or
        $sharedPriorityMismatches -ne 0) {
        $failures.Add("final producer-closed ring counters were inconsistent: dropped=$sharedDropped domainDropped=$domainDroppedTotal writeFailures=$domainWriteFailureTotal pending=$domainPendingTotal")
    }

    if (-not $gameProcess.WaitForExit(20000)) {
        $failures.Add("self-test game did not exit within 20 seconds")
    } elseif ($gameProcess.ExitCode -ne 0) {
        $failures.Add("self-test game exit code was $($gameProcess.ExitCode)")
    }

    $blockingPostUnloadRows = @()
    $targetStdoutSHA256 = ""
    $blockingPostUnloadSHA256 = ""
    $blockingPostUnloadRecord = $null
    $blockingRawDuplicateNegativeCount = 0
    $blockingJsonWires = [Collections.Generic.List[string]]::new()
    $blockingPropertyCounts = @{
        SchemaId=1; SchemaVersion=1; Index=1; Api=1
        ReleaseBarrierObserved=1; Returned=1; ResultPreserved=1
        ResourceReusable=1; ObservedResult=1; ObservedTransferred=1
        ObservedFlags=1; ObservedRemoved=1; ObservedCompletionKey=1
        InputOverlapped=1; OutputOverlapped=1; InputResource=1
        BaselineResource=1; EntryLastError=1
        BaselineLastError=1; ObservedLastError=1; PayloadMatched=1
    }
    if (Test-Path -LiteralPath $targetStdoutPath) {
        try {
            $targetStdoutArtifact = Read-StrictUtf8Artifact -Path $targetStdoutPath
            $targetStdoutRaw = [string]$targetStdoutArtifact.Raw
            $targetStdoutSHA256 = [string]$targetStdoutArtifact.SHA256
            foreach ($rawLine in $targetStdoutRaw.Split("`n")) {
                $line = $rawLine.TrimEnd("`r")
                $prefix = "God2BlockingPostUnload="
                if (-not $line.StartsWith($prefix,
                        [StringComparison]::Ordinal)) { continue }
                $blockingWire = $line.Substring($prefix.Length)
                if (-not (Test-ExactJsonPropertyOccurrences `
                        -Raw $blockingWire `
                        -ExpectedCounts $blockingPropertyCounts) -or
                    -not (Test-StrictJsonNoDuplicateProperties -Raw $blockingWire)) {
                    throw "blocking row contained missing or duplicate JSON properties"
                }
                $blockingJsonWires.Add($blockingWire)
                $blockingPostUnloadRows += $blockingWire | ConvertFrom-Json
            }
            if ($blockingJsonWires.Count -gt 0) {
                $blockingDuplicateNegatives = @(
                    $blockingJsonWires[0].Replace(
                        '"ResultPreserved":true',
                        '"ResultPreserved":false,"ResultPreserved":true'),
                    $blockingJsonWires[0].Replace(
                        '"BaselineLastError":',
                        '"BaselineLastError":0,"BaselineLastError":'),
                    $blockingJsonWires[0].Replace(
                        '"ResultPreserved":true',
                        '"\u0052esultPreserved":false,"ResultPreserved":true')
                )
                foreach ($negative in $blockingDuplicateNegatives) {
                    if (-not (Test-ExactJsonPropertyOccurrences -Raw $negative `
                            -ExpectedCounts $blockingPropertyCounts) -or
                        -not (Test-StrictJsonNoDuplicateProperties -Raw $negative)) {
                        $blockingRawDuplicateNegativeCount++
                    }
                }
            }
            if ($blockingRawDuplicateNegativeCount -ne 3) {
                throw "blocking row duplicate-key negatives were accepted"
            }
        } catch {
            $failures.Add("target stdout/blocking evidence was not strict UTF-8 JSONL: $($_.Exception.Message)")
        }
    } else {
        $failures.Add("target stdout evidence was not created")
    }
    $blockingApis = @(
        "GetQueuedCompletionStatus","GetQueuedCompletionStatusEx",
        "recv","WSAGetOverlappedResult"
    )
    $blockingRowNames = @(
        "Api","BaselineLastError","BaselineResource","EntryLastError","Index",
        "InputOverlapped","InputResource",
        "ObservedCompletionKey","ObservedFlags","ObservedLastError",
        "ObservedRemoved","OutputOverlapped",
        "ObservedResult","ObservedTransferred","PayloadMatched",
        "ReleaseBarrierObserved","ResourceReusable","ResultPreserved",
        "Returned","SchemaId","SchemaVersion"
    ) | Sort-Object
    $blockingExpectedTransferred = @(7,7,1,1)
    $blockingExpectedRemoved = @(0,1,0,0)
    $blockingExpectedKey = @(
        [uint32]0x47324E52,[uint32]0x47324E52,[uint32]0,[uint32]0)
    $blockingEntryLastError = @(
        [uint32]0x51A10001,[uint32]0x51A10002,
        [uint32]0x51A10003,[uint32]0x51A10004)
    $blockingRowsValid = $blockingPostUnloadRows.Count -eq 4
    for ($index = 0; $index -lt $blockingPostUnloadRows.Count; $index++) {
        $row = $blockingPostUnloadRows[$index]
        $actualNames = @($row.PSObject.Properties.Name | Sort-Object)
        $blockingRowsValid = $blockingRowsValid -and
            ($actualNames -join "`n") -ceq ($blockingRowNames -join "`n") -and
            [string]$row.SchemaId -ceq "God2BlockingPostUnloadResult" -and
            $row.SchemaVersion -is [int] -and [int]$row.SchemaVersion -eq 1 -and
            $row.Index -is [int] -and [int]$row.Index -eq $index -and
            [string]$row.Api -ceq $blockingApis[$index] -and
            $row.ReleaseBarrierObserved -is [bool] -and
            [bool]$row.ReleaseBarrierObserved -and
            $row.Returned -is [bool] -and [bool]$row.Returned -and
            $row.ResultPreserved -is [bool] -and [bool]$row.ResultPreserved -and
            $row.ResourceReusable -is [bool] -and [bool]$row.ResourceReusable -and
            $row.ObservedResult -is [int] -and [int]$row.ObservedResult -eq 1 -and
            $row.ObservedTransferred -is [int] -and
                [int]$row.ObservedTransferred -eq $blockingExpectedTransferred[$index] -and
            $row.ObservedFlags -is [int] -and [int]$row.ObservedFlags -eq 0 -and
            $row.ObservedRemoved -is [int] -and
                [int]$row.ObservedRemoved -eq $blockingExpectedRemoved[$index] -and
            [string]$row.ObservedCompletionKey -ceq
                ('0x{0:X8}' -f $blockingExpectedKey[$index]) -and
            [string]$row.InputOverlapped -cmatch '^0x[0-9A-F]{8}$' -and
            [string]$row.OutputOverlapped -cmatch '^0x[0-9A-F]{8}$' -and
            [string]$row.InputResource -cmatch '^0x[0-9A-F]{8}$' -and
            [string]$row.InputResource -cne '0x00000000' -and
            [string]$row.BaselineResource -ceq [string]$row.InputResource -and
            ($index -eq 3) -eq
                ([string]$row.InputOverlapped -cne '0x00000000') -and
            ($index -lt 2) -eq
                ([string]$row.OutputOverlapped -cne '0x00000000') -and
            $row.EntryLastError -is [int] -and
                [uint32]$row.EntryLastError -eq
                    $blockingEntryLastError[$index] -and
            $row.BaselineLastError -is [int] -and
            $row.ObservedLastError -is [int] -and
                [uint32]$row.ObservedLastError -eq
                    [uint32]$row.BaselineLastError -and
            $row.PayloadMatched -is [bool] -and [bool]$row.PayloadMatched
    }
    if (-not $blockingRowsValid) {
        $failures.Add("post-unload target rows did not prove exact result and resource reuse for all four previously-blocked APIs")
    }
    $blockingPostUnloadRecord = [ordered]@{
        schemaId = "God2BlockingPostUnloadMatrix"
        schemaVersion = 1
        targetProcessId = [uint32]$gameProcess.Id
        moduleAbsentBeforeRelease = [bool]$blockingModuleAbsentBeforeRelease
        targetAliveAtDetach = [bool]$blockingTargetAliveAtDetach
        allReturnedAtDetach = [bool]$blockingAllReturnedAtDetach
        releaseIssued = [bool]$blockingReleaseIssued
        allReturnedAfterRelease = [bool]$blockingAllReturnedAfterRelease
        rows = @($blockingPostUnloadRows)
    }
    $blockingPostUnloadJson = $blockingPostUnloadRecord |
        ConvertTo-Json -Depth 5 -Compress
    Write-Utf8NoBomAtomic -Path $blockingPostUnloadPath -Content (
        $blockingPostUnloadJson + "`n")
    $blockingPostUnloadRaw = Read-StrictUtf8Text -Path $blockingPostUnloadPath
    if ($blockingPostUnloadRaw -cne ($blockingPostUnloadJson + "`n")) {
        $failures.Add("blocking post-unload artifact was not canonical exact UTF-8 JSON plus one LF")
    }
    $blockingPostUnloadSHA256 = (Get-FileHash -LiteralPath `
        $blockingPostUnloadPath -Algorithm SHA256).Hash

    $records = @()
    $metadataSHA256 = ""
    if (Test-Path -LiteralPath $metadata) {
        $metadataArtifact = Read-StrictUtf8Artifact -Path $metadata
        $metadataRaw = [string]$metadataArtifact.Raw
        $metadataSHA256 = [string]$metadataArtifact.SHA256
        foreach ($line in [regex]::Split($metadataRaw, '\r?\n')) {
            if ($line.Trim().Length -eq 0) { continue }
            try {
                if (-not (Test-StrictJsonNoDuplicateProperties -Raw $line)) {
                    throw "metadata row contained invalid JSON or duplicate object properties"
                }
                $records += ($line | ConvertFrom-Json)
            }
            catch { $failures.Add("metadata contains invalid JSON: $($_.Exception.Message)") }
        }
    }
    $metadataSequenceContractPassed = $records.Count -gt 0
    $metadataSequencesSeen =
        [Collections.Generic.HashSet[uint32]]::new()
    foreach ($record in $records) {
        $metadataSequence = ConvertTo-ExactUInt32OrNull $record.sequence
        if ($null -eq $metadataSequence -or $metadataSequence -eq 0 -or
            -not $metadataSequencesSeen.Add([uint32]$metadataSequence)) {
            $metadataSequenceContractPassed = $false
            break
        }
    }
    if (-not $metadataSequenceContractPassed) {
        $failures.Add("metadata sequence values were not positive and globally unique across the durable JSONL")
    }
    $sensitiveRetryExpectedSocket = '0x{0:X8}' -f
        [uint32]$sensitiveLiveIntegration.SuccessfulRetryIdentity.ExpectedSocket
    $sensitiveWsaSendExpectedSocket = '0x{0:X8}' -f
        [uint32]$sensitiveLiveIntegration.WsaSendPendingIdentity.ExpectedSocket
    $sensitiveRetrySinkRows = @($records | Where-Object {
        $_.sequence -is [int] -and
        [int]$_.sequence -eq
            [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence -and
        [string]$_.Api -ceq "send" -and
        [string]$_.EvidenceLevel -ceq "Transmitted" -and
        $_.ProcessId -is [int] -and [int]$_.ProcessId -eq $gameProcess.Id -and
        $_.ThreadId -is [int] -and [uint32]$_.ThreadId -eq
            [uint32]$sensitiveLiveIntegration.SuccessfulRetryIdentity.ExpectedThreadId -and
        [string]$_.Socket -ceq $sensitiveRetryExpectedSocket -and
        $_.RequestedLength -is [int] -and [int]$_.RequestedLength -eq 12 -and
        $_.TransferredLength -is [int] -and [int]$_.TransferredLength -eq 12 -and
        $_.CapturedLength -is [int] -and [int]$_.CapturedLength -eq 0 -and
        $_.LastError -is [int] -and [int]$_.LastError -eq 0 -and
        [string]$_.PayloadHex -ceq "" -and
        [string]$_.PlaintextHex -ceq "" -and
        $_.Plaintext -is [bool] -and -not [bool]$_.Plaintext -and
        $_.SensitivePayloadRedacted -is [bool] -and
            [bool]$_.SensitivePayloadRedacted -and
        $_.EvidenceIncomplete -is [bool] -and [bool]$_.EvidenceIncomplete
    })
    $sensitiveRetryDecoyRows = @($records | Where-Object {
        $_.sequence -is [int] -and
        [int]$_.sequence -eq
            [int]$sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoySequence -and
        [string]$_.Api -ceq "send" -and
        [string]$_.EvidenceLevel -ceq "Transmitted" -and
        $_.ProcessId -is [int] -and [int]$_.ProcessId -eq $gameProcess.Id -and
        $_.ThreadId -is [int] -and [uint32]$_.ThreadId -ne
            [uint32]$sensitiveLiveIntegration.SuccessfulRetryIdentity.ExpectedThreadId -and
        [string]$_.Socket -ceq $sensitiveRetryExpectedSocket -and
        $_.RequestedLength -is [int] -and [int]$_.RequestedLength -eq 12 -and
        $_.TransferredLength -is [int] -and [int]$_.TransferredLength -eq 12 -and
        $_.CapturedLength -is [int] -and [int]$_.CapturedLength -eq 0 -and
        $_.LastError -is [int] -and [int]$_.LastError -eq 0 -and
        [string]$_.PayloadHex -ceq "" -and
        [string]$_.PlaintextHex -ceq "" -and
        $_.SensitivePayloadRedacted -is [bool] -and
            [bool]$_.SensitivePayloadRedacted
    })
    $sensitiveWsaSendSinkRows = @($records | Where-Object {
        $_.sequence -is [int] -and
        [int]$_.sequence -eq
            [int]$sensitiveLiveIntegration.WsaSendPendingSinkSequence -and
        [string]$_.Api -ceq "WSASend" -and
        [string]$_.EvidenceLevel -ceq "Candidate" -and
        $_.ProcessId -is [int] -and [int]$_.ProcessId -eq $gameProcess.Id -and
        $_.ThreadId -is [int] -and [uint32]$_.ThreadId -eq
            [uint32]$sensitiveLiveIntegration.WsaSendPendingIdentity.ExpectedThreadId -and
        [string]$_.Socket -ceq $sensitiveWsaSendExpectedSocket -and
        $_.RequestedLength -is [int] -and [int]$_.RequestedLength -eq 32768 -and
        $_.TransferredLength -is [int] -and [int]$_.TransferredLength -eq 0 -and
        $_.CapturedLength -is [int] -and [int]$_.CapturedLength -eq 0 -and
        $_.LastError -is [int] -and [int]$_.LastError -eq 997 -and
        [string]$_.PayloadHex -ceq "" -and
        [string]$_.PlaintextHex -ceq "" -and
        $_.Plaintext -is [bool] -and -not [bool]$_.Plaintext -and
        $_.SensitivePayloadRedacted -is [bool] -and
            [bool]$_.SensitivePayloadRedacted -and
        $_.EvidenceIncomplete -is [bool] -and [bool]$_.EvidenceIncomplete
    })
    $sensitiveWsaSendDecoyRows = @($records | Where-Object {
        $_.sequence -is [int] -and
        [int]$_.sequence -eq
            [int]$sensitiveLiveIntegration.WsaSendPendingSameShapeDecoySequence -and
        [string]$_.Api -ceq "WSASend" -and
        [string]$_.EvidenceLevel -ceq "Candidate" -and
        $_.ProcessId -is [int] -and [int]$_.ProcessId -eq $gameProcess.Id -and
        $_.ThreadId -is [int] -and [uint32]$_.ThreadId -ne
            [uint32]$sensitiveLiveIntegration.WsaSendPendingIdentity.ExpectedThreadId -and
        [string]$_.Socket -ceq $sensitiveWsaSendExpectedSocket -and
        $_.RequestedLength -is [int] -and [int]$_.RequestedLength -eq 32768 -and
        $_.TransferredLength -is [int] -and [int]$_.TransferredLength -eq 0 -and
        $_.CapturedLength -is [int] -and [int]$_.CapturedLength -eq 0 -and
        $_.LastError -is [int] -and [int]$_.LastError -eq 997 -and
        [string]$_.PayloadHex -ceq "" -and
        [string]$_.PlaintextHex -ceq "" -and
        $_.SensitivePayloadRedacted -is [bool] -and
            [bool]$_.SensitivePayloadRedacted
    })
    $sensitiveTargetSequences = @(
        [int]$sensitiveLiveIntegration.SuccessfulRetrySameShapeDecoySequence,
        [int]$sensitiveLiveIntegration.SuccessfulRetrySinkSequence,
        [int]$sensitiveLiveIntegration.WsaSendPendingSameShapeDecoySequence,
        [int]$sensitiveLiveIntegration.WsaSendPendingSinkSequence)
    $sensitiveTargetSequenceRowsAreUnique = @($sensitiveTargetSequences |
        Where-Object {
            $targetSequence = $_
            @($records | Where-Object {
                $_.sequence -is [int] -and
                [int]$_.sequence -eq $targetSequence
            }).Count -ne 1
        }).Count -eq 0
    $sensitiveOutboundSinkRecordsPassed =
        $metadataSequenceContractPassed -and
        $sensitiveTargetSequenceRowsAreUnique -and
        $sensitiveRetrySinkRows.Count -eq 1 -and
        $sensitiveRetryDecoyRows.Count -eq 1 -and
        $sensitiveWsaSendSinkRows.Count -eq 1 -and
        $sensitiveWsaSendDecoyRows.Count -eq 1
    $sensitiveOutboundPrivacyPassed = $sensitiveOutboundPrivacyPassed -and
        $sensitiveOutboundSinkRecordsPassed
    if (-not $sensitiveOutboundSinkRecordsPassed) {
        $failures.Add("sensitive outbound live sink rows did not prove exact admitted retry and pending WSASend metadata-only persistence")
    }
    $expectedSendBytes = [byte[]]::new(208)
    $expectedSendBytes[0] = 208
    $expectedSendBytes[1] = 0
    $expectedSendBytes[2] = 0x42
    for ($index = 3; $index -lt 207; $index++) {
        $expectedSendBytes[$index] = [byte]((0x11 + ($index * 31)) -band 0xFF)
    }
    $expectedChecksum = 0
    for ($index = 0; $index -lt 207; $index++) {
        $expectedChecksum = ($expectedChecksum +
            [int]$expectedSendBytes[$index] + 0x3C) -band 0xFF
    }
    $expectedSendBytes[207] = [byte]$expectedChecksum
    $expectedSendPayloadHex = -join ($expectedSendBytes | ForEach-Object {
        $_.ToString('X2')
    })
    $expectedSendHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $expectedSendPayloadSHA256 = [BitConverter]::ToString(
            $expectedSendHasher.ComputeHash($expectedSendBytes)).Replace('-', '')
    } finally {
        $expectedSendHasher.Dispose()
    }
    $matching = @($records | Where-Object {
        [string]$_.Api -ceq "send" -and
        [string]$_.EvidenceLevel -ceq "Transmitted" -and
        [string]$_.PacketDirection -ceq "ClientToServer" -and
        [string]$_.CaptureSource -ceq "OptInX86Dll" -and
        [string]$_.PayloadHex -ceq $expectedSendPayloadHex -and
        $_.ProcessId -is [int] -and [int]$_.ProcessId -eq $gameProcess.Id -and
        $_.ThreadId -is [int] -and [int]$_.ThreadId -gt 0 -and
        $_.RequestedLength -is [int] -and [int]$_.RequestedLength -eq 208 -and
        $_.TransferredLength -is [int] -and [int]$_.TransferredLength -eq 208 -and
        $_.CapturedLength -is [int] -and [int]$_.CapturedLength -eq 208 -and
        $_.LastError -is [int] -and [int]$_.LastError -eq 0 -and
        $_.SensitivePayloadRedacted -is [bool] -and
        -not [bool]$_.SensitivePayloadRedacted -and
        $_.Plaintext -is [bool] -and -not [bool]$_.Plaintext -and
        [string]$_.SourceFrameId -cmatch '^injected-[0-9]+-[0-9]+$' -and
        [string]$_.HookInvocationId -cmatch '^hook-[0-9]+-[0-9]+$'
    })
    if ($matching.Count -ne 1) {
        $failures.Add("expected one transmitted send record, found $($matching.Count)")
    }
    $tracePath = Join-Path $traceDir "trace.bin"
    if (-not (Test-Path -LiteralPath $tracePath) -or (Get-Item -LiteralPath $tracePath).Length -le 24) {
        $failures.Add("binary trace did not contain a packet record")
    }

    $nativeSemanticWireSHA256 = ""
    $nativeSemanticWireVerificationSHA256 = ""
    $ultimateNativeWireReportSHA256 = ""
    $nativeSemanticWireVerification = $null
    $ultimateNativeWireReport = $null
    if (-not (Test-Path -LiteralPath $nativeSemanticWirePath)) {
        $failures.Add("exact DLL native semantic JSONL artifact was not written")
    } elseif (-not (Test-Path -LiteralPath $recoveryEngine)) {
        $failures.Add("final recovery EXE was unavailable for cross-runtime semantic verification: $recoveryEngine")
    } else {
        $nativeSemanticWireSHA256 = (Get-FileHash -LiteralPath $nativeSemanticWirePath -Algorithm SHA256).Hash
        $verifyArguments = @(
            "--internal-verify-native-semantic-wire",
            "--input", ('"' + $nativeSemanticWirePath + '"'),
            "--report", ('"' + $nativeSemanticWireVerificationPath + '"')
        )
        $verifyProcess = Start-Process -FilePath $recoveryEngine -ArgumentList $verifyArguments `
            -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
        if (-not $verifyProcess.WaitForExit(30000)) {
            $failures.Add("final EXE native semantic wire verifier timed out")
            Stop-Process -Id $verifyProcess.Id -Force -ErrorAction SilentlyContinue
        } elseif ($verifyProcess.ExitCode -ne 0) {
            $failures.Add("final EXE native semantic wire verifier exit code was $($verifyProcess.ExitCode)")
        }
        if (Test-Path -LiteralPath $nativeSemanticWireVerificationPath) {
            $nativeSemanticWireVerificationSHA256 = (Get-FileHash -LiteralPath `
                $nativeSemanticWireVerificationPath -Algorithm SHA256).Hash
            try {
                $nativeSemanticWireVerification = Get-Content -LiteralPath `
                    $nativeSemanticWireVerificationPath -Raw | ConvertFrom-Json
            } catch {
                $failures.Add("native semantic wire verification report is invalid JSON: $($_.Exception.Message)")
            }
        } else {
            $failures.Add("final EXE native semantic wire verification report was not written")
        }
        if ($null -eq $nativeSemanticWireVerification -or
            [string]$nativeSemanticWireVerification.schemaId -cne "God2NativeSemanticWireVerification" -or
            [int]$nativeSemanticWireVerification.schemaVersion -ne 1 -or
            -not [bool]$nativeSemanticWireVerification.passed -or
            [string]$nativeSemanticWireVerification.inputSHA256 -cne $nativeSemanticWireSHA256 -or
            [string]$nativeSemanticWireVerification.eventTypeDigest -cne $nativeTypeDigest -or
            [int]$nativeSemanticWireVerification.inputLineCount -ne 25 -or
            @($nativeSemanticWireVerification.rows).Count -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.readerAcceptedCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.dispatchCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.roundTripCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.corruptionRejectedCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.versionRejectedCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.sourceTokenBoundCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.fixturePayloadBoundCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.authorityFailClosedCount -ne 25 -or
            [int]$nativeSemanticWireVerification.totals.promotionPolicyCount -ne 25) {
            $failures.Add("final EXE did not accept and dispatch all 25 exact DLL wire records with matching hash/digest")
        }

        New-Item -ItemType Directory -Force -Path $ultimateNativeWireArtifacts | Out-Null
        $ultimateArguments = @(
            "--internal-ultimate-selftest",
            "--report", ('"' + $ultimateNativeWireReportPath + '"'),
            "--artifacts", ('"' + $ultimateNativeWireArtifacts + '"'),
            "--native-semantic-wire", ('"' + $nativeSemanticWirePath + '"')
        )
        $ultimateProcess = Start-Process -FilePath $recoveryEngine -ArgumentList $ultimateArguments `
            -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
        if (-not $ultimateProcess.WaitForExit(180000)) {
            $failures.Add("Ultimate native-wire-bound selftest timed out")
            Stop-Process -Id $ultimateProcess.Id -Force -ErrorAction SilentlyContinue
        } elseif ($ultimateProcess.ExitCode -ne 0) {
            $failures.Add("Ultimate native-wire-bound selftest exit code was $($ultimateProcess.ExitCode)")
        }
        if (Test-Path -LiteralPath $ultimateNativeWireReportPath) {
            $ultimateNativeWireReportSHA256 = (Get-FileHash -LiteralPath `
                $ultimateNativeWireReportPath -Algorithm SHA256).Hash
            try {
                $ultimateNativeWireReport = Get-Content -LiteralPath `
                    $ultimateNativeWireReportPath -Raw | ConvertFrom-Json
            } catch {
                $failures.Add("Ultimate native-wire-bound report is invalid JSON: $($_.Exception.Message)")
            }
        } else {
            $failures.Add("Ultimate native-wire-bound report was not written")
        }
        $ultimateMatrix = if ($null -ne $ultimateNativeWireReport) {
            $ultimateNativeWireReport.semanticEventMatrix
        } else { $null }
        if ($null -eq $ultimateMatrix -or
            -not [bool]$ultimateMatrix.nativeWireBinding.bound -or
            -not [bool]$ultimateMatrix.nativeWireBinding.verified -or
            [string]$ultimateMatrix.nativeWireBinding.inputSHA256 -cne $nativeSemanticWireSHA256 -or
            [string]$ultimateMatrix.nativeWireBinding.eventTypeDigest -cne $nativeTypeDigest -or
            [int]$ultimateMatrix.nativeWireBinding.inputLineCount -ne 25 -or
            [int]$ultimateMatrix.totals.nativeWireReaderAcceptedCount -ne 25 -or
            [int]$ultimateMatrix.totals.nativeWireDispatchCount -ne 25 -or
            @($ultimateMatrix.rows).Count -ne 25 -or
            @($ultimateMatrix.rows | Where-Object {
                -not $_.nativeWireReaderAccepted -or -not $_.nativeWireDispatchPassed
            }).Count -ne 0) {
            $failures.Add("Ultimate semanticEventMatrix was not hash-bound to the same 25 DLL wire records")
        }
    }

    $candidateMap = $null
    $candidateMapSHA256 = ""
    $candidateMapDuplicateNegativeCount = 0
    if (Test-Path -LiteralPath $deepProbeCandidateMapPath) {
        try {
            $candidateMapRaw = Read-StrictUtf8Text -Path $deepProbeCandidateMapPath
            if ($candidateMapRaw.IndexOf("`r") -ge 0 -or
                $candidateMapRaw.IndexOf([char]0) -ge 0 -or
                $candidateMapRaw.IndexOf("`n") -ne $candidateMapRaw.Length - 1) {
                throw "candidate map was not canonical one-line JSON plus LF"
            }
            $candidateMap = $candidateMapRaw | ConvertFrom-Json
            $candidateMapSHA256 = (Get-FileHash -LiteralPath $deepProbeCandidateMapPath `
                -Algorithm SHA256).Hash
        } catch {
            $failures.Add("deep-probe candidate map is not strict UTF-8 JSON: $($_.Exception.Message)")
        }
    } else {
        $failures.Add("deep-probe candidate map was not written")
    }
    $probeDomainNames = @(
        "Network","Parser","Serializer","Handler","Object","Allocation",
        "VTable","Factory","ManagerLookup","Registry","ResourceDecode",
        "Mutation","TaintSeed","FormulaOperand","Quest","Map","Portal",
        "NPC","Monster","Battle","Inventory","Item","Skill","Pet/Mount",
        "Snapshot"
    )
    $probeNames = @(
        "WinsockTransport","PacketDecode.FrameBoundary",
        "OutboundEnqueue.FrameBuilder","Battle.HandlerRecordLength",
        "ObjectResolver","AllocationProbe","VTableProbe","FactoryProbe",
        "ManagerLookupProbe","RegistryProbe","ResourceDecodeProbe",
        "MutationProbe","TaintSeedProbe","FormulaOperandProbe","QuestProbe",
        "MapProbe","PortalProbe","NPCProbe","MonsterProbe","BattleProbe",
        "InventoryProbe","ItemProbe","SkillProbe","PetMountProbe",
        "SnapshotProbe"
    )
    $candidateDiscoveryContracts = @(
        @("Parser","StableObjectIdentity;AllocationOrLookupWitness","TypedFieldToObjectOrMutation"),
        @("Parser","AllocationSize;ConstructorAndDestructorPath","AllocationToStableObjectIdentity"),
        @("Parser","ExecutableVTableEntries;RepeatedInstanceFamily","VTableToStableObjectIdentity"),
        @("Parser","FactoryReturnObject;ConstructorPath","FactoryToStableObjectIdentity"),
        @("Parser","StableIdToPointerMapping;LookupConsumer","TypedIdToStableObjectIdentity"),
        @("Parser","ReadOnlyEnumeration;StableRegistryIdentity","RegistryToTypedConsumer"),
        @("Parser","RelativeResourceIdentity;DecodedBufferHash","DecodeToDeserializerToObject"),
        @("Handler","StableObjectIdentity;BeforeInputAfter","TriggerToWriterToVerifiedConsumer"),
        @("Parser","TypedBoundedSourceRange;ValueToken","SourceToOperationToConsumer"),
        @("Handler","TypedOperandsAndResult;RepeatedObservation","OperandToFormulaResultToMutation"),
        @("Parser","StableQuestIdentity;ObjectiveOrState","TypedSourceToQuestMutation"),
        @("Parser","StableMapIdentity;CoordinateContext","TypedSourceToMapConsumer"),
        @("Parser","StablePortalIdentity;SourceAndTargetMap","PortalConditionToMapTransition"),
        @("Parser","StableNpcIdentity;TemplateOrSpawnContext","TypedSourceToNpcConsumer"),
        @("Handler","StableMonsterIdentity;TemplateRuntimeSeparation","TypedSourceToMonsterMutation"),
        @("Handler","StableBattleIdentity;ParticipantContext","HandlerToBattleStateMutation"),
        @("Serializer","StableInventoryIdentity;TransactionBeforeAfter","ActionToSerializerAndMutation"),
        @("Parser","StableItemIdentity;TemplateRuntimeSeparation","TypedSourceToItemConsumer"),
        @("Handler","StableSkillIdentity;OperandOrCooldownContext","ActionToSkillMutation"),
        @("Handler","StablePetOrMountIdentity;StateContext","TypedSourceToPetOrMountMutation"),
        @("Parser","BoundedObjectSet;StableObjectAndEdgeTokens","SnapshotEdgeToVerifiedObject")
    )
    $candidateDomains = if ($null -ne $candidateMap) { @($candidateMap.Domains) } else { @() }
    $candidateObjectCount = 0
    if ($candidateDomains.Count -eq 25) {
        for ($index = 4; $index -lt 25; $index++) {
            $candidateObjectCount += @($candidateDomains[$index].Candidates).Count
        }
    }
    $candidateMapPropertyCountTable = @{
        SchemaVersion=1; ClientSHA256=1; ClientSha256Expected=1
        Architecture=1; DiscoveryPolicy=1; CandidateVerificationEngine=1
        UnconfirmedProbePolicy=1; CandidateSchemaVersion=1
        TargetIdentityVerified=1; IdentityProfile=1
        EngineBounds=1; DomainCount=1; Domains=1
        ScanRadiusBytes=1; MaximumCandidatesPerDomain=1; SignatureBytes=1
        WritableMemoryScanned=1; Domain=25; Probe=25; Status=25
        Authority=25; Candidates=25; CandidateCount=25; VerificationStatus=25
        DiscoveryPlan=21; VerificationContract=21; InstallationPolicy=21
        SafeNextAction=21; SeedDomain=21; Strategy=21
        TypedEvidenceRequired=21; CausalEvidenceRequired=21
        ExactTargetIdentity=21; ExecutableSection=21; ExactCandidateBytes=21
        CallingConventionVerified=21; TypedRuntimeEvidence=21
        RepeatedCausalObservation=21; ContradictionsResolved=21
        StableObjectOrContext=21; VerifiedConsumerOrMutation=21
        SensitiveMaskContract=21; ArgumentContractVerified=21
        ReturnValueLifetimeVerified=21; ThreadContextVerified=21
        ReentrancyRiskVerified=21; PromotionGateCount=21
        AbiSafetyGateCount=21; TotalGateCount=21
        callsiteRva=1; callsiteRvas=1; targetRva=3
        verification=3; signature=1
        DiscoveryProvenance=$candidateObjectCount
        SignatureMask=$candidateObjectCount
        ModuleSection=$candidateObjectCount
        CallingConventionState=$candidateObjectCount
        ArgumentContractState=$candidateObjectCount
        ReturnValueLifetimeState=$candidateObjectCount
        ThreadContextState=$candidateObjectCount
        ReentrancyRiskState=$candidateObjectCount; Risk=$candidateObjectCount
        VerificationState=$candidateObjectCount
        RejectionReason=$candidateObjectCount
    }
    $candidateMapPropertyCounts =
        [Collections.Generic.Dictionary[string,int]]::new(
            [StringComparer]::Ordinal)
    foreach ($entry in $candidateMapPropertyCountTable.GetEnumerator()) {
        $candidateMapPropertyCounts.Add([string]$entry.Key,[int]$entry.Value)
    }
    $candidateMapPropertyCounts.Add("CallsiteRva",$candidateObjectCount)
    $candidateMapPropertyCounts.Add("TargetRva",$candidateObjectCount)
    $candidateMapPropertyCounts.Add("Signature",$candidateObjectCount)
    if ($null -eq $candidateMap -or
        -not (Test-ExactJsonPropertyOccurrences -Raw $candidateMapRaw `
            -ExpectedCounts $candidateMapPropertyCounts)) {
        $failures.Add("deep-probe candidate map contained missing or duplicate JSON properties")
    } else {
        $candidateMapDuplicateNegatives = @(
            $candidateMapRaw.Replace('"TargetIdentityVerified":true',
                '"TargetIdentityVerified":false,"TargetIdentityVerified":true'),
            $candidateMapRaw.Replace('"WritableMemoryScanned":false',
                '"WritableMemoryScanned":true,"WritableMemoryScanned":false'),
            $candidateMapRaw.Replace('"Status":"Active"',
                '"Status":"CurrentTargetIdentityBlocked","Status":"Active"')
        )
        foreach ($negative in $candidateMapDuplicateNegatives) {
            if (-not (Test-ExactJsonPropertyOccurrences -Raw $negative `
                    -ExpectedCounts $candidateMapPropertyCounts)) {
                $candidateMapDuplicateNegativeCount++
            }
        }
        if ($candidateMapDuplicateNegativeCount -ne 3) {
            $failures.Add("candidate map duplicate-key negatives were accepted")
        }
    }
    $candidateMapRootNames = if ($null -ne $candidateMap) {
        @($candidateMap.PSObject.Properties.Name | Sort-Object)
    } else { @() }
    $expectedCandidateMapRootNames = @(
        "Architecture","CandidateSchemaVersion","CandidateVerificationEngine",
        "ClientSHA256","ClientSha256Expected","DiscoveryPolicy","DomainCount",
        "Domains","EngineBounds","IdentityProfile","SchemaVersion","TargetIdentityVerified",
        "UnconfirmedProbePolicy"
    ) | Sort-Object
    if ($null -eq $candidateMap -or
        ($candidateMapRootNames -join "`n") -cne
            ($expectedCandidateMapRootNames -join "`n") -or
        [string]$candidateMap.SchemaVersion -cne "god2-deep-probe-candidate-map-v2" -or
        [string]$candidateMap.CandidateSchemaVersion -cne
            "god2-deep-probe-candidate-v1" -or
        [string]$candidateMap.Architecture -cne "x86" -or
        [string]$candidateMap.IdentityProfile -cne "TestOnlyExactFixture" -or
        [string]$candidateMap.DiscoveryPolicy -cne
            "ExecutableCodeAndExactSignaturesOnly;NoWritableMemoryScan;NoSensitiveValueLogging" -or
        [string]$candidateMap.CandidateVerificationEngine -cne
            "BoundedExecutableCallGraphPlusExactCandidateBytesPlusPromotionHardGate" -or
        [string]$candidateMap.UnconfirmedProbePolicy -cne
            "EvidenceBlockedUnconfirmedProbe" -or
        -not (Test-ExactObjectProperties $candidateMap.EngineBounds @(
            "MaximumCandidatesPerDomain","ScanRadiusBytes","SignatureBytes",
            "WritableMemoryScanned")) -or
        -not ($candidateMap.EngineBounds.ScanRadiusBytes -is [int]) -or
        [int]$candidateMap.EngineBounds.ScanRadiusBytes -ne 256 -or
        -not ($candidateMap.EngineBounds.MaximumCandidatesPerDomain -is [int]) -or
        [int]$candidateMap.EngineBounds.MaximumCandidatesPerDomain -ne 4 -or
        -not ($candidateMap.EngineBounds.SignatureBytes -is [int]) -or
        [int]$candidateMap.EngineBounds.SignatureBytes -ne 8 -or
        -not ($candidateMap.EngineBounds.WritableMemoryScanned -is [bool]) -or
        [bool]$candidateMap.EngineBounds.WritableMemoryScanned -or
        [string]$candidateMap.ClientSHA256 -cne $testClientSHA256 -or
        [string]$candidateMap.ClientSha256Expected -cne $testClientSHA256 -or
        -not ($candidateMap.TargetIdentityVerified -is [bool]) -or
        -not [bool]$candidateMap.TargetIdentityVerified -or
        -not ($candidateMap.DomainCount -is [int]) -or
        [int]$candidateMap.DomainCount -ne 25 -or $candidateDomains.Count -ne 25) {
        $failures.Add("deep-probe candidate map root/schema/type contract failed")
    }
    if ($candidateDomains.Count -eq 25) {
        $seenDomainProbe = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        for ($domainIndex = 0; $domainIndex -lt 25; $domainIndex++) {
            $domainRow = $candidateDomains[$domainIndex]
            $candidateRows = @($domainRow.Candidates)
            $identityKey = [string]$domainRow.Domain + "`n" +
                [string]$domainRow.Probe
            $rowValid = $seenDomainProbe.Add($identityKey) -and
                [string]$domainRow.Domain -ceq $probeDomainNames[$domainIndex] -and
                [string]$domainRow.Probe -ceq $probeNames[$domainIndex] -and
                $domainRow.CandidateCount -is [int] -and
                [int]$domainRow.CandidateCount -eq $candidateRows.Count -and
                [int]$domainRow.CandidateCount -ge 0 -and
                [int]$domainRow.CandidateCount -le 4
            if ($domainIndex -eq 0) {
                $rowValid = $rowValid -and
                    (Test-ExactObjectProperties $domainRow @(
                        "Authority","CandidateCount","Candidates","Domain",
                        "Probe","Status","VerificationStatus")) -and
                    [string]$domainRow.Status -ceq "Active" -and
                    [string]$domainRow.Authority -ceq "VERIFIED" -and
                    [string]$domainRow.VerificationStatus -ceq "PASS" -and
                    [int]$domainRow.CandidateCount -eq 4 -and
                    (@($candidateRows) -join "`n") -ceq
                        (@("send","WSASend","recv","WSARecv") -join "`n")
            } elseif ($domainIndex -lt 4) {
                $rowValid = $rowValid -and
                    (Test-ExactObjectProperties $domainRow @(
                        "Authority","CandidateCount","Candidates","Domain",
                        "Probe","Status","VerificationStatus")) -and
                    [string]$domainRow.Status -ceq
                        "ConfiguredInactiveNetworkOnly" -and
                    [string]$domainRow.Authority -ceq "VERIFIED" -and
                    [string]$domainRow.VerificationStatus -ceq
                        "NotApplicableConfiguredInactiveNetworkOnly" -and
                    [int]$domainRow.CandidateCount -eq 1
                if ($candidateRows.Count -eq 1) {
                    $expectedConfirmedCandidateProperties = if ($domainIndex -eq 1) {
                        @("callsiteRva","targetRva","verification")
                    } elseif ($domainIndex -eq 2) {
                        @("signature","targetRva","verification")
                    } else {
                        @("callsiteRvas","targetRva","verification")
                    }
                    $rowValid = $rowValid -and
                        (Test-ExactObjectProperties $candidateRows[0] `
                            $expectedConfirmedCandidateProperties)
                    if ($domainIndex -eq 1) {
                        $rowValid = $rowValid -and
                            [string]$candidateRows[0].callsiteRva -ceq "0x00078A48" -and
                            [string]$candidateRows[0].targetRva -ceq "0x00078D70" -and
                            [string]$candidateRows[0].verification -ceq "ExactCallTarget"
                    } elseif ($domainIndex -eq 2) {
                        $rowValid = $rowValid -and
                            [string]$candidateRows[0].targetRva -ceq "0x0007FC10" -and
                            [string]$candidateRows[0].signature -ceq "55 8B EC 56 57" -and
                            [string]$candidateRows[0].verification -ceq "ExactInstructionBytes"
                    } else {
                        $rowValid = $rowValid -and
                            (@($candidateRows[0].callsiteRvas) -join "`n") -ceq
                                (@("0x00147096","0x001470AE") -join "`n") -and
                            [string]$candidateRows[0].targetRva -ceq "0x0007F940" -and
                            [string]$candidateRows[0].verification -ceq "BothExactCallTargets"
                    }
                }
            } else {
                $discoveryContract = $candidateDiscoveryContracts[$domainIndex - 4]
                $expectedCandidateVerificationStatus = if ($candidateRows.Count -eq 0) {
                    "EvidenceBlockedNoExecutableCallCandidate"
                } else { "EvidenceBlockedCandidateRequiresTypedRuntimeVerification" }
                $rowValid = $rowValid -and
                    (Test-ExactObjectProperties $domainRow @(
                        "Authority","CandidateCount","Candidates","DiscoveryPlan",
                        "Domain","InstallationPolicy","Probe","SafeNextAction",
                        "Status","VerificationContract","VerificationStatus")) -and
                    (Test-ExactObjectProperties $domainRow.DiscoveryPlan @(
                        "CausalEvidenceRequired","SeedDomain","Strategy",
                        "TypedEvidenceRequired")) -and
                    (Test-ExactObjectProperties $domainRow.VerificationContract @(
                        "AbiSafetyGateCount","ArgumentContractVerified",
                        "CallingConventionVerified","ContradictionsResolved",
                        "ExactCandidateBytes","ExactTargetIdentity",
                        "ExecutableSection","PromotionGateCount",
                        "ReentrancyRiskVerified","RepeatedCausalObservation",
                        "ReturnValueLifetimeVerified","SensitiveMaskContract",
                        "StableObjectOrContext","ThreadContextVerified",
                        "TotalGateCount","TypedRuntimeEvidence",
                        "VerifiedConsumerOrMutation")) -and
                    [string]$domainRow.DiscoveryPlan.SeedDomain -ceq
                        [string]$discoveryContract[0] -and
                    [string]$domainRow.DiscoveryPlan.Strategy -ceq
                        "BoundedDirectCallTargetsFromVerifiedSeed" -and
                    [string]$domainRow.DiscoveryPlan.TypedEvidenceRequired -ceq
                        [string]$discoveryContract[1] -and
                    [string]$domainRow.DiscoveryPlan.CausalEvidenceRequired -ceq
                        [string]$discoveryContract[2] -and
                    [string]$domainRow.Status -ceq
                        "EvidenceBlockedUnconfirmedProbe" -and
                    [string]$domainRow.Authority -ceq "UNKNOWN" -and
                    [string]$domainRow.VerificationStatus -ceq
                        $expectedCandidateVerificationStatus -and
                    [string]$domainRow.InstallationPolicy -ceq
                        "NeverInstallUntilAllVerificationGatesPass" -and
                    [string]$domainRow.SafeNextAction -ceq
                        "CollectExactBuildRepeatedTypedRuntimeEvidenceAndVerifyCallingConvention"
                $candidateIdentitySet = [Collections.Generic.HashSet[string]]::new(
                    [StringComparer]::Ordinal)
                foreach ($candidateRow in $candidateRows) {
                    $candidateIdentity = [string]$candidateRow.CallsiteRva + "`n" +
                        [string]$candidateRow.TargetRva
                    $rowValid = $rowValid -and
                        $candidateIdentitySet.Add($candidateIdentity) -and
                        (Test-ExactObjectProperties $candidateRow @(
                            "ArgumentContractState","CallingConventionState",
                            "CallsiteRva","DiscoveryProvenance","ModuleSection",
                            "ReentrancyRiskState","RejectionReason",
                            "ReturnValueLifetimeState","Risk","Signature",
                            "SignatureMask","TargetRva","ThreadContextState",
                            "VerificationState")) -and
                        [string]$candidateRow.CallsiteRva -cmatch
                            '^0x[0-9A-F]{8}$' -and
                        [string]$candidateRow.TargetRva -cmatch
                            '^0x[0-9A-F]{8}$' -and
                        [string]$candidateRow.DiscoveryProvenance -ceq
                            ("VerifiedSeed:{0};BoundedExecutableDirectCall" -f
                                [string]$discoveryContract[0]) -and
                        [string]$candidateRow.Signature -cmatch
                            '^[0-9A-F]{2}( [0-9A-F]{2}){7}$' -and
                        [string]$candidateRow.SignatureMask -ceq
                            'FF FF FF FF FF FF FF FF' -and
                        [string]$candidateRow.ModuleSection -cmatch
                            '^(?:[A-Za-z0-9._$]{1,8}|ExecutableSection)$' -and
                        [string]$candidateRow.CallingConventionState -ceq "UNKNOWN" -and
                        [string]$candidateRow.ArgumentContractState -ceq "UNKNOWN" -and
                        [string]$candidateRow.ReturnValueLifetimeState -ceq "UNKNOWN" -and
                        [string]$candidateRow.ThreadContextState -ceq "UNKNOWN" -and
                        [string]$candidateRow.ReentrancyRiskState -ceq "UNKNOWN" -and
                        [string]$candidateRow.Risk -ceq "High" -and
                        [string]$candidateRow.VerificationState -ceq "CandidateOnly" -and
                        [string]$candidateRow.RejectionReason -ceq
                            "InsufficientTypedDomainCorrelationAndCausalEvidence"
                }
            }
            if (-not $rowValid) {
                $failures.Add("deep-probe candidate map canonical row mismatch at index $domainIndex")
            }
        }
    }

    $candidateGateDefinitions = @(
        @("ExactTargetIdentity","Promotion"),
        @("ExecutableSection","Promotion"),
        @("ExactCandidateBytes","Promotion"),
        @("CallingConventionVerified","Promotion"),
        @("TypedRuntimeEvidence","Promotion"),
        @("RepeatedCausalObservation","Promotion"),
        @("ContradictionsResolved","Promotion"),
        @("StableObjectOrContext","Promotion"),
        @("VerifiedConsumerOrMutation","Promotion"),
        @("SensitiveMaskContract","Promotion"),
        @("ArgumentContractVerified","AbiSafety"),
        @("ReturnValueLifetimeVerified","AbiSafety"),
        @("ThreadContextVerified","AbiSafety"),
        @("ReentrancyRiskVerified","AbiSafety")
    )
    $candidateOnlyRows = if ($candidateDomains.Count -eq 25) {
        @($candidateDomains | Select-Object -Skip 4)
    } else { @() }
    $candidateGateResults = [Collections.Generic.List[object]]::new()
    foreach ($definition in $candidateGateDefinitions) {
        $gateName = [string]$definition[0]
        $allPassed = $candidateOnlyRows.Count -eq 21
        $allRejected = $candidateOnlyRows.Count -eq 21
        foreach ($domainRow in $candidateOnlyRows) {
            $contract = $domainRow.VerificationContract
            $property = if ($null -ne $contract) {
                $contract.PSObject.Properties[$gateName]
            } else { $null }
            if ($null -eq $property -or -not ($property.Value -is [bool])) {
                $allPassed = $false
                $allRejected = $false
                break
            }
            if ([bool]$property.Value) { $allRejected = $false }
            else { $allPassed = $false }
        }
        $candidateGateResults.Add([pscustomobject]@{
            Gate = $gateName
            Category = [string]$definition[1]
            PassedOnAllCandidateDomains = $allPassed
            RejectedOnAllCandidateDomains = $allRejected
        })
    }
    $candidatePassedGateCount = @($candidateGateResults | Where-Object {
        $_.PassedOnAllCandidateDomains
    }).Count
    $candidateRejectedGateCount = @($candidateGateResults | Where-Object {
        $_.RejectedOnAllCandidateDomains
    }).Count
    $candidateGateMatrixValid = $candidateGateResults.Count -eq 14 -and
        $candidatePassedGateCount -eq 1 -and
        $candidateRejectedGateCount -eq 13 -and
        [string]$candidateGateResults[0].Gate -ceq "ExactTargetIdentity" -and
        [bool]$candidateGateResults[0].PassedOnAllCandidateDomains -and
        -not [bool]$candidateGateResults[0].RejectedOnAllCandidateDomains
    if ($candidateOnlyRows.Count -ne 21 -or
        @($candidateOnlyRows | Where-Object {
            [string]$_.Status -cne "EvidenceBlockedUnconfirmedProbe" -or
            [string]$_.Authority -cne "UNKNOWN" -or
            [string]$_.InstallationPolicy -cne
                "NeverInstallUntilAllVerificationGatesPass" -or
            -not ($_.VerificationContract.PromotionGateCount -is [int]) -or
            [int]$_.VerificationContract.PromotionGateCount -ne 10 -or
            -not ($_.VerificationContract.AbiSafetyGateCount -is [int]) -or
            [int]$_.VerificationContract.AbiSafetyGateCount -ne 4 -or
            -not ($_.VerificationContract.TotalGateCount -is [int]) -or
            [int]$_.VerificationContract.TotalGateCount -ne 14
        }).Count -ne 0 -or -not $candidateGateMatrixValid) {
        $failures.Add("21 candidate-only domains did not preserve the exact 1 passed identity + 13 rejected activation gates")
    }

    $probeDomainWireRows = @($sharedAcceptedWireRows | Where-Object {
        [string]$_.Parsed.EventType -ceq "ProbeDiagnostic" -and
        $null -ne $_.Parsed.Payload -and
        $null -ne $_.Parsed.Payload.ContractStatus
    })
    $probeDomainWireSHA256 = ""
    $probeDomainWireDuplicateNegativeCount = 0
    $probeDiagnosticEvents = @()
    if ($probeDomainWireRows.Count -eq 25) {
        $probeDomainWireContent = (@($probeDomainWireRows.Wire) -join "`n") + "`n"
        Write-Utf8NoBomAtomic -Path $probeDomainWirePath `
            -Content $probeDomainWireContent
        $probeDomainWireRaw = Read-StrictUtf8Text -Path $probeDomainWirePath
        $probeDomainWireSHA256 = (Get-FileHash -LiteralPath $probeDomainWirePath `
            -Algorithm SHA256).Hash
        if ($probeDomainWireRaw -cne $probeDomainWireContent -or
            $probeDomainWireRaw.IndexOf("`r") -ge 0 -or
            $probeDomainWireRaw.IndexOf([char]0) -ge 0) {
            $failures.Add("immutable probe-domain JSONL did not byte-match the 25 consumed wires")
        } else {
            $probeDomainWireLines = @($probeDomainWireRaw.Split("`n") |
                Where-Object { $_.Length -gt 0 })
            $probeDomainPropertyCounts = @{
                SchemaId=1; SchemaVersion=1; EventType=1; EventId=1
                Sequence=1; Timestamp=1; ThreadId=1; ProcessId=1
                SessionId=1; ClientBuildId=1; ModuleId=1; RVA=1
                CallsiteRVA=1; ParentEventId=1; ContextId=1; ActionId=1
                ObjectToken=1; ValueToken=1; SourceToken=1; AuthorityHint=1
                SensitiveMaskStatus=1; Payload=1; Domain=1; Probe=1
                Status=1; Reason=1; ContractStatus=1
                ActivationAllowedByContract=1; RuntimeDiagnosticObserved=1
                RuntimeActivationObserved=1; TargetIdentityVerified=1
                FaultIsolation=1
            }
            foreach ($wireLine in $probeDomainWireLines) {
                try {
                    if (-not (Test-ExactJsonPropertyOccurrences -Raw $wireLine `
                            -ExpectedCounts $probeDomainPropertyCounts)) {
                        throw "missing or duplicate semantic properties"
                    }
                    $probeDiagnosticEvents += $wireLine | ConvertFrom-Json
                }
                catch { $failures.Add("probe-domain JSONL contained invalid JSON") }
            }
            if ($probeDomainWireLines.Count -eq 25) {
                $probeDomainDuplicateNegatives = @(
                    $probeDomainWireLines[0].Replace(
                        ('"ProcessId":{0}' -f $gameProcess.Id),
                        ('"ProcessId":1,"ProcessId":{0}' -f $gameProcess.Id)),
                    $probeDomainWireLines[0].Replace(
                        '"SourceToken":"WinsockTransport"',
                        '"SourceToken":"spoof","SourceToken":"WinsockTransport"'),
                    $probeDomainWireLines[0].Replace(
                        '"Domain":"Network"',
                        '"Domain":"Snapshot","Domain":"Network"')
                )
                foreach ($negative in $probeDomainDuplicateNegatives) {
                    if (-not (Test-ExactJsonPropertyOccurrences -Raw $negative `
                            -ExpectedCounts $probeDomainPropertyCounts)) {
                        $probeDomainWireDuplicateNegativeCount++
                    }
                }
            }
            if ($probeDomainWireDuplicateNegativeCount -ne 3) {
                $failures.Add("probe-domain duplicate-key negatives were accepted")
            }
        }
    } else {
        $failures.Add("shared consumer did not receive exactly 25 domain diagnostic wires")
    }
    $probeDomainResults = [Collections.Generic.List[object]]::new()
    [int64]$previousProbeSemanticSequence = 0
    [uint32]$previousProbeSlotSequence = 0
    for ($domainIndex = 0; $domainIndex -lt $probeDomainNames.Count; $domainIndex++) {
        $domainName = $probeDomainNames[$domainIndex]
        if ($domainIndex -ge $probeDiagnosticEvents.Count -or
            $domainIndex -ge $probeDomainWireRows.Count) {
            $failures.Add("runtime probe diagnostic was missing at index $domainIndex")
            continue
        }
        $event = $probeDiagnosticEvents[$domainIndex]
        $wireRow = $probeDomainWireRows[$domainIndex]
        $payload = $event.Payload
        $expectedContract = if ($domainIndex -lt 4) {
            "ConfirmedContract"
        } else { "CandidateOnlyBlockedContract" }
        $expectedStatus = if ($domainIndex -eq 0) {
            "Active"
        } elseif ($domainIndex -lt 4) {
            "ConfiguredInactiveNetworkOnly"
        } else { "EvidenceBlockedUnconfirmedProbe" }
        $expectedRuntimeActive = $domainIndex -eq 0
        $typedFlags = $payload.ActivationAllowedByContract -is [bool] -and
            $payload.RuntimeDiagnosticObserved -is [bool] -and
            $payload.RuntimeActivationObserved -is [bool] -and
            $payload.TargetIdentityVerified -is [bool]
        if (-not $typedFlags -or
            -not (Test-ExactObjectProperties $event @(
                "ActionId","AuthorityHint","CallsiteRVA","ClientBuildId",
                "ContextId","EventId","EventType","ModuleId","ObjectToken",
                "ParentEventId","Payload","ProcessId","RVA","SchemaId",
                "SchemaVersion","SensitiveMaskStatus","Sequence","SessionId",
                "SourceToken","ThreadId","Timestamp","ValueToken")) -or
            -not (Test-ExactObjectProperties $payload @(
                "ActivationAllowedByContract","ContractStatus","Domain",
                "FaultIsolation","Probe","Reason",
                "RuntimeActivationObserved","RuntimeDiagnosticObserved","Status",
                "TargetIdentityVerified")) -or
            [string]$payload.Domain -cne $domainName -or
            [string]$payload.Probe -cne $probeNames[$domainIndex] -or
            [string]$event.SchemaId -cne "God2SemanticEvent" -or
            -not ($event.SchemaVersion -is [int]) -or
            [int]$event.SchemaVersion -ne 2 -or
            [string]$event.EventType -cne "ProbeDiagnostic" -or
            -not ($event.Sequence -is [int] -or $event.Sequence -is [long]) -or
            [int64]$event.Sequence -le 0 -or
            [int64]$event.Sequence -le $previousProbeSemanticSequence -or
            [string]$event.EventId -cne
                ("PD-{0}-{1}" -f $gameProcess.Id,[int64]$event.Sequence) -or
            -not ($event.ProcessId -is [int]) -or
            [int]$event.ProcessId -ne $gameProcess.Id -or
            [string]$event.ClientBuildId -cne $testClientSHA256 -or
            [string]$event.ModuleId -cne "God2_opt.exe" -or
            -not ($event.Timestamp -is [int] -or $event.Timestamp -is [long]) -or
            [int64]$event.Timestamp -le 0 -or
            -not ($event.ThreadId -is [int]) -or [int]$event.ThreadId -le 0 -or
            $null -ne $event.RVA -or $null -ne $event.CallsiteRVA -or
            $null -ne $event.ParentEventId -or $null -ne $event.ContextId -or
            $null -ne $event.ActionId -or $null -ne $event.ObjectToken -or
            $null -ne $event.ValueToken -or
            [string]$event.SessionId -cne
                "product-injector-shared-transport-selftest" -or
            [string]$event.SourceToken -cne $probeNames[$domainIndex] -or
            [string]$event.AuthorityHint -cne
                $(if ($domainIndex -lt 4) { "VERIFIED" } else { "UNKNOWN" }) -or
            [string]$event.SensitiveMaskStatus -cne "NotSensitive" -or
            -not ($wireRow.Domain -is [uint32] -or $wireRow.Domain -is [int]) -or
            [uint32]$wireRow.Domain -ne [uint32]$domainIndex -or
            -not ($wireRow.Sequence -is [uint32] -or $wireRow.Sequence -is [int]) -or
            [uint32]$wireRow.Sequence -eq 0 -or
            [uint32]$wireRow.Sequence -le $previousProbeSlotSequence -or
            [uint32]$wireRow.Priority -ne 3 -or
            [string]$wireRow.WireSHA256 -cnotmatch '^[A-F0-9]{64}$' -or
            [string]$wireRow.PayloadSHA256 -cnotmatch '^[A-F0-9]{64}$' -or
            [string]$payload.ContractStatus -cne $expectedContract -or
            [string]$payload.Status -cne $expectedStatus -or
            [string]$payload.Reason -cne $(if ($domainIndex -eq 0) {
                "VerifiedProbeInstalled"
            } elseif ($domainIndex -lt 4) {
                "ExplicitNetworkOnlyConfiguration"
            } else { "NoVerifiedTargetRva" }) -or
            [string]$payload.FaultIsolation -cne "PerDomain" -or
            [bool]$payload.ActivationAllowedByContract -ne ($domainIndex -lt 4) -or
            -not [bool]$payload.RuntimeDiagnosticObserved -or
            [bool]$payload.RuntimeActivationObserved -ne $expectedRuntimeActive -or
            -not [bool]$payload.TargetIdentityVerified) {
            $failures.Add("runtime probe diagnostic contract mismatch for $domainName")
        }
        $previousProbeSemanticSequence = [int64]$event.Sequence
        $previousProbeSlotSequence = [uint32]$wireRow.Sequence
        $probeDomainResults.Add([pscustomobject]@{
            Index = $domainIndex
            Domain = [string]$payload.Domain
            Probe = [string]$payload.Probe
            Status = [string]$payload.Status
            SlotSequence = [uint32]$wireRow.Sequence
            SlotDomain = [uint32]$wireRow.Domain
            SlotPriority = [uint32]$wireRow.Priority
            WireSHA256 = [string]$wireRow.WireSHA256
            PayloadSHA256 = [string]$wireRow.PayloadSHA256
            EventId = [string]$event.EventId
            ProcessId = [uint32]$event.ProcessId
            ClientBuildId = [string]$event.ClientBuildId
            SessionId = [string]$event.SessionId
            SourceToken = [string]$event.SourceToken
            AuthorityHint = [string]$event.AuthorityHint
            SensitiveMaskStatus = [string]$event.SensitiveMaskStatus
            ContractStatus = [string]$payload.ContractStatus
            ActivationAllowedByContract = [bool]$payload.ActivationAllowedByContract
            RuntimeDiagnosticObserved = [bool]$payload.RuntimeDiagnosticObserved
            RuntimeActivationObserved = [bool]$payload.RuntimeActivationObserved
            TargetIdentityVerified = [bool]$payload.TargetIdentityVerified
        })
    }
    $confirmedContractDomainCount = @($probeDomainResults | Where-Object {
        $_.ContractStatus -ceq "ConfirmedContract"
    }).Count
    $candidateOnlyBlockedDomainCount = @($probeDomainResults | Where-Object {
        $_.ContractStatus -ceq "CandidateOnlyBlockedContract"
    }).Count
    if ($probeDomainResults.Count -ne 25 -or $confirmedContractDomainCount -ne 4 -or
        $candidateOnlyBlockedDomainCount -ne 21) {
        $failures.Add("runtime probe domain matrix was not exact 4 confirmed + 21 candidate-only")
    }
    if ($candidateDomains.Count -eq 25 -and $probeDomainResults.Count -eq 25) {
        for ($domainIndex = 0; $domainIndex -lt 25; $domainIndex++) {
            if ([string]$candidateDomains[$domainIndex].Domain -cne
                    [string]$probeDomainResults[$domainIndex].Domain -or
                [string]$candidateDomains[$domainIndex].Probe -cne
                    [string]$probeDomainResults[$domainIndex].Probe -or
                [string]$candidateDomains[$domainIndex].Status -cne
                    [string]$probeDomainResults[$domainIndex].Status) {
                $failures.Add("candidate map and shared domain ledger diverged at index $domainIndex")
            }
        }
    }

    $sharedProducerProcessId = [uint32]$sharedAccessor.ReadUInt32(80L)
    $sharedConsumerProcessId = [uint32]$PID
    $nativeProcessIdValue = if ($null -ne $nativeReport) {
        ConvertTo-ExactUInt32OrNull $nativeReport.ProcessId
    } else { $null }
    $nativeProcessId = if ($null -ne $nativeProcessIdValue) {
        [uint32]$nativeProcessIdValue
    } else { [uint32]0 }
    $nativeWireProcessIds = @($nativeWireRows | ForEach-Object {
        $value = ConvertTo-ExactUInt32OrNull $_.Parsed.ProcessId
        if ($null -ne $value) { [uint32]$value } else { [uint32]0 }
    })
    $testCrossProcessBinding = {
        param([uint32]$TargetPid, [uint32]$ProducerPid, [uint32]$ConsumerPid,
              [uint32]$NativePid, [uint32[]]$WirePids)
        return $TargetPid -ne 0 -and $ProducerPid -eq $TargetPid -and
            $NativePid -eq $ProducerPid -and $ConsumerPid -ne $ProducerPid -and
            $WirePids.Count -eq 25 -and
            @($WirePids | Where-Object { $_ -ne $ProducerPid }).Count -eq 0
    }
    $sharedTransportCrossProcess = & $testCrossProcessBinding `
        ([uint32]$gameProcess.Id) $sharedProducerProcessId `
        $sharedConsumerProcessId $nativeProcessId $nativeWireProcessIds
    $crossProcessNegativeCount = 0
    if (-not (& $testCrossProcessBinding ([uint32]$gameProcess.Id) `
            $sharedProducerProcessId $sharedProducerProcessId $nativeProcessId `
            $nativeWireProcessIds)) { $crossProcessNegativeCount++ }
    $wrongWirePids = @($nativeWireProcessIds)
    if ($wrongWirePids.Count -eq 25) { $wrongWirePids[0] = $sharedConsumerProcessId }
    if (-not (& $testCrossProcessBinding ([uint32]$gameProcess.Id) `
            $sharedProducerProcessId $sharedConsumerProcessId $nativeProcessId `
            $wrongWirePids)) { $crossProcessNegativeCount++ }
    if (-not $sharedTransportCrossProcess -or $crossProcessNegativeCount -ne 2) {
        $failures.Add("shared transport cross-process PID/wire binding or negatives failed")
    }

    $summary = [pscustomobject]@{
        Status = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
        FailureCount = $failures.Count
        Failures = @($failures)
        AttachResultRecord = $attachRecord
        AttachResultPath = Convert-ToRunEvidencePath $attachEvidencePath
        AttachResultSHA256 = $attachResultSHA256
        DetachResultRecord = $detachRecord
        DetachResultPath = Convert-ToRunEvidencePath $detachEvidencePath
        DetachResultSHA256 = $detachResultSHA256
        TargetExecutable = "God2_opt.exe"
        TargetArchitecture = "x86"
        ReadinessHandshake = "God2TraceProbeWaitReady"
        StopHandshake = "God2TraceProbeStop"
        SafeUnloadHandshake = "God2TraceProbeCanUnload"
        ModuleUnloaded = $moduleUnloaded
        ModuleResidentInactive = $moduleResidentInactive
        ModuleSnapshotVerified = $moduleSnapshotVerified
        ModuleAbsent = $moduleAbsent
        UnloadSafe = $unloadSafe
        ExtraReferenceNegativeFirstFreeLibraryStillPresent = $extraReferenceNegativeFirstFreeLibraryStillPresent
        ExtraReferenceNegativeNotClaimedUnloaded = $extraReferenceNegativeNotClaimedUnloaded
        ExtraReferenceReleasedThenModuleAbsent = $extraReferenceReleasedThenModuleAbsent
        BlockingTargetAliveAtDetach = $blockingTargetAliveAtDetach
        BlockingAllReturnedAtDetach = $blockingAllReturnedAtDetach
        BlockingModuleAbsentBeforeRelease = $blockingModuleAbsentBeforeRelease
        BlockingReleaseIssued = $blockingReleaseIssued
        BlockingAllReturnedAfterRelease = $blockingAllReturnedAfterRelease
        BlockingDetachObservedUtc = $blockingDetachObservedUtc
        BlockingReleaseIssuedUtc = $blockingReleaseIssuedUtc
        BlockingAllReturnedObservedUtc = $blockingAllReturnedObservedUtc
        BlockingTargetStdoutPath = Convert-ToRunEvidencePath $targetStdoutPath
        BlockingTargetStdoutSHA256 = $targetStdoutSHA256
        BlockingPostUnloadResultPath = Convert-ToRunEvidencePath $blockingPostUnloadPath
        BlockingPostUnloadResultSHA256 = $blockingPostUnloadSHA256
        BlockingPostUnloadResult = $blockingPostUnloadRecord
        BlockingPostUnloadDuplicateNegativeCount = $blockingRawDuplicateNegativeCount
        MetadataPath = Convert-ToRunEvidencePath $metadata
        MetadataSHA256 = $metadataSHA256
        MetadataRecordCount = $records.Count
        MetadataSequenceContractPassed = $metadataSequenceContractPassed
        MatchingTransmittedSendCount = $matching.Count
        ExpectedTransmittedSendPayloadSHA256 = $expectedSendPayloadSHA256
        MetadataReadableWhileAttached = $metadataReadableWhileAttached
        SensitiveOutboundSinkRecordsPassed = $sensitiveOutboundSinkRecordsPassed
        SensitiveOutboundSinkRecords = @(
            @($sensitiveRetryDecoyRows) + @($sensitiveRetrySinkRows) +
            @($sensitiveWsaSendDecoyRows) + @($sensitiveWsaSendSinkRows))
        ResidualInjectorProcess = $injectorProcess -ne $null -and -not $injectorProcess.HasExited
        ResidualGameProcess = $gameProcess -ne $null -and -not $gameProcess.HasExited
        ProductionIdentityGateRecord = $identityGateRecord
        ProductionIdentityGatePath = Convert-ToRunEvidencePath $identityGateEvidencePath
        ProductionIdentityGateSHA256 = $identityGateResultSHA256
        StrictInjectorResultParserNegativeCount = $strictInjectorResultNegativeCount
        ProductionWrongIdentityRejectedBeforeInjection = $null -ne $identityGateRecord -and
            [string]$identityGateRecord.status -ceq "EVIDENCE_BLOCKED_BUILD_MISMATCH" -and
            -not [bool]$identityGateRecord.injectionAttempted -and
            -not [bool]$identityGateRecord.moduleWasEverLoaded
        WrongIdentityNeverPublishedDerivedFromInjectorNoLoad =
            $null -ne $identityGateRecord -and
            -not [bool]$identityGateRecord.injectionAttempted -and
            -not [bool]$identityGateRecord.moduleWasEverLoaded
        WrongIdentitySampledSnapshotSupportOnly = $true
        WrongIdentitySampledSnapshotPath = Convert-ToRunEvidencePath $wrongIdentityNoHookSnapshotPath
        WrongIdentitySampledSnapshotSHA256 = $wrongIdentityNoHookSnapshotSHA256
        WrongIdentitySampledSnapshotRecord = $wrongIdentityNoHookSnapshot
        WrongIdentitySnapshotDuplicateNegativeCount = $wrongIdentitySnapshotDuplicateNegativeCount
        PositiveLifecycleIdentityMode = "TestOnlyExactFixture"
        PositiveLifecycleIdentityVerified = $null -ne $attachRecord -and
            [bool]$attachRecord.targetIdentityVerified
        PositiveLifecycleClientVersion = "God2TraceSelfTestClient/1"
        PositiveLifecycleClientSHA256 = $testClientSHA256
        PositiveLifecycleProcessId = $gameProcess.Id
        PositiveLifecycleProcessCreationTime = [uint64]$testClientCreationTime
        PositiveLifecycleProbeSHA256 = $testProbeSHA256
        ProductionProbeSHA256 = $productionProbeSHA256
        TestInjectorSHA256 = $testInjectorSHA256
        ProductionInjectorSHA256 = $productionInjectorSHA256
        OfficialClientPositiveRuntimeObserved = $false
        SharedTransportVersion = 4
        SharedTransportProducerReady = $sharedProducerReady -eq 1
        SharedTransportProducerClosed = $sharedProducerClosed -eq 1
        SharedTransportConsumerReady = $sharedAccessor.ReadInt32(68L) -eq 1
        SharedTransportConsumerClosed = $sharedAccessor.ReadInt32(72L) -eq 1
        SharedTransportConsumerFailure = $sharedAccessor.ReadInt32(76L)
        SharedTransportConsumerDrainVerified = $null -ne $nativeReport -and
            [bool]$nativeReport.SharedTransportConsumerDrainVerified -and
            $sharedProducerClosed -eq 1 -and
            $sharedAccessor.ReadInt32(36L) -eq $sharedWrite
        SharedTransportDomainPendingTotal = $domainPendingTotal
        TargetProcessId = [uint32]$gameProcess.Id
        SharedTransportProducerProcessId = $sharedProducerProcessId
        SharedTransportConsumerProcessId = $sharedConsumerProcessId
        NativeProbeProcessId = $nativeProcessId
        NativeSemanticWireProcessIds = $nativeWireProcessIds
        SharedTransportCrossProcess = $sharedTransportCrossProcess
        SharedTransportCrossProcessNegativeCount = $crossProcessNegativeCount
        SharedTransportAttempted = $sharedAttempted
        SharedTransportAccepted = $sharedWrite
        SharedTransportDropped = $sharedDropped
        SharedTransportSampled = $sharedSampled
        SharedTransportHighWaterMark = $sharedAccessor.ReadInt32(52L)
        SharedTransportConsumerLag = $sharedAccessor.ReadInt32(56L)
        SharedTransportLaneContractPassed = [bool]$laneContractPassed
        SharedTransportLaneResults = @($laneRows)
        SharedTransportDomainDropTotal = $domainDroppedTotal
        SharedTransportDomainWriteFailureTotal = $domainWriteFailureTotal
        SharedTransportDomainPendingAfterDrain = $domainPendingTotal
        SharedTransportDomainsWithAcceptedHighWater = $domainsWithAcceptedHighWater
        SharedTransportDomainsWithDropAccounting = $domainWithDropAccounting
        SharedTransportConsumedPayloads = $sharedAcceptedPayloads.Count
        SharedTransportSequenceOrdered = $sharedSequenceOrdered
        SharedTransportInvalidPayloads = $sharedInvalidPayloads
        SharedTransportWirePriorityCounts = $sharedPriorityCounts
        SharedTransportPriorityMismatches = $sharedPriorityMismatches
        SharedTransportBatchMaximumItems = $nativeBatchMaximumItems
        SharedTransportBatchAccepted = $nativeBatchAccepted
        SharedTransportBatchCount = $nativeBatchCount
        SharedTransportBatchEventSignalDelta = $nativeBatchEventSignalDelta
        SharedTransportBatchLockAcquisitionDelta = $nativeBatchLockAcquisitionDelta
        SharedTransportBatchContractVerifiedByNativeExport = $nativeBatchContractPassed
        SemanticEventTypeFixtureCount = $nativeTypeFixtures.Count
        SemanticEventTypeDistinctCount = $nativeTypeNames.Count
        SemanticEventTypeNativeCount = $nativeSemanticTypeCount
        SemanticEventTypeDigestSHA256 = $nativeTypeDigest
        NativeProbeSelfTestPath = Convert-ToRunEvidencePath $nativeReportPath
        NativeProbeSelfTestSHA256 = $nativeReportSHA256
        NativeProbeDuplicateNegativeCount = $nativeReportDuplicateNegativeCount
        NativeProbeSelfTestSchemaId = if ($null -ne $nativeReport) { [string]$nativeReport.SchemaId } else { "" }
        NativeProbeSelfTestSchemaVersion = if ($null -ne $nativeReport) { [int]$nativeReport.SchemaVersion } else { 0 }
        NativeProbeIdentityProfile = if ($null -ne $nativeReport) { [string]$nativeReport.IdentityProfile } else { "" }
        SensitiveOutboundPrivacyPassed = $sensitiveOutboundPrivacyPassed
        SensitiveOutboundPrivacyRecord = $sensitiveOutboundPrivacyRecord
        NativeSemanticWirePath = Convert-ToRunEvidencePath $nativeSemanticWirePath
        NativeSemanticWireSHA256 = $nativeSemanticWireSHA256
        NativeSemanticWireCount = $nativeWireRows.Count
        NativeSemanticWireVerificationPath = Convert-ToRunEvidencePath $nativeSemanticWireVerificationPath
        NativeSemanticWireVerificationSHA256 = $nativeSemanticWireVerificationSHA256
        NativeSemanticWireVerificationSchemaId = if ($null -ne $nativeSemanticWireVerification) { [string]$nativeSemanticWireVerification.schemaId } else { "" }
        NativeSemanticWireVerificationSchemaVersion = if ($null -ne $nativeSemanticWireVerification) { [int]$nativeSemanticWireVerification.schemaVersion } else { 0 }
        NativeSemanticWireVerificationReport = $nativeSemanticWireVerification
        UltimateNativeWireReportPath = Convert-ToRunEvidencePath $ultimateNativeWireReportPath
        UltimateNativeWireReportSHA256 = $ultimateNativeWireReportSHA256
        UltimateNativeWireReport = $ultimateNativeWireReport
        DeepProbeCandidateMapPath = Convert-ToRunEvidencePath $deepProbeCandidateMapPath
        DeepProbeCandidateMapSHA256 = $candidateMapSHA256
        DeepProbeCandidateMapSchemaVersion = if ($null -ne $candidateMap) { [string]$candidateMap.SchemaVersion } else { "" }
        DeepProbeCandidateMapDuplicateNegativeCount = $candidateMapDuplicateNegativeCount
        ProbeDomainCount = $probeDomainResults.Count
        ConfirmedContractDomainCount = $confirmedContractDomainCount
        CandidateOnlyBlockedDomainCount = $candidateOnlyBlockedDomainCount
        ProbeDomainWirePath = Convert-ToRunEvidencePath $probeDomainWirePath
        ProbeDomainWireSHA256 = $probeDomainWireSHA256
        ProbeDomainWireDuplicateNegativeCount = $probeDomainWireDuplicateNegativeCount
        ProbeDomainResults = @($probeDomainResults)
        CandidatePromotionGateCount = @($candidateGateResults | Where-Object { $_.Category -ceq "Promotion" }).Count
        CandidateAbiSafetyGateCount = @($candidateGateResults | Where-Object { $_.Category -ceq "AbiSafety" }).Count
        CandidateTotalGateCount = $candidateGateResults.Count
        CandidateActivationGateMatrixValid = $candidateGateMatrixValid
        CandidateActivationPassedGateCount = $candidatePassedGateCount
        CandidateActivationRejectedGateCount = $candidateRejectedGateCount
        CandidateGateResults = @($candidateGateResults)
        SemanticAdmissionCapContractPassed = $null -ne $nativeReport -and [bool]$nativeReport.SemanticAdmissionCapContractPassed
        SemanticAdmissionCapSharedLedgerPassed = $null -ne $nativeReport -and [bool]$nativeReport.SemanticAdmissionCapSharedLedgerPassed
        SemanticAdmissionCapLiveStateUntouched = $null -ne $nativeReport -and [bool]$nativeReport.SemanticAdmissionCapLiveStateUntouched
        SemanticAdmissionHardLimit = if ($null -ne $nativeReport) { [int]$nativeReport.SemanticAdmissionHardLimit } else { 0 }
        SemanticAdmissionLowPriorityCeiling = if ($null -ne $nativeReport) { [int]$nativeReport.SemanticAdmissionLowPriorityCeiling } else { 0 }
        SemanticAdmissionCapResults = if ($null -ne $nativeReport) { @($nativeReport.SemanticAdmissionCapResults) } else { @() }
        FaultDiagnosticConsumerAckPath = Convert-ToRunEvidencePath $faultDiagnosticConsumerAckPath
        FaultDiagnosticConsumerAckSHA256 = $faultDiagnosticConsumerAckSHA256
        FaultDiagnosticConsumerAckArtifactVerified = $faultDiagnosticConsumerArtifactVerified
        FaultDiagnosticConsumerAck = $faultDiagnosticConsumerAckRecord
        FaultDiagnosticCrossProcessObserved = $faultDiagnosticConsumerPassed
        FaultDiagnosticPriorityPolicyPassed = $null -ne $nativeReport -and
            [bool]$nativeReport.FaultDiagnosticPriorityPolicyPassed
        FaultDiagnosticSelectedPriority = if ($null -ne $nativeReport) {
            [int]$nativeReport.FaultDiagnosticSelectedPriority
        } else { -1 }
        StartupProbeDiagnosticSelectedPriority = if ($null -ne $nativeReport) {
            [int]$nativeReport.StartupProbeDiagnosticSelectedPriority
        } else { -1 }
        FaultDiagnosticAcceptedAfterLowPriorityCeiling = $null -ne $nativeReport -and
            [bool]$nativeReport.FaultDiagnosticAcceptedAfterLowPriorityCeiling
        FaultDiagnosticAcceptedBeyondLegacyHardLimit = $null -ne $nativeReport -and
            [bool]$nativeReport.FaultDiagnosticAcceptedBeyondLegacyHardLimit
        FaultDiagnosticBeyondLegacyLimitDropReason = if ($null -ne $nativeReport) {
            [int]$nativeReport.FaultDiagnosticBeyondLegacyLimitDropReason
        } else { 0 }
        NativeProbeSelfTestReport = $nativeReport
    }
    $summaryPath = Join-Path $analysisDir "product-injector-selftest-summary.json"
    $summaryJson = $summary | ConvertTo-Json -Depth 12
    Write-Utf8NoBomAtomic -Path $summaryPath -Content ($summaryJson + "`n")
    $summaryJson
    if ($failures.Count -ne 0) { exit 1 }
}
finally {
    $stopEvent.Set() | Out-Null
    $stopEvent.Dispose()
    $sharedEvent.Dispose()
    $sharedFixtureCompleteEvent.Dispose()
    $sharedFixtureDrainedEvent.Dispose()
    $runtimeFixturesReadyEvent.Dispose()
    $blockingReleaseEvent.Set() | Out-Null
    $blockingReleaseEvent.Dispose()
    $blockingAllReturnedEvent.Dispose()
    $identitySnapshotRequestEvent.Set() | Out-Null
    $identitySnapshotRequestEvent.Dispose()
    $identitySnapshotCompleteEvent.Dispose()
    $identitySnapshotBaselineReadyEvent.Dispose()
    $sharedAccessor.Dispose()
    $sharedMmf.Dispose()
    if ($injectorProcess -ne $null -and -not $injectorProcess.HasExited) {
        Stop-Process -Id $injectorProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($gameProcess -ne $null -and -not $gameProcess.HasExited) {
        Stop-Process -Id $gameProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldCase) { Remove-Item Env:\GOD2_TRACE_SELFTEST_CASE -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SELFTEST_CASE = $oldCase }
    if ($null -eq $oldStartDelay) { Remove-Item Env:\GOD2_TRACE_SELFTEST_START_DELAY_MS -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SELFTEST_START_DELAY_MS = $oldStartDelay }
    if ($null -eq $oldHoldDelay) { Remove-Item Env:\GOD2_TRACE_SELFTEST_HOLD_MS -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SELFTEST_HOLD_MS = $oldHoldDelay }
    if ($null -eq $oldSharedRingSelfTest) { Remove-Item Env:\GOD2_TRACE_SHARED_RING_SELFTEST -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SHARED_RING_SELFTEST = $oldSharedRingSelfTest }
    if ($null -eq $oldSharedFixtureCompleteEvent) { Remove-Item Env:\GOD2_TRACE_SHARED_RING_COMPLETE_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SHARED_RING_COMPLETE_EVENT = $oldSharedFixtureCompleteEvent }
    if ($null -eq $oldSharedFixtureDrainedEvent) { Remove-Item Env:\GOD2_TRACE_SHARED_RING_DRAINED_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_SHARED_RING_DRAINED_EVENT = $oldSharedFixtureDrainedEvent }
    if ($null -eq $oldRuntimeFixturesReadyEvent) { Remove-Item Env:\GOD2_TRACE_RUNTIME_FIXTURES_READY_FOR_STOP_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_RUNTIME_FIXTURES_READY_FOR_STOP_EVENT = $oldRuntimeFixturesReadyEvent }
    if ($null -eq $oldBlockingReleaseEvent) { Remove-Item Env:\GOD2_TRACE_BLOCKING_RELEASE_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_BLOCKING_RELEASE_EVENT = $oldBlockingReleaseEvent }
    if ($null -eq $oldBlockingAllReturnedEvent) { Remove-Item Env:\GOD2_TRACE_BLOCKING_ALL_RETURNED_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_BLOCKING_ALL_RETURNED_EVENT = $oldBlockingAllReturnedEvent }
    if ($null -eq $oldIdentitySnapshotPath) { Remove-Item Env:\GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_PATH -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_PATH = $oldIdentitySnapshotPath }
    if ($null -eq $oldIdentitySnapshotRequestEvent) { Remove-Item Env:\GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_REQUEST_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_REQUEST_EVENT = $oldIdentitySnapshotRequestEvent }
    if ($null -eq $oldIdentitySnapshotCompleteEvent) { Remove-Item Env:\GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_COMPLETE_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_COMPLETE_EVENT = $oldIdentitySnapshotCompleteEvent }
    if ($null -eq $oldIdentitySnapshotBaselineReadyEvent) { Remove-Item Env:\GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_BASELINE_READY_EVENT -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_IDENTITY_NEGATIVE_SNAPSHOT_BASELINE_READY_EVENT = $oldIdentitySnapshotBaselineReadyEvent }
    if ($null -eq $oldIdentitySnapshotProbePath) { Remove-Item Env:\GOD2_TRACE_IDENTITY_NEGATIVE_PROBE_PATH -ErrorAction SilentlyContinue }
    else { $env:GOD2_TRACE_IDENTITY_NEGATIVE_PROBE_PATH = $oldIdentitySnapshotProbePath }
}
