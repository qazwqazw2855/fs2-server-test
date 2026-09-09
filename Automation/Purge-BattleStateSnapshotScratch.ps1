param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,100}$')]
    [string] $SessionId
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$scratchRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "Artifacts\AnalysisScratch\BattleStateSnapshots"))
$target = [IO.Path]::GetFullPath((Join-Path $scratchRoot $SessionId))
$scratchPrefix = $scratchRoot.TrimEnd('\') + '\'
if (-not $target.StartsWith($scratchPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "State snapshot scratch purge boundary failed."
}

$manifestPath = Join-Path $target "manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Readable state snapshot manifest is missing."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
    ConvertFrom-Json
if ([string]$manifest.sessionId -cne $SessionId -or
    [bool]$manifest.rawPacketDataIncluded) {
    throw "State snapshot manifest does not authorize scratch deletion."
}

$snapshotFiles = @(Get-ChildItem -LiteralPath $target -Filter "*.bin" -File)
foreach ($file in $snapshotFiles) {
    Remove-Item -LiteralPath $file.FullName -Force
}

[pscustomobject][ordered]@{
    sessionId = $SessionId
    removedSnapshotFiles = $snapshotFiles.Count
    remainingSnapshotFiles = @(
        Get-ChildItem -LiteralPath $target -Filter "*.bin" -File).Count
    readableManifestRetained = $true
} | ConvertTo-Json
