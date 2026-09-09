[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:ScriptVersion = '1.0.0'
$script:BaselineId = 'God2PacketCapture-v1.2.0-RC'
$script:ManifestJsonRelative = 'manifests/public-baseline-manifest.json'
$script:ManifestCsvRelative = 'manifests/public-baseline-manifest.csv'

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-PathIsWithin {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $parentFull = (Get-NormalizedFullPath $Parent).TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $candidateFull = Get-NormalizedFullPath $Candidate
    return $candidateFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)
}

function Convert-ToForwardSlashPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return $Path.Replace([System.IO.Path]::DirectorySeparatorChar, '/').Replace([System.IO.Path]::AltDirectorySeparatorChar, '/')
}

function Assert-SafeRelativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Unsafe baseline relative path: '$RelativePath'"
    }

    $segments = (Convert-ToForwardSlashPath $RelativePath).Split('/')
    if ($segments -contains '..' -or $segments -contains '.' -or $segments -contains '') {
        throw "Unsafe baseline relative path: '$RelativePath'"
    }
}

function Write-NewUtf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    if (Test-Path -LiteralPath $Path) {
        throw "Refusing to overwrite baseline file: $Path"
    }
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        [void](New-Item -ItemType Directory -Path $directory)
    }
    [System.IO.File]::WriteAllText($Path, $Content, $script:Utf8NoBom)
}

