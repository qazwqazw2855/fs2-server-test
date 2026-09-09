param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Attach', 'Detach')]
    [string] $Action,
    [Parameter(Mandatory = $true)]
    [int] $ClientPid,
    [Parameter(Mandatory = $true)]
    [string] $RunDir,
    [string] $Module = '',
    [string] $ClientExecutablePath = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedRunDir = [IO.Path]::GetFullPath($RunDir)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedRunDir.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RunDir must be under the repository root.'
}

$client = Get-Process -Id $ClientPid -ErrorAction Stop
if ($client.ProcessName -cne 'God2_opt') {
    throw "Refusing to attach to unexpected process: $($client.ProcessName)"
}
$clientPath = if ([string]::IsNullOrWhiteSpace($ClientExecutablePath)) {
    [string]$client.Path
} else {
    [IO.Path]::GetFullPath($ClientExecutablePath)
}
if ([string]::IsNullOrWhiteSpace($clientPath) -or -not (Test-Path -LiteralPath $clientPath -PathType Leaf)) {
    throw 'The client executable path is unavailable. Supply -ClientExecutablePath explicitly.'
}
if ([IO.Path]::GetFileName($clientPath) -cne 'god2_opt.exe') {
    throw "Refusing unexpected client executable: $clientPath"
}
$clientSha256 = (Get-FileHash -LiteralPath $clientPath -Algorithm SHA256).Hash.ToUpperInvariant()
$clientVersion = [string](Get-Item -LiteralPath $clientPath).VersionInfo.FileVersion
$clientCreationTime = $client.StartTime.ToUniversalTime().ToFileTimeUtc()

$buildDir = Join-Path $repoRoot 'Build\ClientInstrumentation\Release'
$launcher = Join-Path $buildDir 'God2ClientTraceLauncher.exe'
$sourceDll = Join-Path $buildDir 'God2ClientTraceProbe.dll'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf) -or
    -not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
    throw 'Validated Release instrumentation artifacts are missing.'
}

New-Item -ItemType Directory -Force -Path $resolvedRunDir | Out-Null
$resultPath = Join-Path $resolvedRunDir ("direct-$($Action.ToLowerInvariant())-result.json")

if ($Action -eq 'Attach') {
    $sensitive = Join-Path $resolvedRunDir 'sensitive'
    New-Item -ItemType Directory -Force -Path $sensitive | Out-Null
    $probeDll = Join-Path $resolvedRunDir 'God2ClientTraceProbe.dll'
    Copy-Item -LiteralPath $sourceDll -Destination $probeDll -Force
    $generalLog = Join-Path $resolvedRunDir 'general.log'
    $metadata = Join-Path $resolvedRunDir 'metadata.jsonl'
    @(
        "traceDir=$sensitive"
        "generalLog=$generalLog"
        "metadata=$metadata"
        'clientBuildVerified=true'
        "clientVersion=$clientVersion"
        "clientSha256=$clientSha256"
        "clientProcessId=$ClientPid"
        "clientProcessCreationTime=$clientCreationTime"
        'enableInline=true'
        'enableInputInline=false'
        'enableVersionProbe=true'
    ) | Set-Content -LiteralPath (Join-Path $resolvedRunDir 'God2ClientTraceProbe.attach.env') -Encoding ASCII

    $output = @(& $launcher --attach --pid $ClientPid --dll $probeDll 2>&1)
    $exitCode = $LASTEXITCODE
    $moduleLine = @($output) | Where-Object { $_ -match '^MODULE=' } | Select-Object -First 1
    $moduleValue = if ($moduleLine) { ([string]$moduleLine -replace '^MODULE=', '').Trim() } else { '' }
    $result = [ordered]@{
        action = 'Attach'
        clientPid = $ClientPid
        clientPath = $clientPath
        clientSha256 = $clientSha256
        clientVersion = $clientVersion
        clientProcessCreationTime = $clientCreationTime
        launcherSha256 = (Get-FileHash -LiteralPath $launcher -Algorithm SHA256).Hash
        probeSha256 = (Get-FileHash -LiteralPath $probeDll -Algorithm SHA256).Hash
        module = $moduleValue
        traceRoot = $resolvedRunDir
        traceBin = Join-Path $sensitive 'trace.bin'
        metadata = $metadata
        generalLog = $generalLog
        output = @($output)
        exitCode = $exitCode
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($moduleValue)) {
        throw "Direct packet capture attach failed with exit code $exitCode."
    }
    $result | ConvertTo-Json -Depth 8
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Module)) {
    throw 'Detach requires Module.'
}
$output = @(& $launcher --detach --pid $ClientPid --module $Module 2>&1)
$exitCode = $LASTEXITCODE
$result = [ordered]@{
    action = 'Detach'
    clientPid = $ClientPid
    module = $Module
    output = @($output)
    exitCode = $exitCode
    clientStillRunning = ($null -ne (Get-Process -Id $ClientPid -ErrorAction SilentlyContinue))
    completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
$result | ConvertTo-Json -Depth 8
exit $exitCode
