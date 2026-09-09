param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int] $ProcessId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId,

    [ValidateRange(30, 900)]
    [int] $WaitSeconds = 600,

    [switch] $DiagnosticOnly,

    [switch] $ElevatedWorker
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$expectedClientSha256 = "6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "Artifacts\AnalysisScratch\BattleActors\$SessionId"))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "Artifacts\AnalysisScratch\BattleActors"))
$allowedPrefix = $allowedRoot.TrimEnd('\') + '\'
$statePath = Join-Path $outputRoot "state.json"

if (-not $outputRoot.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Battle actor output escaped the approved analysis directory."
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-State {
    param([Parameter(Mandatory = $true)] [hashtable] $State)
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    [IO.File]::WriteAllText(
        $statePath,
        ([ordered]@{
            schemaVersion = "god2-read-only-battle-actor-state-v1"
            sessionId = $SessionId
            processId = $ProcessId
            gameMemoryWritten = $false
        } + $State | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
}

if (-not (Test-IsAdministrator)) {
    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    if (Test-Path -LiteralPath $statePath) {
        Remove-Item -LiteralPath $statePath -Force
    }
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'),
        "-ProcessId", $ProcessId,
        "-SessionId", ('"' + $SessionId + '"'),
        "-WaitSeconds", $WaitSeconds,
        "-ElevatedWorker"
    )
    if ($DiagnosticOnly) {
        $arguments += "-DiagnosticOnly"
    }
    $worker = Start-Process `
        -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(90)
    do {
        Start-Sleep -Milliseconds 250
        if (Test-Path -LiteralPath $statePath -PathType Leaf) {
            try {
                $state = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
                if ([string]$state.status -in @("ARMED", "CAPTURED", "DIAGNOSTIC", "FAILED")) {
                    $state | ConvertTo-Json -Depth 8
                    exit $(if ([string]$state.status -ceq "FAILED") { 1 } else { 0 })
                }
            }
            catch [System.IO.IOException] { }
        }
        $worker.Refresh()
    } while (-not $worker.HasExited -and (Get-Date) -lt $deadline)
    throw "Elevated read-only battle actor worker did not reach ARMED state."
}

if (-not $ElevatedWorker) {
    throw "Administrator execution must use the internal ElevatedWorker switch."
}

try {
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    if ([string]$process.ProcessName -cne "God2_opt") {
        throw "The selected process is not God2_opt.exe."
    }
    $module = $process.MainModule
    $clientSha256 = (Get-FileHash -LiteralPath $module.FileName -Algorithm SHA256).Hash
    if (-not [string]::Equals($clientSha256, $expectedClientSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The running God2 client does not match the approved executable hash."
    }

    . (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
    Initialize-God2NativeApi
    $moduleBase = [uint32]$module.BaseAddress.ToInt64()

    function Resolve-BattlePointerTableAddress {
        $rootPointerAddress = [uint32]($moduleBase + 0x004AD5F8)
        $rootPointerBytes = [God2Automation.NativeApi]::ReadProcessBytes(
            $ProcessId, $rootPointerAddress, 4)
        $rootObject = [BitConverter]::ToUInt32($rootPointerBytes, 0)
        if ($rootObject -lt 0x00010000 -or $rootObject -gt 0x7FFEFFFF) {
            throw "The root game object pointer is not readable."
        }
        $dispatcherPointerBytes = [God2Automation.NativeApi]::ReadProcessBytes(
            $ProcessId, [uint32]($rootObject + 0x008885F0), 4)
        $gameObject = [BitConverter]::ToUInt32($dispatcherPointerBytes, 0)
        if ($gameObject -lt 0x00010000 -or $gameObject -gt 0x7FFEFFFF) {
            throw "The gameplay dispatcher object pointer is not readable."
        }
        return [uint32]($gameObject + 0x00149DF8 + 0x38)
    }

    $pointerTableAddress = Resolve-BattlePointerTableAddress

    if ($DiagnosticOnly) {
        $pointerBytes = [God2Automation.NativeApi]::ReadProcessBytes(
            $ProcessId, $pointerTableAddress, (27 * 24) + 4)
        $slots = [Collections.Generic.List[object]]::new()
        for ($slot = 0; $slot -lt 28; $slot++) {
            $pointer = [BitConverter]::ToUInt32($pointerBytes, $slot * 24)
            $entry = [ordered]@{
                slot = $slot
                pointerPresent = $pointer -ge 0x00010000 -and $pointer -le 0x7FFEFFFF
                readable = $false
                lifecycle = $null
                battlePosition = $null
                actorState = $null
                readError = $null
            }
            if ($entry.pointerPresent) {
                try {
                    $bytes = [God2Automation.NativeApi]::ReadProcessBytes($ProcessId, $pointer, 0xE00)
                    $entry.readable = $bytes.Length -eq 0xE00
                    if ($entry.readable) {
                        $entry.lifecycle = [int][BitConverter]::ToInt16($bytes, 0x1A)
                        $entry.battlePosition = [int][BitConverter]::ToUInt16($bytes, 0xDB0)
                        $entry.actorState = [int][BitConverter]::ToInt16($bytes, 0x1D8)
                    }
                }
                catch {
                    $entry.readError = $_.Exception.Message
                }
            }
            $slots.Add([pscustomobject]$entry)
        }
        Write-State @{
            status = "DIAGNOSTIC"
            inspectedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
            rootPointerPresent = $true
            dispatcherPointerPresent = $true
            slots = @($slots)
        }
        exit 0
    }

    Write-State @{
        status = "ARMED"
        armedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
        timeoutSeconds = $WaitSeconds
    }

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    $lastHeartbeat = Get-Date
    $maximumActiveActorCount = 0
    do {
        $pointerTableAddress = Resolve-BattlePointerTableAddress
        $pointerBytes = [God2Automation.NativeApi]::ReadProcessBytes(
            $ProcessId, $pointerTableAddress, (27 * 24) + 4)
        $actors = [Collections.Generic.List[object]]::new()
        for ($slot = 0; $slot -lt 28; $slot++) {
            $pointer = [BitConverter]::ToUInt32($pointerBytes, $slot * 24)
            if ($pointer -lt 0x00010000 -or $pointer -gt 0x7FFEFFFF) {
                continue
            }
            try {
                $bytes = [God2Automation.NativeApi]::ReadProcessBytes($ProcessId, $pointer, 0xE00)
                if ($bytes.Length -ne 0xE00) {
                    continue
                }
                $lifecycle = [BitConverter]::ToInt16($bytes, 0x1A)
                $battlePosition = [BitConverter]::ToUInt16($bytes, 0xDB0)
                $actorState = [BitConverter]::ToInt16($bytes, 0x1D8)
                if ($lifecycle -lt 0 -or $actorState -lt 0 -or $battlePosition -gt 41) {
                    continue
                }
                $actors.Add([pscustomobject]@{
                    slot = $slot
                    pointer = $pointer
                    battlePosition = [int]$battlePosition
                    lifecycle = [int]$lifecycle
                    actorState = [int]$actorState
                    bytes = $bytes
                })
            }
            catch { }
        }

        $uniquePositions = @($actors | Select-Object -ExpandProperty battlePosition -Unique)
        $friendlyCount = @($actors | Where-Object { $_.battlePosition -lt 14 }).Count
        $enemyCount = @($actors | Where-Object {
            $_.battlePosition -ge 14 -and $_.battlePosition -lt 28
        }).Count
        $maximumActiveActorCount = [Math]::Max($maximumActiveActorCount, $actors.Count)
        if (((Get-Date) - $lastHeartbeat).TotalSeconds -ge 1) {
            Write-State @{
                status = "ARMED"
                armedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
                timeoutSeconds = $WaitSeconds
                maximumActiveActorCountObserved = $maximumActiveActorCount
                currentActiveActorCount = $actors.Count
                currentActivePositions = @($uniquePositions)
            }
            $lastHeartbeat = Get-Date
        }
        if ($actors.Count -ge 2 -and $uniquePositions.Count -eq $actors.Count -and
            $friendlyCount -ge 1 -and $enemyCount -ge 1) {
            $capturedAt = [DateTimeOffset]::UtcNow
            $manifestActors = [Collections.Generic.List[object]]::new()
            foreach ($actor in $actors) {
                $fileName = "actor-slot-$($actor.slot)-position-$($actor.battlePosition).bin"
                $path = Join-Path $outputRoot $fileName
                [IO.File]::WriteAllBytes($path, $actor.bytes)
                $manifestActors.Add([pscustomobject][ordered]@{
                    slot = $actor.slot
                    battlePosition = $actor.battlePosition
                    lifecycle = $actor.lifecycle
                    actorState = $actor.actorState
                    byteLength = $actor.bytes.Length
                    sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                    temporarySnapshotFile = $fileName
                })
            }
            Write-State @{
                status = "CAPTURED"
                capturedAtUtc = $capturedAt.ToString("o")
                access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
                actorCount = $actors.Count
                actors = @($manifestActors)
            }
            exit 0
        }
        Start-Sleep -Milliseconds 100
    } while ((Get-Date) -lt $deadline)

    Write-State @{
        status = "TIMED_OUT"
        timedOutAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        access = "PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"
        actorCount = 0
    }
    exit 2
}
catch {
    Write-State @{
        status = "FAILED"
        failedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        error = $_.Exception.Message
    }
    throw
}
