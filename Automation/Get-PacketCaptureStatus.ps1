$ErrorActionPreference = "Stop"
$stateRoot = Join-Path $PSScriptRoot "State"
$activePath = Join-Path $stateRoot "packet-capture-active.json"
$lastPath = Join-Path $stateRoot "packet-capture-last.json"

if (Test-Path -LiteralPath $activePath) {
    $capture = Get-Content -LiteralPath $activePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $traceRoot = [string]$capture.capture.traceRoot
    $files = Get-ChildItem -LiteralPath $traceRoot -Recurse -File -ErrorAction SilentlyContinue |
        Select-Object FullName, Length, LastWriteTime
    [pscustomobject]@{
        active = $true
        runDir = $traceRoot
        clientPid = [int]$capture.client.pid
        files = @($files)
    } | ConvertTo-Json -Depth 8
    exit 0
}

if (Test-Path -LiteralPath $lastPath) {
    $last = Get-Content -LiteralPath $lastPath -Raw -Encoding UTF8 | ConvertFrom-Json
    [pscustomobject]@{
        active = $false
        last = $last
    } | ConvertTo-Json -Depth 8
    exit 0
}

[pscustomobject]@{
    active = $false
    last = $null
} | ConvertTo-Json -Depth 4
