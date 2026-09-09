param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir,
    [ValidateSet("A", "B", "C", "D")]
    [string] $Sample,
    [int] $TimeoutSeconds = 240
)

$ErrorActionPreference = "Stop"

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$markers = Join-Path $runDir "markers.jsonl"
$metadata = Join-Path $runDir "metadata.jsonl"
$samplesDir = Join-Path $runDir "samples"
New-Item -ItemType Directory -Force -Path $samplesDir | Out-Null

$markerTime = [DateTimeOffset]::UtcNow
$marker = [ordered]@{
    sample = $Sample
    markerUtc = $markerTime.ToString("o")
    markerUnixMs = $markerTime.ToUnixTimeMilliseconds()
}
($marker | ConvertTo-Json -Compress) | Add-Content -LiteralPath $markers -Encoding UTF8

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $metadata) {
        $lines = Get-Content -LiteralPath $metadata -Tail 200 -ErrorAction SilentlyContinue
        foreach ($line in $lines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $event = $line | ConvertFrom-Json } catch { continue }
            if ($event.wallUnixMs -ge $marker.markerUnixMs -and
                $event.direction -eq "ClientToServer" -and
                ($event.api -eq "send" -or $event.api -eq "WSASend") -and
                ($event.transferredLength -eq 208 -or $event.requestedLength -eq 208 -or $event.frame208 -eq $true)) {
                $samplePath = Join-Path $samplesDir "$Sample.json"
                [ordered]@{
                    sample = $Sample
                    savedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                    traceSequence = $event.sequence
                    api = $event.api
                    socket = $event.socket
                    requestedLength = $event.requestedLength
                    transferredLength = $event.transferredLength
                    returnAddress = $event.returnAddress
                } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $samplePath -Encoding UTF8
                Write-Output "$Sample sample saved"
                exit 0
            }
        }
    }
    Start-Sleep -Milliseconds 500
}

throw "Timed out waiting for $Sample 208-byte client-to-server login sample."

