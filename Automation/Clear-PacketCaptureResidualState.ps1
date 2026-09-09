param(
    [switch] $Force
)

$ErrorActionPreference = "Stop"

$stateRoot = Join-Path $PSScriptRoot "State"
New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

$activePath = Join-Path $stateRoot "packet-capture-active.json"
$lastPath = Join-Path $stateRoot "packet-capture-last.json"
$statusPath = Join-Path $stateRoot "host-status.json"
$commandPath = Join-Path $stateRoot "host-command.json"

$removed = New-Object System.Collections.Generic.List[string]
$preserved = New-Object System.Collections.Generic.List[string]

function Remove-StateFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force
        $script:removed.Add((Split-Path -Leaf $Path)) | Out-Null
    }
}

if (Test-Path -LiteralPath $activePath) {
    $active = $null
    try {
        $active = Get-Content -LiteralPath $activePath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        if (-not $Force) {
            throw "packet-capture-active.json is not parseable. Re-run with -Force to remove the stale active marker."
        }
    }

    $clientPid = 0
    if ($null -ne $active -and $null -ne $active.client -and $null -ne $active.client.pid) {
        $clientPid = [int]$active.client.pid
    }

    $clientAlive = $false
    if ($clientPid -gt 0) {
        $clientAlive = $null -ne (Get-Process -Id $clientPid -ErrorAction SilentlyContinue)
    }

    if ($clientAlive -and -not $Force) {
        $preserved.Add("packet-capture-active.json") | Out-Null
        throw "Active packet capture appears to be running for PID $clientPid. Stop capture first, or re-run with -Force only if this is stale."
    }

    Remove-StateFile -Path $activePath
}

Remove-StateFile -Path $lastPath
Remove-StateFile -Path $statusPath
Remove-StateFile -Path $commandPath

$remaining = Get-ChildItem -LiteralPath $stateRoot -Force -File |
    Where-Object { $_.Name -match '^(packet-capture-|host-)' } |
    Select-Object -ExpandProperty Name

Write-Host "PACKET CAPTURE RESIDUAL STATE CLEARED"
if ($removed.Count -gt 0) {
    Write-Host ("REMOVED: " + (($removed | Sort-Object) -join ", "))
}
else {
    Write-Host "REMOVED: none"
}

if ($preserved.Count -gt 0) {
    Write-Host ("PRESERVED: " + (($preserved | Sort-Object) -join ", "))
}

if (@($remaining).Count -gt 0) {
    Write-Host ("REMAINING STATE: " + ((@($remaining) | Sort-Object) -join ", "))
}
else {
    Write-Host "REMAINING STATE: none"
}

Write-Host "TRACE FILES: preserved"
Write-Host "REPORTS: preserved"
Write-Host "CONFIG: preserved"
