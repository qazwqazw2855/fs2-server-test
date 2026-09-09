param(
    [string]$RepositoryRoot = "",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
if ($RepositoryRoot.Length -eq 0) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent `
        (Split-Path -Parent $PSScriptRoot))
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$buildRoot = Join-Path $RepositoryRoot `
    "Build\ClientInstrumentation\$Configuration"
$payloadRoot = Join-Path $RepositoryRoot `
    "tools\God2.PacketCapture\payload\x86"

function Test-ExactX86Pe {
    param([Parameter(Mandatory=$true)][byte[]]$Bytes,
          [Parameter(Mandatory=$true)][bool]$ExpectedDll,
          [Parameter(Mandatory=$true)][string]$Label)
    if ($Bytes.Length -lt 256 -or
        [BitConverter]::ToUInt16($Bytes, 0) -ne 0x5A4D) {
        throw "$Label is not an MZ executable"
    }
    $peOffset = [BitConverter]::ToInt32($Bytes, 0x3C)
    if ($peOffset -lt 64 -or $peOffset + 26 -gt $Bytes.Length -or
        [BitConverter]::ToUInt32($Bytes, $peOffset) -ne 0x00004550 -or
        [BitConverter]::ToUInt16($Bytes, $peOffset + 4) -ne 0x014C -or
        [BitConverter]::ToUInt16($Bytes, $peOffset + 24) -ne 0x010B) {
        throw "$Label is not PE32/I386"
    }
    $isDll = ([BitConverter]::ToUInt16($Bytes, $peOffset + 22) -band
        0x2000) -ne 0
    if ($isDll -ne $ExpectedDll) {
        throw "$Label DLL characteristic did not match its contract"
    }
}

function Sync-ExactPayload {
    param([Parameter(Mandatory=$true)][string]$Source,
          [Parameter(Mandatory=$true)][string]$Destination,
          [Parameter(Mandatory=$true)][bool]$ExpectedDll)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "locked x86 payload is missing: $Source"
    }
    $bytes = [IO.File]::ReadAllBytes($Source)
    Test-ExactX86Pe -Bytes $bytes -ExpectedDll $ExpectedDll `
        -Label ([IO.Path]::GetFileName($Source))
    $temporary = $Destination + ".sync-" + [guid]::NewGuid().ToString("N")
    $backup = ""
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
        if (Test-Path -LiteralPath $Destination) {
            $backup = $Destination + ".backup-" + [guid]::NewGuid().ToString("N")
            [IO.File]::Replace($temporary, $Destination, $backup)
        } else {
            [IO.File]::Move($temporary, $Destination)
        }
        $committed = $true
    } finally {
        if (-not $committed) {
            Remove-Item -LiteralPath $temporary -Force `
                -ErrorAction SilentlyContinue
        }
    }
    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash -LiteralPath $Destination `
        -Algorithm SHA256).Hash
    if ($sourceHash -cne $destinationHash -or
        (Get-Item -LiteralPath $Source).Length -ne
            (Get-Item -LiteralPath $Destination).Length) {
        throw "atomic payload sync did not preserve exact bytes: $Destination"
    }
    if ($backup.Length -gt 0) {
        Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
    }
    return [ordered]@{
        Source = $Source.Substring($RepositoryRoot.Length + 1).Replace('\','/')
        Destination = $Destination.Substring($RepositoryRoot.Length + 1).Replace('\','/')
        SHA256 = $destinationHash
        Bytes = (Get-Item -LiteralPath $Destination).Length
        Machine = "I386"
        Format = "PE32"
        IsDll = $ExpectedDll
    }
}

$probe = Sync-ExactPayload `
    -Source (Join-Path $buildRoot "God2ClientTraceProbe.dll") `
    -Destination (Join-Path $payloadRoot "God2PacketCaptureProbe.dll") `
    -ExpectedDll $true
$injector = Sync-ExactPayload `
    -Source (Join-Path $buildRoot "God2PacketCaptureInjector.exe") `
    -Destination (Join-Path $payloadRoot "God2PacketCaptureInjector.exe") `
    -ExpectedDll $false

[pscustomobject][ordered]@{
    SchemaId = "God2EmbeddedInstrumentationPayloadSync"
    SchemaVersion = 1
    Configuration = $Configuration
    Probe = $probe
    Injector = $injector
    Passed = $true
} | ConvertTo-Json -Depth 4 -Compress
