param(
    [ValidateSet("Apply", "Clear", "Query", "SelfTest", "Compile")]
    [string] $Action = "Query",
    [uint32] $TargetProcessId,
    [string] $Module = "",
    [string] $PlanPath = "",
    [string] $Launcher = "",
    [string] $BinaryOut = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Convert-ToUInt32([object] $Value, [string] $Name) {
    if ($Value -is [string]) {
        $text = $Value.Trim()
        if ($text.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
            return [Convert]::ToUInt32($text.Substring(2), 16)
        }
        return [Convert]::ToUInt32($text, 10)
    }
    if ([uint64]$Value -gt [uint32]::MaxValue) { throw "$Name exceeds UInt32." }
    return [uint32]$Value
}

function Resolve-Domain([object] $Value) {
    $domains = @(
        "Network", "Parser", "Serializer", "Handler", "Object", "Allocation",
        "VTable", "Factory", "ManagerLookup", "Registry", "ResourceDecode",
        "Mutation", "TaintSeed", "FormulaOperand", "Quest", "Map", "Portal",
        "NPC", "Monster", "Battle", "Inventory", "Item", "Skill", "Pet/Mount",
        "Snapshot"
    )
    if ($Value -isnot [string] -or $Value -match '^\d+$') {
        $index = Convert-ToUInt32 $Value "domain"
    } else {
        $index = [Array]::IndexOf($domains, [string]$Value)
        if ($index -lt 0) { throw "Unknown domain: $Value" }
    }
    if ($index -lt 4 -or $index -gt 24) {
        throw "Generic candidates are limited to deep domains 4..24."
    }
    return [uint32]$index
}

function Resolve-Priority([object] $Value, [uint32] $Domain) {
    $expected = if ($Domain -eq 11) { 0 } elseif ($Domain -le 10) { 1 } elseif ($Domain -le 13) { 2 } else { 3 }
    $actual = if ($Value -is [string] -and $Value -match '^P([0-3])$') {
        [uint32]$Matches[1]
    } else {
        Convert-ToUInt32 $Value "priority"
    }
    if ($actual -ne $expected) {
        throw "Domain $Domain requires P$expected, not P$actual."
    }
    return $actual
}

function Get-FlagMask([object[]] $Values, [hashtable] $Map, [string] $Name) {
    [uint32]$mask = 0
    foreach ($value in @($Values)) {
        if (-not $Map.ContainsKey([string]$value)) { throw "Unknown $Name value: $value" }
        $mask = $mask -bor [uint32]$Map[[string]$value]
    }
    return $mask
}

function Write-FixedAscii([IO.BinaryWriter] $Writer, [string] $Value, [int] $Bytes, [string] $Name) {
    $encoded = [Text.Encoding]::ASCII.GetBytes($Value)
    if ($encoded.Length -eq 0 -or $encoded.Length -ge $Bytes) {
        throw "$Name must contain 1..$($Bytes - 1) ASCII bytes."
    }
    if ($Value -notmatch '^[A-Za-z0-9_.:-]+$') {
        throw "$Name contains characters outside the probe wire allow-list."
    }
    $buffer = [byte[]]::new($Bytes)
    [Array]::Copy($encoded, $buffer, $encoded.Length)
    $Writer.Write($buffer)
}

function Build-GenericProbeBinary([string] $JsonPath, [string] $OutputPath) {
    $plan = Get-Content -LiteralPath $JsonPath -Raw | ConvertFrom-Json
    $targets = @($plan.targets)
    if ($targets.Count -lt 1 -or $targets.Count -gt 3) {
        throw "A plan requires 1..3 targets."
    }
    $process = Get-Process -Id $TargetProcessId -ErrorAction Stop
    [uint64]$creationTime = $process.StartTime.ToUniversalTime().ToFileTimeUtc()
    $captureMap = @{ ECX = 1; EDX = 2; BoundedStack = 4 }
    $followMap = @{ Return = 1; Caller = 2; Consumer = 4 }
    $objectMap = @{ None = 0; ECX = 1; EDX = 2; Stack0 = 3 }
    $seenIds = @{}
    $seenRvas = @{}
    $fullOutput = [IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $fullOutput) | Out-Null
    $stream = [IO.File]::Open($fullOutput, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [IO.BinaryWriter]::new($stream, [Text.Encoding]::ASCII, $false)
    try {
        $writer.Write([uint32]0x31505047)
        $writer.Write([uint32]1)
        $writer.Write([uint32]336)
        $writer.Write((Convert-ToUInt32 $plan.generation "generation"))
        $writer.Write([uint32]$targets.Count)
        $writer.Write([uint32]0)
        $writer.Write([uint32]$TargetProcessId)
        $writer.Write([uint64]$creationTime)
        for ($index = 0; $index -lt 3; $index++) {
            if ($index -ge $targets.Count) {
                $writer.Write([byte[]]::new(100))
                continue
            }
            $target = $targets[$index]
            [uint32]$targetId = Convert-ToUInt32 $target.targetId "targetId"
            [uint32]$rva = Convert-ToUInt32 $target.functionRva "functionRva"
            if ($targetId -eq 0 -or $seenIds.ContainsKey($targetId)) { throw "targetId must be unique and nonzero." }
            if ($seenRvas.ContainsKey($rva)) { throw "functionRva must be unique." }
            $seenIds[$targetId] = $true
            $seenRvas[$rva] = $true
            [uint32]$capture = Get-FlagMask @($target.capture) $captureMap "capture"
            [uint32]$follow = Get-FlagMask @($target.follow) $followMap "follow"
            [uint32]$stackWords = Convert-ToUInt32 $target.stackWords "stackWords"
            [uint32]$consumerSteps = Convert-ToUInt32 $target.consumerSteps "consumerSteps"
            [uint32]$maximum = Convert-ToUInt32 $target.maximumObservations "maximumObservations"
            [uint32]$domain = Resolve-Domain $target.domain
            [uint32]$priority = Resolve-Priority $target.priority $domain
            if (-not $objectMap.ContainsKey([string]$target.objectSource)) { throw "Unknown objectSource: $($target.objectSource)" }
            [uint32]$objectSource = $objectMap[[string]$target.objectSource]
            $expected = @(([string]$target.expectedBytes -split '[ ,:-]+' | Where-Object { $_ }))
            if ($expected.Count -ne 8) { throw "expectedBytes must contain exactly 8 bytes." }
            [byte[]]$signature = $expected | ForEach-Object { [Convert]::ToByte($_, 16) }
            $writer.Write($targetId)
            $writer.Write($rva)
            $writer.Write($capture)
            $writer.Write($follow)
            $writer.Write($stackWords)
            $writer.Write($consumerSteps)
            $writer.Write($maximum)
            $writer.Write($domain)
            $writer.Write($priority)
            $writer.Write($objectSource)
            $writer.Write([uint32]8)
            $writer.Write($signature)
            Write-FixedAscii $writer ([string]$target.candidateId) 48 "candidateId"
        }
    }
    finally {
        $writer.Dispose()
    }
    if ((Get-Item -LiteralPath $fullOutput).Length -ne 336) {
        throw "GenericProbePlanV1 compiler produced a noncanonical size."
    }
    return $fullOutput
}

if ($TargetProcessId -eq 0) { throw "TargetProcessId is required." }
if ($Action -eq "Compile") {
    if ($PlanPath.Length -eq 0) { throw "PlanPath is required for Compile." }
    if ($BinaryOut.Length -eq 0) {
        $BinaryOut = [IO.Path]::ChangeExtension([IO.Path]::GetFullPath($PlanPath), ".gpp1.bin")
    }
    $binary = Build-GenericProbeBinary $PlanPath $BinaryOut
    Write-Output "GENERIC_PROBE_BINARY=$binary"
    return
}
if ($Module.Length -eq 0) { throw "Module is required for $Action." }
[uint32]$moduleValue = Convert-ToUInt32 $Module "module"
if ($Launcher.Length -eq 0) {
    $sideBySide = Join-Path $repoRoot "Build\ClientInstrumentation-GenericProbe\Release\God2ClientTraceLauncher.exe"
    $standard = Join-Path $repoRoot "Build\ClientInstrumentation\Release\God2ClientTraceLauncher.exe"
    $Launcher = if (Test-Path -LiteralPath $sideBySide) { $sideBySide } else { $standard }
}
if (-not (Test-Path -LiteralPath $Launcher)) { throw "Launcher not found: $Launcher" }

$arguments = @("--pid", [string]$TargetProcessId, "--module", ("0x{0:X8}" -f $moduleValue))
switch ($Action) {
    "Apply" {
        if ($PlanPath.Length -eq 0) { throw "PlanPath is required for Apply." }
        if ($BinaryOut.Length -eq 0) {
            $BinaryOut = [IO.Path]::ChangeExtension([IO.Path]::GetFullPath($PlanPath), ".gpp1.bin")
        }
        $binary = Build-GenericProbeBinary $PlanPath $BinaryOut
        $arguments = @("--generic-plan-apply") + $arguments + @("--plan", $binary)
    }
    "Clear" { $arguments = @("--generic-plan-clear") + $arguments }
    "Query" { $arguments = @("--generic-plan-query") + $arguments }
    "SelfTest" { $arguments = @("--generic-plan-self-test") + $arguments }
}

& $Launcher @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Generic Probe Plan command failed with exit code $LASTEXITCODE."
}
