Set-StrictMode -Version 2.0

function Wait-God2OfficialRuntimeControl {
    param(
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $SessionId,
        [Parameter(Mandatory = $true)] [uint32] $ClientProcessId,
        [Parameter(Mandatory = $true)] [string[]] $AcceptedStatuses,
        [ValidateRange(1, 600)] [int] $TimeoutSeconds = 120,
        [Parameter(Mandatory = $true)] [string] $Phase
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ($null -eq (Get-Process -Id $ClientProcessId -ErrorAction SilentlyContinue)) {
            throw "Official client exited before the $Phase gate."
        }

        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $raw = $null
            try {
                $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
            }
            catch [System.IO.IOException] { }
            if ($null -ne $raw) {
                $control = $raw | ConvertFrom-Json
                $identityValid =
                    [string]$control.schemaId -ceq 'God2ContractAcquisitionLiveControl' -and
                    [int]$control.schemaVersion -eq 1 -and
                    [string]$control.sessionId -ceq $SessionId -and
                    [uint32]$control.clientProcessId -eq $ClientProcessId
                if (-not $identityValid) {
                    throw "$Phase Contract Acquisition live-control identity mismatch."
                }
                if ([string]$control.status -ceq 'ABORT') {
                    throw "$Phase Contract Acquisition gate was blocked: $($control.firstBlocker)"
                }
                if ($AcceptedStatuses -ccontains [string]$control.status) {
                    return $control
                }
            }
        }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)

    throw "Contract Acquisition did not receive the bounded $Phase gate."
}
