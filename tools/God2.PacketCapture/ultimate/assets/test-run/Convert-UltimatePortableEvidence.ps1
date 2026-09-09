[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PortableEvidence.ps1')

function Full([string]$Path) { [System.IO.Path]::GetFullPath($Path) }
function Within([string]$Parent, [string]$Candidate) {
    (Full $Candidate).StartsWith((Full $Parent).TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)
}
function NoReparse([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Portable evidence input may not be a reparse point: $Path"
    }
    if ($item.PSIsContainer) {
        $bad = Get-ChildItem -LiteralPath $item.FullName -Recurse -Force |
            Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 } |
            Select-Object -First 1
        if ($null -ne $bad) { throw "Portable evidence input contains a reparse point: $($bad.Name)" }
    }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }

$input = Full $InputRoot
$output = Full $OutputRoot
if (-not (Test-Path -LiteralPath $input -PathType Container)) { throw "Input report root is missing: $input" }
if (Test-Path -LiteralPath $output) { throw "Refusing to overwrite portable report root: $output" }
if ($input -eq $output -or (Within $input $output) -or (Within $output $input)) {
    throw 'InputRoot and OutputRoot must be separate, non-nested directories'
}
NoReparse $input
$outputParent = Split-Path -Parent $output
if (-not (Test-Path -LiteralPath $outputParent)) { [void](New-Item -ItemType Directory -Path $outputParent) }
$stage = Full (Join-Path $outputParent ('.' + [System.IO.Path]::GetFileName($output) + '.portable-' + [guid]::NewGuid().ToString('N')))
if (-not (Within $outputParent $stage)) { throw 'Unsafe portable evidence staging root' }
$committed = $false
$trxCount = 0
$fileCount = 0
try {
    [void](New-Item -ItemType Directory -Path $stage)
    foreach ($file in Get-ChildItem -LiteralPath $input -Recurse -File | Sort-Object FullName) {
        $relative = $file.FullName.Substring($input.TrimEnd('\').Length + 1)
        if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative) -or
            $relative.Split([char[]]@('\', '/')) -contains '..') {
            throw "Unsafe report-tree relative path: $relative"
        }
        $destination = Full (Join-Path $stage $relative)
        if (-not (Within $stage $destination)) { throw "Report-tree path escaped staging root: $relative" }
        $directory = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory) }
        if ($file.Extension -ieq '.trx') {
            [void](Convert-UltimatePortableTrxFile $file.FullName $destination)
            $trxCount++
        } else {
            [System.IO.File]::Copy($file.FullName, $destination, $false)
            if ($file.Length -ne (Get-Item -LiteralPath $destination).Length -or
                (Hash $file.FullName) -ne (Hash $destination)) {
                throw "Non-TRX evidence copy changed bytes: $relative"
            }
        }
        $fileCount++
    }
    $scan = Assert-UltimatePortableEvidenceTree $stage
    if (Test-Path -LiteralPath $output) { throw "Portable report root appeared during canonicalization: $output" }
    Move-Item -LiteralPath $stage -Destination $output
    $committed = $true
    [pscustomobject][ordered]@{
        Status = 'ULTIMATE_PORTABLE_EVIDENCE_CANONICALIZATION_PASS'
        FileCount = $fileCount
        TrxCanonicalizedCount = $trxCount
        PortableTextFileCount = $scan.TextFileCount
        OutputRoot = $output
    } | ConvertTo-Json -Depth 4
} finally {
    if (-not $committed -and (Test-Path -LiteralPath $stage)) {
        if ((Within $outputParent $stage) -and [System.IO.Path]::GetFileName($stage).StartsWith('.' + [System.IO.Path]::GetFileName($output) + '.portable-')) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        } else {
            throw "Refusing unsafe portable evidence cleanup: $stage"
        }
    }
}
