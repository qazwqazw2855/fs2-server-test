param(
    [int] $MaximumAttempts = 8,
    [int] $InitialDelayMilliseconds = 20
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$probeRoot = Join-Path (Get-God2RepoRoot) "Artifacts\SkillRuntimeRegression\json-retry-probe"
$transientPath = Join-Path $probeRoot "transient.json"
$permanentPath = Join-Path $probeRoot "permanent.json"
$atomicPath = Join-Path $probeRoot "atomic.json"
$writer = $null
$atomicWriters = @()

try {
    New-Item -ItemType Directory -Force -Path $probeRoot | Out-Null
    Set-Content -LiteralPath $transientPath -Value "{invalid" -Encoding UTF8
    $writer = Start-Job -ScriptBlock {
        param($path)
        Start-Sleep -Milliseconds 90
        @{ ok = $true } | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding UTF8
    } -ArgumentList $transientPath

    $transient = Read-God2JsonWithRetry `
        -Path $transientPath `
        -MaximumAttempts $MaximumAttempts `
        -InitialDelayMilliseconds $InitialDelayMilliseconds
    Wait-Job -Job $writer | Out-Null

    Set-Content -LiteralPath $permanentPath -Value "{invalid" -Encoding UTF8
    $permanentFailureBounded = $false
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        Read-God2JsonWithRetry `
            -Path $permanentPath `
            -MaximumAttempts 3 `
            -InitialDelayMilliseconds 1 | Out-Null
    }
    catch {
        $permanentFailureBounded = $true
    }
    finally {
        $stopwatch.Stop()
    }

    1..16 | ForEach-Object {
        $ordinal = $_
        $atomicWriters += Start-Job -ScriptBlock {
            param($common, $path, $value)
            . $common
            Write-God2AtomicJson -Value ([ordered]@{ ordinal = $value; valid = $true }) -Path $path
        } -ArgumentList (Join-Path $PSScriptRoot "God2Automation.Common.ps1"), $atomicPath, $ordinal
    }
    $atomicWriters | Wait-Job | Receive-Job | Out-Null
    $atomic = Read-God2JsonWithRetry -Path $atomicPath
    $atomicWritePass = [bool]$atomic.valid -and [int]$atomic.ordinal -ge 1 -and [int]$atomic.ordinal -le 16
    $temporaryFilesRemaining = @(Get-ChildItem -LiteralPath $probeRoot -Filter "atomic.json.*.tmp" -File -ErrorAction SilentlyContinue).Count

    $status = if ([bool]$transient.ok -and $permanentFailureBounded -and $atomicWritePass -and $temporaryFilesRemaining -eq 0) { "PASS" } else { "FAIL" }
    [pscustomobject]@{
        transientRecovery = [bool]$transient.ok
        permanentFailureBounded = $permanentFailureBounded
        boundedFailureElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        maximumAttempts = $MaximumAttempts
        concurrentAtomicWriters = $atomicWriters.Count
        concurrentAtomicWrite = $atomicWritePass
        temporaryFilesRemaining = $temporaryFilesRemaining
        status = $status
    } | ConvertTo-Json -Depth 4

    if ($status -ne "PASS") {
        exit 1
    }
}
finally {
    if ($writer) {
        Remove-Job -Job $writer -Force -ErrorAction SilentlyContinue
    }
    if ($atomicWriters.Count -ne 0) {
        $atomicWriters | Remove-Job -Force -ErrorAction SilentlyContinue
    }

    Remove-Item -LiteralPath $transientPath, $permanentPath, $atomicPath -Force -ErrorAction SilentlyContinue
}