function Get-FileDigestRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Classification
    )

    $relative = Convert-ToForwardSlashPath $File.FullName.Substring((Get-NormalizedFullPath $Root).TrimEnd('\').Length + 1)
    return [pscustomobject][ordered]@{
        RelativePath = $relative
        SizeBytes = [int64]$File.Length
        SHA256 = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash
        Classification = $Classification
        Source = $Source
    }
}

function Get-Classification {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ($RelativePath.StartsWith('release/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'ReleaseBinary' }
    if ($RelativePath.StartsWith('payload/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'EmbeddedPayloadSource' }
    if ($RelativePath.StartsWith('reports/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'HistoricalReport' }
    if ($RelativePath.StartsWith('regression/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'RegressionEvidence' }
    if ($RelativePath.StartsWith('schemas/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'EvidenceSchema' }
    if ($RelativePath.StartsWith('validation/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'ExternalValidationArtifact' }
    if ($RelativePath.StartsWith('evidence/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'AcceptedEvidenceFixture' }
    if ($RelativePath.StartsWith('identity/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'BaselineIdentity' }
    return 'BaselineDocumentation'
}

function Verify-PublicBaseline {
    param([Parameter(Mandatory = $true)][string]$Root)

    $rootFull = Get-NormalizedFullPath $Root
    $manifestPath = Join-Path $rootFull ($script:ManifestJsonRelative.Replace('/', '\'))
    $csvPath = Join-Path $rootFull ($script:ManifestCsvRelative.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Existing baseline has no authoritative manifest: $manifestPath"
    }
    if (-not (Test-Path -LiteralPath $csvPath -PathType Leaf)) {
        throw "Existing baseline has no CSV manifest: $csvPath"
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.BaselineId -ne $script:BaselineId) {
        throw "Baseline identity mismatch: expected $($script:BaselineId), observed $($manifest.BaselineId)"
    }

    $expected = @{}
    foreach ($artifact in $manifest.Artifacts) {
        $relative = [string]$artifact.RelativePath
        Assert-SafeRelativePath $relative
        if ($expected.ContainsKey($relative)) {
            throw "Duplicate manifest path: $relative"
        }
        $expected[$relative] = $artifact
        $full = Get-NormalizedFullPath (Join-Path $rootFull ($relative.Replace('/', '\')))
        if (-not (Test-PathIsWithin -Parent $rootFull -Candidate $full)) {
            throw "Manifest path escapes baseline root: $relative"
        }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Frozen artifact is missing: $relative"
        }
        $file = Get-Item -LiteralPath $full
        if ([int64]$file.Length -ne [int64]$artifact.SizeBytes) {
            throw "Frozen artifact size mismatch: $relative"
        }
        $actualHash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
        if ($actualHash -ne [string]$artifact.SHA256) {
            throw "Frozen artifact SHA-256 mismatch: $relative"
        }
    }

    $excluded = @($script:ManifestJsonRelative, $script:ManifestCsvRelative)
    $observed = Get-ChildItem -LiteralPath $rootFull -Recurse -File | ForEach-Object {
        Convert-ToForwardSlashPath $_.FullName.Substring($rootFull.TrimEnd('\').Length + 1)
    } | Where-Object { $_ -notin $excluded }
    foreach ($relative in $observed) {
        if (-not $expected.ContainsKey($relative)) {
            throw "Unmanifested file found in immutable baseline: $relative"
        }
    }
    if ($observed.Count -ne $expected.Count) {
        throw "Baseline artifact count mismatch: expected $($expected.Count), observed $($observed.Count)"
    }

    $csvRows = @(Import-Csv -LiteralPath $csvPath)
    if ($csvRows.Count -ne $expected.Count) {
        throw "CSV manifest count mismatch: expected $($expected.Count), observed $($csvRows.Count)"
    }
    foreach ($row in $csvRows) {
        if (-not $expected.ContainsKey([string]$row.RelativePath)) {
            throw "CSV manifest contains an unknown path: $($row.RelativePath)"
        }
        $jsonRow = $expected[[string]$row.RelativePath]
        if ([string]$row.SHA256 -ne [string]$jsonRow.SHA256 -or
            [int64]$row.SizeBytes -ne [int64]$jsonRow.SizeBytes) {
            throw "JSON/CSV manifest disagreement: $($row.RelativePath)"
        }
    }

    return [pscustomobject]@{
        Status = 'PUBLIC_BASELINE_INTEGRITY_VERIFIED_WITH_KNOWN_GAPS'
        BaselineId = $script:BaselineId
        Root = $rootFull
        ArtifactCount = $expected.Count
        KnownGapCount = [int]$manifest.KnownGapCount
        ManifestSHA256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
        CsvManifestSHA256 = (Get-FileHash -LiteralPath $csvPath -Algorithm SHA256).Hash
    }
}

$repoRoot = Get-NormalizedFullPath (Join-Path $PSScriptRoot '..\..\..')
$baselineParent = Get-NormalizedFullPath (Join-Path $repoRoot 'Artifacts\PublicBaseline')
$targetRoot = Get-NormalizedFullPath (Join-Path $baselineParent 'v1.2.0-RC')

if (Test-Path -LiteralPath $targetRoot) {
    $verification = Verify-PublicBaseline -Root $targetRoot
    $verification | ConvertTo-Json -Depth 5
    return
}

if (-not (Test-Path -LiteralPath $baselineParent)) {
    [void](New-Item -ItemType Directory -Path $baselineParent)
}

$stageRoot = Get-NormalizedFullPath (Join-Path $baselineParent ('.v1.2.0-RC.staging-' + [guid]::NewGuid().ToString('N')))
if (-not (Test-PathIsWithin -Parent $baselineParent -Candidate $stageRoot) -or
    -not ([System.IO.Path]::GetFileName($stageRoot).StartsWith('.v1.2.0-RC.staging-', [System.StringComparison]::Ordinal))) {
    throw "Unsafe baseline staging path: $stageRoot"
}

$script:Provenance = @{}

function Add-Provenance {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Source
    )
    $normalized = Convert-ToForwardSlashPath $RelativePath
    if ($script:Provenance.ContainsKey($normalized)) {
        throw "Duplicate baseline destination: $normalized"
    }
    $script:Provenance[$normalized] = $Source
}

function Copy-BaselineFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRelative,
        [Parameter(Mandatory = $true)][string]$DestinationRelative,
        [string]$SourceLabel
    )

    Assert-SafeRelativePath $DestinationRelative
    $sourcePath = Get-NormalizedFullPath (Join-Path $repoRoot $SourceRelative)
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required baseline source is missing: $SourceRelative"
    }
    $destinationPath = Get-NormalizedFullPath (Join-Path $stageRoot ($DestinationRelative.Replace('/', '\')))
    if (-not (Test-PathIsWithin -Parent $stageRoot -Candidate $destinationPath)) {
        throw "Baseline destination escapes staging root: $DestinationRelative"
    }
    if (Test-Path -LiteralPath $destinationPath) {
        throw "Refusing to overwrite staged baseline file: $DestinationRelative"
    }
    $destinationDirectory = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        [void](New-Item -ItemType Directory -Path $destinationDirectory)
    }
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
    if ([string]::IsNullOrWhiteSpace($SourceLabel)) {
        $SourceLabel = 'Workspace:' + (Convert-ToForwardSlashPath $SourceRelative)
    }
    Add-Provenance -RelativePath $DestinationRelative -Source $SourceLabel
}

function Copy-BaselineTree {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRelative,
        [Parameter(Mandatory = $true)][string]$DestinationRelative,
        [string]$Filter = '*'
    )

    $sourceRoot = Get-NormalizedFullPath (Join-Path $repoRoot $SourceRelative)
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
        throw "Required baseline source directory is missing: $SourceRelative"
    }
    $files = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter $Filter | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "No baseline files matched $SourceRelative / $Filter"
    }
    foreach ($file in $files) {
        $child = Convert-ToForwardSlashPath $file.FullName.Substring($sourceRoot.TrimEnd('\').Length + 1)
        Copy-BaselineFile -SourceRelative (Join-Path $SourceRelative $child.Replace('/', '\')) `
            -DestinationRelative ((Convert-ToForwardSlashPath $DestinationRelative).TrimEnd('/') + '/' + $child)
    }
}

try {
    [void](New-Item -ItemType Directory -Path $stageRoot)

    Copy-BaselineFile 'Artifacts\release\God2PacketCapture.exe' 'release/God2PacketCapture.exe'
    Copy-BaselineFile 'tools\God2.PacketCapture\payload\x86\God2PacketCaptureProbe.dll' 'payload/x86/God2PacketCaptureProbe.dll'
    Copy-BaselineFile 'tools\God2.PacketCapture\payload\x86\God2PacketCaptureInjector.exe' 'payload/x86/God2PacketCaptureInjector.exe'

    Copy-BaselineFile 'Artifacts\GpuAccelerationRc\GPU-ACCELERATION-RC-REPORT.md' 'reports/GPU-ACCELERATION-RC-REPORT.md'
    Copy-BaselineFile 'Artifacts\ProtocolSemantic\TARGET-MODE-FINAL-REPORT.md' 'reports/TARGET-MODE-FINAL-REPORT.md'
    Copy-BaselineFile 'Artifacts\ProtocolSemantic\target-mode-final-report.json' 'reports/target-mode-final-report.json'
    Copy-BaselineFile 'Artifacts\RTX5070-VALIDATION-PACKAGE-REPORT.md' 'reports/RTX5070-VALIDATION-PACKAGE-REPORT.md'

    $acceptedReports = @(
        'core-selftest-report.json',
        'semantic-selftest-report.json',
        'evidence-package-fixture-report.json',
        'gpu-contract-report.json',
        'gpu-benchmark-report.json',
        'cpu-gpu-package-equivalence-report.json',
        'gui-smoke-report.json',
        'current-implementation-matrix.json',
        'semantic-probe-plan.json',
        'performance-gate.json'
    )
    foreach ($name in $acceptedReports) {
        Copy-BaselineFile (Join-Path 'Artifacts\ProtocolSemantic' $name) ('regression/accepted/' + $name)
    }

    $laterReports = @(
        'semantic.json',
        'package-fixture.json',
        'package-fixture-source2.json',
        'gpu-contract.json',
        'gpu-benchmark.json',
        'gpu-package-equivalence.json',
        'gui-smoke.json'
    )
    foreach ($name in $laterReports) {
        Copy-BaselineFile (Join-Path 'Artifacts\Rtx5070ValidationChecks' $name) ('regression/post-validation-rebuild/' + $name)
    }
    Copy-BaselineFile 'Artifacts\Rtx5070ValidationChecks\DotNet\Rtx5070Validation.trx' 'regression/post-validation-rebuild/dotnet/Rtx5070Validation.trx'

    Copy-BaselineFile 'Artifacts\ClientInstrumentation\LoginTrial\gpu-benefit-gate-20260808\analysis\instrumentation-selftest-summary.json' `
        'regression/instrumentation/instrumentation-selftest-summary.json'
    Copy-BaselineFile 'Artifacts\ClientInstrumentation\LoginTrial\gpu-benefit-gate-product-injector-20260808\analysis\product-injector-selftest-summary.json' `
        'regression/instrumentation/product-injector-selftest-summary.json'

    Copy-BaselineTree 'Artifacts\GpuAccelerationRc\DotNetTestResultsBenefitGateFinal' 'regression/dotnet-2968' '*.trx'
    Copy-BaselineTree 'Artifacts\Rtx5070ValidationChecks\x64-analysis-final' 'regression/static-analysis/x64' '*.nativecodeanalysis.xml'
    Copy-BaselineTree 'Artifacts\GpuAccelerationRc\x86-analysis' 'regression/static-analysis/x86' '*.nativecodeanalysis.xml'

    $packageSource = Get-NormalizedFullPath (Join-Path $repoRoot 'Artifacts\Rtx5070ValidationPackage')
    $packageManifestSource = Join-Path $packageSource 'validation-package-manifest.json'
    $packageManifest = Get-Content -Raw -LiteralPath $packageManifestSource | ConvertFrom-Json
    foreach ($artifact in $packageManifest.Artifacts) {
        $relative = Convert-ToForwardSlashPath ([string]$artifact.RelativePath)
        Assert-SafeRelativePath $relative
        $sourceFile = Join-Path $packageSource ($relative.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "RTX validation package artifact is missing: $relative"
        }
        $sourceInfo = Get-Item -LiteralPath $sourceFile
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash
        if ([int64]$sourceInfo.Length -ne [int64]$artifact.SizeBytes -or $sourceHash -ne [string]$artifact.SHA256) {
            throw "RTX validation package manifest mismatch: $relative"
        }
        Copy-BaselineFile ('Artifacts\Rtx5070ValidationPackage\' + $relative.Replace('/', '\')) `
            ('validation/rtx5070-package-unpacked/' + $relative)
    }
    Copy-BaselineFile 'Artifacts\Rtx5070ValidationPackage\validation-package-manifest.json' `
        'validation/rtx5070-package-unpacked/validation-package-manifest.json'

    $resultZip = Get-ChildItem -LiteralPath $packageSource -File -Filter 'RTX5070-VALIDATION-RESULT-*.zip' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $resultZip) {
        throw 'Expected GTX 1080 validation self-test result ZIP is missing'
    }
    Copy-BaselineFile ('Artifacts\Rtx5070ValidationPackage\' + $resultZip.Name) `
        ('validation/gtx1080-selftest/result/' + $resultZip.Name)
    Copy-BaselineTree 'Artifacts\Rtx5070FinalSelfTestAudit' 'validation/gtx1080-selftest/audit'
    Copy-BaselineTree 'Artifacts\Rtx5070IntegrityNegativeAudit' 'validation/integrity-negative-audit'

    Copy-BaselineFile 'Artifacts\Rtx5070ValidationPackage\schemas\validation-result.schema.json' `
        'schemas/rtx5070-validation/validation-result.schema.json'

    $acceptedFixtureReportPath = Join-Path $repoRoot 'Artifacts\ProtocolSemantic\evidence-package-fixture-report.json'
    $acceptedFixture = Get-Content -Raw -LiteralPath $acceptedFixtureReportPath | ConvertFrom-Json
    if ([int]$acceptedFixture.CheckCount -ne 62 -or [int]$acceptedFixture.Passed -ne 62 -or [int]$acceptedFixture.Failed -ne 0) {
        throw 'The selected accepted Evidence fixture report is not the recorded 62/62 run'
    }
    $fixtureZipSource = Get-NormalizedFullPath ([string]$acceptedFixture.PackagePath)
    if (-not (Test-Path -LiteralPath $fixtureZipSource -PathType Leaf)) {
        throw "Accepted Evidence fixture ZIP is missing: $fixtureZipSource"
    }
    $fixtureZipHash = (Get-FileHash -LiteralPath $fixtureZipSource -Algorithm SHA256).Hash
    if ($fixtureZipHash -ne [string]$acceptedFixture.PackageSHA256) {
        throw 'Accepted Evidence fixture ZIP no longer matches its 62/62 report'
    }
    $fixtureZipName = [System.IO.Path]::GetFileName($fixtureZipSource)
    $fixtureDestinationRelative = 'evidence/accepted-fixture/' + $fixtureZipName
    $fixtureDestination = Join-Path $stageRoot ($fixtureDestinationRelative.Replace('/', '\'))
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $fixtureDestination))
    Copy-Item -LiteralPath $fixtureZipSource -Destination $fixtureDestination
    Add-Provenance -RelativePath $fixtureDestinationRelative -Source ('AcceptedFixtureReport:' + $fixtureZipName)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $fixtureArchive = [System.IO.Compression.ZipFile]::OpenRead($fixtureZipSource)
    try {
        $manifestEntry = $fixtureArchive.GetEntry('manifest.json')
        if ($null -eq $manifestEntry) { throw 'Accepted Evidence ZIP has no manifest.json' }
        $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
        try { $fixtureManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        $schemaArtifacts = @($fixtureManifest.Artifacts | Where-Object { ([string]$_.RelativePath).StartsWith('schemas/') })
        if ($schemaArtifacts.Count -ne 28) {
            throw "Accepted Evidence ZIP schema inventory mismatch: expected 28, observed $($schemaArtifacts.Count)"
        }
        foreach ($schemaArtifact in $schemaArtifacts) {
            $entryPath = Convert-ToForwardSlashPath ([string]$schemaArtifact.RelativePath)
            Assert-SafeRelativePath $entryPath
            $entry = $fixtureArchive.GetEntry($entryPath)
            if ($null -eq $entry) { throw "Accepted Evidence schema entry is missing: $entryPath" }
            $destinationRelative = 'schemas/evidence-v11/' + $entryPath.Substring('schemas/'.Length)
            $destination = Join-Path $stageRoot ($destinationRelative.Replace('/', '\'))
            if (Test-Path -LiteralPath $destination) { throw "Duplicate extracted schema: $destinationRelative" }
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
            $entryStream = $entry.Open()
            $outputStream = [System.IO.File]::Open(
                $destination,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            try { $entryStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $entryStream.Dispose() }
            $schemaInfo = Get-Item -LiteralPath $destination
            $schemaHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if ([int64]$schemaInfo.Length -ne [int64]$schemaArtifact.FileSize -or $schemaHash -ne [string]$schemaArtifact.SHA256) {
                throw "Extracted Evidence schema failed manifest validation: $entryPath"
            }
            Add-Provenance -RelativePath $destinationRelative -Source ($fixtureDestinationRelative + '!/' + $entryPath)
        }
    } finally {
        $fixtureArchive.Dispose()
    }

    $releasePath = Join-Path $repoRoot 'Artifacts\release\God2PacketCapture.exe'
    $probePath = Join-Path $repoRoot 'tools\God2.PacketCapture\payload\x86\God2PacketCaptureProbe.dll'
    $injectorPath = Join-Path $repoRoot 'tools\God2.PacketCapture\payload\x86\God2PacketCaptureInjector.exe'
    $releaseInfo = Get-Item -LiteralPath $releasePath
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($releasePath)
    $releaseBytes = [System.IO.File]::ReadAllBytes($releasePath)
    $peOffset = [BitConverter]::ToInt32($releaseBytes, 0x3c)
    $machine = [BitConverter]::ToUInt16($releaseBytes, $peOffset + 4)
    $optionalMagic = [BitConverter]::ToUInt16($releaseBytes, $peOffset + 24)
    $subsystem = [BitConverter]::ToUInt16($releaseBytes, $peOffset + 24 + 68)

    $resourceReaderSource = @'
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public static class God2PublicBaselineResourceReader {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    public static string ReadInfo(string path, int id) {
        IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) throw new InvalidOperationException("LoadLibraryEx failed: " + Marshal.GetLastWin32Error());
        try {
            IntPtr resource = FindResource(module, (IntPtr)id, (IntPtr)10);
            if (resource == IntPtr.Zero) throw new InvalidOperationException("FindResource failed: " + Marshal.GetLastWin32Error());
            uint size = SizeofResource(module, resource);
            IntPtr loaded = LoadResource(module, resource);
            IntPtr pointer = LockResource(loaded);
            byte[] bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, (int)size);
            using (SHA256 sha = SHA256.Create()) {
                return size.ToString() + "|" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            }
        } finally { FreeLibrary(module); }
    }
}
'@
    Add-Type -TypeDefinition $resourceReaderSource
    $probeEmbedded = [God2PublicBaselineResourceReader]::ReadInfo($releasePath, 201).Split('|')
    $injectorEmbedded = [God2PublicBaselineResourceReader]::ReadInfo($releasePath, 202).Split('|')
    $probeHash = (Get-FileHash -LiteralPath $probePath -Algorithm SHA256).Hash
    $injectorHash = (Get-FileHash -LiteralPath $injectorPath -Algorithm SHA256).Hash
    if ([int64]$probeEmbedded[0] -ne (Get-Item $probePath).Length -or $probeEmbedded[1] -ne $probeHash) {
        throw 'Embedded Probe does not match the archived payload source'
    }
    if ([int64]$injectorEmbedded[0] -ne (Get-Item $injectorPath).Length -or $injectorEmbedded[1] -ne $injectorHash) {
        throw 'Embedded Injector does not match the archived payload source'
    }

    $laterFixture = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'Artifacts\Rtx5070ValidationChecks\package-fixture.json') | ConvertFrom-Json
    $laterFixtureFailure = @($laterFixture.Checks | Where-Object { $_.Status -ne 'PASS' })
    $gtxSummary = Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'Artifacts\Rtx5070FinalSelfTestAudit\validation-summary.json') | ConvertFrom-Json

    $reportedOuterZip = Join-Path $repoRoot 'Artifacts\Rtx5070ValidationPackage.zip'
    $reportedOuterZipPresent = Test-Path -LiteralPath $reportedOuterZip -PathType Leaf
    if ($reportedOuterZipPresent) {
        Copy-BaselineFile 'Artifacts\Rtx5070ValidationPackage.zip' 'validation/Rtx5070ValidationPackage.zip'
    }

    $historicalHashes = @(
        'E39BFEA6B53E93598607CF3FE8DC85E04C69642B4BF6FD983C9681392FA7B781',
        '2D02D1C2DF4D3E64185435F702C5B8F11262A9749282453E9AC9EBAE767B5C82'
    )
    $historicalBinaryPresence = @{}
    foreach ($hash in $historicalHashes) { $historicalBinaryPresence[$hash] = @() }
    foreach ($candidate in Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter 'God2PacketCapture.exe' -ErrorAction SilentlyContinue) {
        if (Test-PathIsWithin -Parent $stageRoot -Candidate $candidate.FullName) { continue }
        $candidateHash = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash
        if ($historicalBinaryPresence.ContainsKey($candidateHash)) {
            $historicalBinaryPresence[$candidateHash] += 'Workspace:' + (Convert-ToForwardSlashPath $candidate.FullName.Substring($repoRoot.TrimEnd('\').Length + 1))
        }
    }

    $physicalRtxResults = @()
    $resultCandidates = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Artifacts') -Recurse -File -Filter 'RTX5070-VALIDATION-RESULT-*.zip' -ErrorAction SilentlyContinue
    foreach ($candidate in $resultCandidates) {
        $archive = $null
        try {
            $archive = [System.IO.Compression.ZipFile]::OpenRead($candidate.FullName)
            $summaryEntry = $archive.GetEntry('validation-summary.json')
            if ($null -eq $summaryEntry) { continue }
            $summaryReader = New-Object System.IO.StreamReader($summaryEntry.Open())
            try { $summary = $summaryReader.ReadToEnd() | ConvertFrom-Json } finally { $summaryReader.Dispose() }
            if ([string]$summary.ObservedGpu -eq 'NVIDIA GeForce RTX 5070' -and
                [string]$summary.FinalStatus -eq 'RTX5070_PHYSICAL_VALIDATION_PASS') {
                $physicalRtxResults += $candidate
            }
        } catch {
            # A malformed or negative-test ZIP is not RTX 5070 PASS evidence.
        } finally {
            if ($null -ne $archive) { $archive.Dispose() }
        }
    }
    foreach ($physicalResult in $physicalRtxResults) {
        $shortHash = ((Get-FileHash -LiteralPath $physicalResult.FullName -Algorithm SHA256).Hash).Substring(0, 12)
        $destinationRelative = 'validation/rtx5070-physical-results/' + $shortHash + '-' + $physicalResult.Name
        $destination = Join-Path $stageRoot ($destinationRelative.Replace('/', '\'))
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force)
        Copy-Item -LiteralPath $physicalResult.FullName -Destination $destination
        Add-Provenance -RelativePath $destinationRelative -Source ('WorkspaceRTX5070Result:' + $physicalResult.Name)
    }

    $createdAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $knownGaps = @(
        [ordered]@{
            Id = 'HISTORICAL_GPU_RC_EXE_NOT_PRESENT'
            Severity = 'IdentityGap'
            Status = 'KNOWN_GAP'
            Evidence = 'GPU-ACCELERATION-RC-REPORT.md records E39BFEA... but no matching God2PacketCapture.exe exists in the workspace.'
        },
        [ordered]@{
            Id = 'HISTORICAL_TARGET_MODE_EXE_NOT_PRESENT'
            Severity = 'IdentityGap'
            Status = 'KNOWN_GAP'
            Evidence = 'TARGET-MODE-FINAL-REPORT records v1.1.0 EXE 2D02D1... but no matching God2PacketCapture.exe exists in the workspace.'
        },
        [ordered]@{
            Id = 'RTX5070_OUTER_PACKAGE_ZIP_MISSING'
            Severity = 'ArtifactGap'
            Status = $(if ($reportedOuterZipPresent) { 'RESOLVED_PRESENT' } else { 'KNOWN_GAP' })
            Evidence = 'The report names Artifacts/Rtx5070ValidationPackage.zip with SHA-256 FAE918..., but the exact outer ZIP was not present during inventory; the manifest-validated unpacked five-file package is archived.'
        },
        [ordered]@{
            Id = 'RTX5070_PHYSICAL_RESULT_PENDING'
            Severity = 'ExternalGate'
            Status = $(if ($physicalRtxResults.Count -gt 0) { 'RESULT_PRESENT' } else { 'PENDING_EXTERNAL_RUN' })
            Evidence = 'No result with exact ObservedGpu NVIDIA GeForce RTX 5070 and RTX5070_PHYSICAL_VALIDATION_PASS was found. The archived local result is Windows 10 + GTX 1080 and is blocked by wrong OS/GPU.'
        },
        [ordered]@{
            Id = 'POST_VALIDATION_FIXTURE_61_OF_62'
            Severity = 'RegressionDiscrepancy'
            Status = 'KNOWN_FAILING_CHECK'
            Evidence = 'The later Rtx5070ValidationChecks package fixture reports 61/62; check 34_codex_gameplay_and_coverage_artifacts failed. The earlier accepted ProtocolSemantic fixture remains archived byte-for-byte as 62/62 evidence.'
        },
        [ordered]@{
            Id = 'HISTORICAL_REPORT_TEXT_ENCODING'
            Severity = 'DocumentationQuality'
            Status = 'BYTE_PRESERVED_NOT_REPAIRED'
            Evidence = 'The GPU RC and RTX validation Markdown reports contain visibly mojibaked Traditional Chinese in the source bytes. Immutable baseline copies preserve those bytes and do not silently rewrite history.'
        },
        [ordered]@{
            Id = 'RELEASE_EXE_UNSIGNED'
            Severity = 'DistributionRisk'
            Status = 'KNOWN_GAP'
            Evidence = 'The frozen v1.2.0 RC EXE has no Authenticode signature.'
        }
    )

    $identity = [ordered]@{
        SchemaVersion = 1
        BaselineId = $script:BaselineId
        FrozenAtUtc = $createdAt
        FreezeStatus = 'PUBLIC_BASELINE_FROZEN_WITH_KNOWN_GAPS'
        ReleaseStatus = 'RELEASE_BLOCKED'
        PublicFinalAsserted = $false
        Release = [ordered]@{
            RelativePath = 'release/God2PacketCapture.exe'
            DisplayVersion = 'v1.2.0 RC'
            FileVersion = $versionInfo.FileVersion
            ProductVersion = $versionInfo.ProductVersion
            SizeBytes = [int64]$releaseInfo.Length
            SHA256 = (Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
            Machine = ('0x{0:X4}' -f $machine)
            OptionalHeaderMagic = ('0x{0:X4}' -f $optionalMagic)
            Subsystem = [int]$subsystem
            Architecture = 'x64'
            AuthenticodeStatus = 'NotSigned'
        }
        EmbeddedPayloads = @(
            [ordered]@{
                ResourceId = 201
                Name = 'God2PacketCaptureProbe.dll'
                SourceRelativePath = 'payload/x86/God2PacketCaptureProbe.dll'
                SizeBytes = [int64]$probeEmbedded[0]
                SHA256 = $probeEmbedded[1]
                EmbeddedMatchesArchivedSource = $true
            },
            [ordered]@{
                ResourceId = 202
                Name = 'God2PacketCaptureInjector.exe'
                SourceRelativePath = 'payload/x86/God2PacketCaptureInjector.exe'
                SizeBytes = [int64]$injectorEmbedded[0]
                SHA256 = $injectorEmbedded[1]
                EmbeddedMatchesArchivedSource = $true
            }
        )
        HistoricalReportIdentities = @(
            [ordered]@{
                Report = 'reports/GPU-ACCELERATION-RC-REPORT.md'
                ReferencedExeSHA256 = $historicalHashes[0]
                MatchingBinaryPresent = ($historicalBinaryPresence[$historicalHashes[0]].Count -gt 0)
                MatchingPaths = @($historicalBinaryPresence[$historicalHashes[0]])
            },
            [ordered]@{
                Report = 'reports/TARGET-MODE-FINAL-REPORT.md'
                ReferencedExeSHA256 = $historicalHashes[1]
                MatchingBinaryPresent = ($historicalBinaryPresence[$historicalHashes[1]].Count -gt 0)
                MatchingPaths = @($historicalBinaryPresence[$historicalHashes[1]])
            }
        )
        Regression = [ordered]@{
            AcceptedFixture = [ordered]@{
                Report = 'regression/accepted/evidence-package-fixture-report.json'
                Passed = [int]$acceptedFixture.Passed
                Failed = [int]$acceptedFixture.Failed
                CheckCount = [int]$acceptedFixture.CheckCount
                EvidenceZipSHA256 = [string]$acceptedFixture.PackageSHA256
            }
            LaterPostValidationFixture = [ordered]@{
                Report = 'regression/post-validation-rebuild/package-fixture.json'
                Passed = [int]$laterFixture.Passed
                Failed = [int]$laterFixture.Failed
                CheckCount = [int]$laterFixture.CheckCount
                FailedChecks = @($laterFixtureFailure | ForEach-Object { [string]$_.Name })
            }
            AggregateStatus = 'KNOWN_DISCREPANCY_NOT_ALL_GREEN'
        }
        Rtx5070Validation = [ordered]@{
            TargetGpu = 'NVIDIA GeForce RTX 5070'
            TargetOs = 'Windows 11 x64'
            PhysicalResultCount = $physicalRtxResults.Count
            Status = $(if ($physicalRtxResults.Count -gt 0) { 'RESULT_PRESENT_REQUIRES_SEPARATE_AUDIT' } else { 'PENDING_EXTERNAL_RUN' })
            LocalSelfTestObservedGpu = [string]$gtxSummary.ObservedGpu
            LocalSelfTestObservedOsGate = [string]$gtxSummary.ObservedOsGate
            LocalSelfTestFinalStatus = [string]$gtxSummary.FinalStatus
            LocalSelfTestIsRtx5070Pass = $false
            ReportedOuterPackagePath = 'Artifacts/Rtx5070ValidationPackage.zip'
            ReportedOuterPackageSHA256 = 'FAE918596EC255E1E6E97B2114AC2622C16AFBCA78079EAEAE3F6F18633795DE'
            ReportedOuterPackagePresent = $reportedOuterZipPresent
            UnpackedPackageArchived = $true
        }
        EvidenceSchemas = [ordered]@{
            SourceFixtureSHA256 = [string]$acceptedFixture.PackageSHA256
            ExtractedAndManifestVerifiedCount = $schemaArtifacts.Count
            SchemaVersion = 11
        }
        KnownGaps = $knownGaps
        DoesNotAssert = @('ULTIMATE PASS', 'COMPLETE', 'PUBLIC FINAL', 'RELEASE_PASS', 'RTX5070_PHYSICAL_VALIDATION_PASS')
    }

    $identityRelative = 'identity/baseline-identity.json'
    Write-NewUtf8File -Path (Join-Path $stageRoot ($identityRelative.Replace('/', '\'))) `
        -Content (($identity | ConvertTo-Json -Depth 12) + "`n")
    Add-Provenance -RelativePath $identityRelative -Source 'GeneratedBy:Freeze-PublicBaseline.ps1'

    $knownGapsMarkdown = @'
# God2PacketCapture v1.2.0 RC immutable baseline known gaps

Freeze status: `PUBLIC_BASELINE_FROZEN_WITH_KNOWN_GAPS`

This archive is an identity-preserving engineering baseline. It does **not** assert `PUBLIC FINAL`, `RELEASE_PASS`, `RTX5070_PHYSICAL_VALIDATION_PASS`, or any Ultimate completion status.

## Identity discontinuities

- The frozen current EXE is `2787719E19901760C79B01AE58D4DB91F51C7DDEEB9411316B7B399532DFA0E4` (4,288,512 bytes, PE 1.2.0.0).
- `GPU-ACCELERATION-RC-REPORT.md` names an earlier EXE hash `E39BFEA6B53E93598607CF3FE8DC85E04C69642B4BF6FD983C9681392FA7B781`. No matching binary was present, so the report is retained unchanged and the missing binary is explicit.
- `TARGET-MODE-FINAL-REPORT.md/json` names the earlier v1.1.0 EXE hash `2D02D1C2DF4D3E64185435F702C5B8F11262A9749282453E9AC9EBAE767B5C82`. No matching binary was present.
- The two x86 payload source files were compared directly with PE RCDATA resources 201/202 and match exactly. Their hashes are recorded in `baseline-identity.json`.

## RTX 5070 external gate

- No exact RTX 5070 physical PASS result exists in the inventoried workspace. Status remains `PENDING_EXTERNAL_RUN`.
- The archived physical self-test is Windows 10 + NVIDIA GeForce GTX 1080 and ends in `RTX5070_PHYSICAL_VALIDATION_BLOCKED_WRONG_GPU`; it is not RTX 5070 evidence.
- The package report names `Artifacts/Rtx5070ValidationPackage.zip` (`FAE918596EC255E1E6E97B2114AC2622C16AFBCA78079EAEAE3F6F18633795DE`), but that exact outer ZIP was absent at freeze time. The manifest-validated five-file unpacked package is preserved instead.

## Regression discrepancy

- Accepted pre-package evidence: `regression/accepted/evidence-package-fixture-report.json` is 62/62.
- Later post-validation rebuild evidence: `regression/post-validation-rebuild/package-fixture.json` and `package-fixture-source2.json` are 61/62. The failing check is `34_codex_gameplay_and_coverage_artifacts`.
- Both outcomes are archived. Therefore the baseline is **not represented as all green**.

## Other preservation risks

- The frozen EXE is not Authenticode signed.
- Two historical Traditional Chinese Markdown reports contain mojibaked source text. The immutable archive preserves the original bytes and does not silently repair or replace historical reporting.
'@
    $knownGapsRelative = 'identity/KNOWN-GAPS.md'
    Write-NewUtf8File -Path (Join-Path $stageRoot ($knownGapsRelative.Replace('/', '\'))) -Content $knownGapsMarkdown
    Add-Provenance -RelativePath $knownGapsRelative -Source 'GeneratedBy:Freeze-PublicBaseline.ps1'

    $readme = @'
# God2PacketCapture v1.2.0 RC public baseline

This directory is a byte-preserving, read-only development baseline created by `Freeze-PublicBaseline.ps1`.

- Integrity status after creation: `PUBLIC_BASELINE_INTEGRITY_VERIFIED_WITH_KNOWN_GAPS`
- Engineering status: `PUBLIC_BASELINE_FROZEN_WITH_KNOWN_GAPS`
- Release/public-final status: not asserted
- RTX 5070 physical validation: `PENDING_EXTERNAL_RUN`

`manifests/public-baseline-manifest.json` and `.csv` inventory every non-manifest file by relative path, size, SHA-256, classification, and source. The manifest files themselves are excluded to avoid recursive self-digests. Re-running the freeze script verifies this existing directory and never overwrites it.

Read `identity/KNOWN-GAPS.md` before treating any historical report as the identity of the current frozen EXE.
'@
    $readmeRelative = 'README.md'
    Write-NewUtf8File -Path (Join-Path $stageRoot $readmeRelative) -Content $readme
    Add-Provenance -RelativePath $readmeRelative -Source 'GeneratedBy:Freeze-PublicBaseline.ps1'

    foreach ($generatedTextRelative in @($knownGapsRelative, $readmeRelative)) {
        $generatedTextPath = Join-Path $stageRoot ($generatedTextRelative.Replace('/', '\'))
        $generatedText = [System.IO.File]::ReadAllText($generatedTextPath)
        if ($generatedText -match '[\x00-\x08\x0B\x0C\x0E-\x1F]') {
            throw "Generated baseline documentation contains a forbidden control character: $generatedTextRelative"
        }
    }

    $artifactRecords = @()
    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File | Sort-Object FullName) {
        $relative = Convert-ToForwardSlashPath $file.FullName.Substring($stageRoot.TrimEnd('\').Length + 1)
        if ($relative -in @($script:ManifestJsonRelative, $script:ManifestCsvRelative)) { continue }
        if (-not $script:Provenance.ContainsKey($relative)) {
            throw "No provenance was recorded for staged file: $relative"
        }
        $artifactRecords += Get-FileDigestRecord -Root $stageRoot -File $file `
            -Source ([string]$script:Provenance[$relative]) -Classification (Get-Classification $relative)
    }

    $manifest = [ordered]@{
        SchemaVersion = 1
        BaselineId = $script:BaselineId
        CreatedAtUtc = $createdAt
        Status = 'PUBLIC_BASELINE_FROZEN_WITH_KNOWN_GAPS'
        ArtifactCount = $artifactRecords.Count
        KnownGapCount = $knownGaps.Count
        ManifestSelfHashPolicy = 'The JSON and CSV manifests are excluded from artifact digests to avoid recursive self-hashes.'
        ExcludedFromArtifactDigest = @($script:ManifestJsonRelative, $script:ManifestCsvRelative)
        Artifacts = $artifactRecords
    }
    $manifestJsonPath = Join-Path $stageRoot ($script:ManifestJsonRelative.Replace('/', '\'))
    Write-NewUtf8File -Path $manifestJsonPath -Content (($manifest | ConvertTo-Json -Depth 10) + "`n")

    $csvLines = $artifactRecords | Select-Object RelativePath, SizeBytes, SHA256, Classification, Source | ConvertTo-Csv -NoTypeInformation
    $manifestCsvPath = Join-Path $stageRoot ($script:ManifestCsvRelative.Replace('/', '\'))
    Write-NewUtf8File -Path $manifestCsvPath -Content (($csvLines -join "`r`n") + "`r`n")

    [void](Verify-PublicBaseline -Root $stageRoot)

    foreach ($file in Get-ChildItem -LiteralPath $stageRoot -Recurse -File) {
        $file.IsReadOnly = $true
    }

    if (Test-Path -LiteralPath $targetRoot) {
        throw "Baseline target appeared during creation; refusing to overwrite it: $targetRoot"
    }
    if (-not (Test-PathIsWithin -Parent $baselineParent -Candidate $stageRoot) -or
        -not (Test-PathIsWithin -Parent $baselineParent -Candidate $targetRoot)) {
        throw 'Refusing unsafe baseline move'
    }
    Move-Item -LiteralPath $stageRoot -Destination $targetRoot

    $finalVerification = Verify-PublicBaseline -Root $targetRoot
    $finalVerification | ConvertTo-Json -Depth 5
} finally {
    if (Test-Path -LiteralPath $stageRoot) {
        if ((Test-PathIsWithin -Parent $baselineParent -Candidate $stageRoot) -and
            [System.IO.Path]::GetFileName($stageRoot).StartsWith('.v1.2.0-RC.staging-', [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $stageRoot -Recurse -Force
        } else {
            throw "Refusing to clean unsafe staging directory: $stageRoot"
        }
    }
}
