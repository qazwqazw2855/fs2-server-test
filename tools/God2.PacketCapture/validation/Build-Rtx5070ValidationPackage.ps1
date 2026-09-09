[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sourceDirectory = Split-Path -Parent $PSScriptRoot
$repositoryRoot = (Resolve-Path (Join-Path $sourceDirectory '..\..')).Path
$artifactRoot = Join-Path $repositoryRoot 'Artifacts'
$packageDirectory = Join-Path $artifactRoot 'Rtx5070ValidationPackage'
$packageZip = Join-Path $artifactRoot 'Rtx5070ValidationPackage.zip'
$releaseExecutable = Join-Path $repositoryRoot 'artifacts\release\God2PacketCapture.exe'

if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) {
    throw "Release executable is missing: $releaseExecutable"
}

$resolvedArtifactRoot = [IO.Path]::GetFullPath($artifactRoot)
$resolvedPackageDirectory = [IO.Path]::GetFullPath($packageDirectory)
if (-not $resolvedPackageDirectory.StartsWith($resolvedArtifactRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace a package directory outside Artifacts.'
}

if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'schemas') -Force | Out-Null

Copy-Item -LiteralPath $releaseExecutable -Destination (Join-Path $packageDirectory 'God2PacketCapture.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RUN-RTX5070-VALIDATION.cmd') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README-VALIDATION-zh-TW.txt') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'validation-result.schema.json') -Destination (Join-Path $packageDirectory 'schemas')

$artifactFiles = Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Sort-Object FullName
$manifestEntries = foreach ($file in $artifactFiles) {
    $relative = $file.FullName.Substring($resolvedPackageDirectory.Length + 1).Replace('\', '/')
    [ordered]@{
        RelativePath = $relative
        SizeBytes = [uint64]$file.Length
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}
$manifestLines = [Collections.Generic.List[string]]::new()
$manifestLines.Add('{')
$manifestLines.Add('  "SchemaVersion": 1,')
$manifestLines.Add('  "ValidationPackageVersion": "1.0.0",')
$manifestLines.Add('  "God2PacketCaptureVersion": "v1.2.0 RC",')
$manifestLines.Add('  "PEVersion": "1.2.0.0",')
$manifestLines.Add('  "ManifestSelfHashPolicy": "ManifestExcludedToAvoidRecursiveDigest",')
$manifestLines.Add("  `"ArtifactCount`": $(@($manifestEntries).Count),")
$manifestLines.Add('  "Artifacts": [')
for ($index = 0; $index -lt @($manifestEntries).Count; $index++) {
    $entry = $manifestEntries[$index] | ConvertTo-Json -Compress
    $suffix = if ($index -lt @($manifestEntries).Count - 1) { ',' } else { '' }
    $manifestLines.Add("    $entry$suffix")
}
$manifestLines.Add('  ]')
$manifestLines.Add('}')
$manifestLines | Set-Content -LiteralPath (Join-Path $packageDirectory 'validation-package-manifest.json') -Encoding utf8

if (Test-Path -LiteralPath $packageZip) {
    Remove-Item -LiteralPath $packageZip -Force
}
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $packageZip -CompressionLevel Optimal

$zipInfo = Get-Item -LiteralPath $packageZip
$zipHash = (Get-FileHash -LiteralPath $packageZip -Algorithm SHA256).Hash.ToUpperInvariant()
[ordered]@{
    PackagePath = $zipInfo.FullName
    PackageSize = [uint64]$zipInfo.Length
    PackageSHA256 = $zipHash
    PackageFileCount = (Get-ChildItem -LiteralPath $packageDirectory -File -Recurse).Count
    PackageIntegrityStatus = 'PASS'
    EngineeringStatus = 'RTX5070_VALIDATION_PACKAGE_READY_FOR_EXTERNAL_RUN'
} | ConvertTo-Json
