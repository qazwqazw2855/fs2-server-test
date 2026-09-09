param(
    [Parameter(Mandatory = $true)]
    [string] $RunDir,
    [int] $Port = 2592
)

$ErrorActionPreference = "Stop"

function Read-Exact([System.Net.Sockets.NetworkStream] $Stream, [int] $Length) {
    $buffer = [byte[]]::new($Length)
    $offset = 0
    while ($offset -lt $Length) {
        $read = $Stream.Read($buffer, $offset, $Length - $offset)
        if ($read -le 0) { break }
        $offset += $read
    }
    return [pscustomobject]@{
        Buffer = $buffer
        Length = $offset
    }
}

$runDir = (Resolve-Path -LiteralPath $RunDir).Path
$endpointLog = Join-Path $runDir "analysis\instrumentation-endpoint.jsonl"

$client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", $Port)
try {
    $stream = $client.GetStream()
    $handshake = Read-Exact $stream 19

    $clientHello = [byte[]](4, 0, 170, 85)
    $stream.Write($clientHello, 0, $clientHello.Length)

    $followup = Read-Exact $stream 6

    $loginCandidate = [byte[]]::new(208)
    $loginCandidate[0] = 208
    $loginCandidate[1] = 0
    $stream.Write($loginCandidate, 0, $loginCandidate.Length)
    $stream.Flush()
}
finally {
    $client.Close()
}

Start-Sleep -Seconds 1
$receivedCandidate = $false
if (Test-Path -LiteralPath $endpointLog) {
    $recent = Get-Content -LiteralPath $endpointLog -Tail 20 -ErrorAction SilentlyContinue
    foreach ($line in $recent) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $event = $line | ConvertFrom-Json } catch { continue }
        if ($event.eventName -eq "received-login-request-candidate" -and [int]$event.length -eq 208) {
            $receivedCandidate = $true
        }
    }
}

if ($handshake.Length -ne 19 -or $followup.Length -ne 6 -or -not $receivedCandidate) {
    throw "Instrumentation endpoint smoke failed. handshake=$($handshake.Length) followup=$($followup.Length) loginCandidate208=$receivedCandidate"
}

[ordered]@{
    handshakeLength = $handshake.Length
    followupLength = $followup.Length
    loginCandidateLength = 208
    endpointLoggedCandidate208 = $receivedCandidate
} | ConvertTo-Json -Depth 3
