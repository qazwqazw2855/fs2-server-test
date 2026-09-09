param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir
)

$ErrorActionPreference = "Stop"

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$activePath = Join-Path $runDir "active-run.json"
if (-not (Test-Path -LiteralPath $activePath)) {
    throw "active-run.json not found: $activePath"
}

$active = Get-Content -Raw -LiteralPath $activePath | ConvertFrom-Json
Start-Sleep -Seconds 1

$detachResult = "not-running"
$process = Get-Process -Id $active.pid -ErrorAction SilentlyContinue
if ($process) {
    & $active.launcher --detach --pid $active.pid --module $active.module | Out-Host
    if ($LASTEXITCODE -ne 0) {
        $detachResult = "failed"
    } else {
        $detachResult = "pass"
    }
}

$manifest = Get-Content -Raw -LiteralPath (Join-Path $runDir "source-manifest.json") | ConvertFrom-Json
$currentHash = (Get-FileHash -LiteralPath $manifest.client.exePath -Algorithm SHA256).Hash
$summary = [ordered]@{
    stoppedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    detach = $detachResult
    clientSha256Before = $manifest.client.sha256
    clientSha256After = $currentHash
    clientBinaryUnchanged = ($currentHash -eq $manifest.client.sha256)
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDir "stop-summary.json") -Encoding UTF8
$summary | ConvertTo-Json -Depth 5

