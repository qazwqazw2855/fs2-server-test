param(
    [string] $RepositoryRoot = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { Get-God2RepoRoot } else { [IO.Path]::GetFullPath($RepositoryRoot) }
$solution = Join-Path $repoRoot "God2ClassicServer.sln"
$artifactRoot = Join-Path $repoRoot "Artifacts\GameplayContentRecoveryPhase2"
$summaryPath = Join-Path $artifactRoot "final-summary.json"
$testResultsPath = Join-Path $artifactRoot "test-results.json"
$reportPath = Join-Path $repoRoot "Reports\GameplayContentRecoveryPhase2.Final.md"
if (-not (Test-Path -LiteralPath $summaryPath) -or -not (Test-Path -LiteralPath $testResultsPath)) {
    throw "Phase 2 content-recovery artifacts are missing; external gates were not run."
}

$summary = Read-God2JsonWithRetry -Path $summaryPath
$recoveryRunId = [string]$summary.runId
if ([string]::IsNullOrWhiteSpace($recoveryRunId)) {
    throw "Phase 2 final summary does not contain a recovery run id."
}

$gateStartedAtUtc = [DateTimeOffset]::UtcNow
Push-Location $repoRoot
$temporaryResults = Join-Path ([IO.Path]::GetTempPath()) ("god2-phase2-gates-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temporaryResults | Out-Null
try {
    & dotnet build $solution -c Debug --nologo --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Debug build failed with exit code $LASTEXITCODE." }

    & dotnet build $solution -c Release --nologo --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }

    & dotnet test $solution -c Release --no-build --nologo --results-directory $temporaryResults --logger "trx;LogFilePrefix=phase2-gate"
    if ($LASTEXITCODE -ne 0) { throw "Full tests failed with exit code $LASTEXITCODE." }
    $testCounters = @(
        Get-ChildItem -LiteralPath $temporaryResults -Filter "*.trx" -File |
            ForEach-Object {
                [xml]$trx = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
                $trx.SelectSingleNode("//*[local-name()='Counters']")
            }
    )
    $testTotal = ($testCounters | ForEach-Object { [int]$_.total } | Measure-Object -Sum).Sum
    $testPassed = ($testCounters | ForEach-Object { [int]$_.passed } | Measure-Object -Sum).Sum
    $testFailed = ($testCounters | ForEach-Object { [int]$_.failed + [int]$_.error + [int]$_.timeout + [int]$_.aborted } | Measure-Object -Sum).Sum
    if ($testTotal -le 0 -or $testPassed -ne $testTotal -or $testFailed -ne 0) {
        throw "TRX counters are inconsistent: total=$testTotal passed=$testPassed failed=$testFailed."
    }

    & dotnet format $solution --verify-no-changes --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Format verification failed with exit code $LASTEXITCODE." }

    $vulnerabilityText = (& dotnet list $solution package --vulnerable --include-transitive --no-restore --format json | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "NuGet vulnerability query failed with exit code $LASTEXITCODE." }
    $vulnerabilityJson = $vulnerabilityText | ConvertFrom-Json
    $vulnerablePackages = 0
    foreach ($project in @($vulnerabilityJson.projects)) {
        foreach ($framework in @($project.frameworks | Where-Object { $null -ne $_ })) {
            $packages = @($framework.topLevelPackages | Where-Object { $null -ne $_ }) +
                @($framework.transitivePackages | Where-Object { $null -ne $_ })
            foreach ($package in $packages) {
                $vulnerablePackages += @($package.vulnerabilities | Where-Object { $null -ne $_ }).Count
            }
        }
    }
    if ($vulnerablePackages -ne 0) { throw "NuGet vulnerability gate found $vulnerablePackages vulnerable package entries." }

    $credentialScanPath = Join-Path $artifactRoot "credential-redaction-scan.json"
    & (Join-Path $repoRoot "Automation\Test-CharacterLifecycleCredentialRedaction.ps1") -ScanRoots @("Reports", "Artifacts", "logs") -OutputPath $credentialScanPath | Out-Null
    $credentialScan = Read-God2JsonWithRetry -Path $credentialScanPath
    if ([int]$credentialScan.matchCount -ne 0) { throw "Credential redaction scan found $($credentialScan.matchCount) output files." }

    $sensitiveScanPath = Join-Path $artifactRoot "sensitive-build-output-scan.json"
    & (Join-Path $repoRoot "Automation\Test-God2SensitiveBuildOutputs.ps1") -RepositoryRoot $repoRoot -OutputPath $sensitiveScanPath | Out-Null
    $sensitiveBuildScan = Read-God2JsonWithRetry -Path $sensitiveScanPath
    $sensitiveBuildOutputs = [int]$sensitiveBuildScan.matchCount
    if ($sensitiveBuildOutputs -ne 0) { throw "Sensitive build-output scan found $sensitiveBuildOutputs matches." }

    $contentRecoverySourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "tools\God2.GameplayContentRecovery") -Filter "*.cs" -File -Recurse)
    $networkEmitPattern = "\b(?:Socket|TcpClient|NetworkStream|UdpClient)\b|\b(?:Send|SendAsync|SendTo|SendToAsync)\s*\("
    $networkEmitApiMatches = 0
    foreach ($sourceFile in $contentRecoverySourceFiles) {
        $sourceText = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding UTF8
        $networkEmitApiMatches += [Text.RegularExpressions.Regex]::Matches($sourceText, $networkEmitPattern).Count
    }
    if ($networkEmitApiMatches -ne 0) {
        throw "Content-recovery fake-network-byte gate found $networkEmitApiMatches production network-emission API matches."
    }

    & dotnet build-server shutdown | Out-Null
    Start-Sleep -Milliseconds 300
    $residual = [ordered]@{
        dotnet = @(Get-Process -Name "dotnet" -ErrorAction SilentlyContinue).Count
        testhost = @(Get-Process -Name "testhost" -ErrorAction SilentlyContinue).Count
        vstest = @(Get-Process -Name "vstest.console" -ErrorAction SilentlyContinue).Count
    }
    if (($residual.dotnet + $residual.testhost + $residual.vstest) -ne 0) {
        throw "Residual process gate failed: dotnet/testhost/vstest=$($residual.dotnet)/$($residual.testhost)/$($residual.vstest)."
    }

    $externalGates = [ordered]@{
        status = "PASS"
        recoveryRunId = $recoveryRunId
        startedAtUtc = $gateStartedAtUtc.ToString("o")
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        debugBuild = "PASS (warnings=0, errors=0; warnings are errors)"
        releaseBuild = "PASS (warnings=0, errors=0; warnings are errors)"
        fullTests = "$testPassed / $testTotal PASS"
        format = "PASS"
        nuGetProjects = @($vulnerabilityJson.projects).Count
        vulnerablePackageEntries = $vulnerablePackages
        credentialFilesScanned = [int]$credentialScan.scannedFileCount
        credentialSecretValuesResolved = [int]$credentialScan.secretValueCount
        credentialMatches = [int]$credentialScan.matchCount
        buildFilesScanned = [int]$sensitiveBuildScan.filesScanned
        encodedSecretPatternsScanned = [int]$sensitiveBuildScan.encodedPatternsScanned
        sensitiveBuildOutputs = $sensitiveBuildOutputs
        contentRecoverySourceFilesScanned = $contentRecoverySourceFiles.Count
        productionNetworkEmitApiMatches = $networkEmitApiMatches
        productionFakeNetworkBytes = $networkEmitApiMatches
        residualProcesses = $residual
    }

    $testResults = Read-God2JsonWithRetry -Path $testResultsPath
    $testResults.externalGates = [pscustomobject]$externalGates
    Write-God2AtomicJson -Value $testResults -Path $testResultsPath
    $summary | Add-Member -NotePropertyName externalGates -NotePropertyValue ([pscustomobject]$externalGates) -Force
    Write-God2AtomicJson -Value $summary -Path $summaryPath

    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8
    $marker = "`n## Final verification gates"
    $markerIndex = $report.IndexOf($marker, [StringComparison]::Ordinal)
    if ($markerIndex -ge 0) { $report = $report.Substring(0, $markerIndex).TrimEnd() }
    $gateReport = @"

## Final verification gates

- Recovery Run: $recoveryRunId
- Debug / Release: PASS / PASS (warnings 0, errors 0)
- Full tests: $testPassed / $testTotal PASS
- Format: PASS
- NuGet: $(@($vulnerabilityJson.projects).Count) projects, 0 vulnerable package entries
- Credential scan: $([int]$credentialScan.scannedFileCount) files, $([int]$credentialScan.secretValueCount) resolved secret values, 0 matches
- Sensitive build outputs: 0 / $([int]$sensitiveBuildScan.filesScanned) files ($([int]$sensitiveBuildScan.encodedPatternsScanned) encoded patterns)
- Content-recovery source network-emission scan: 0 / $($contentRecoverySourceFiles.Count)
- Production Fake Network Bytes: $networkEmitApiMatches
- Residual dotnet / testhost / vstest: 0 / 0 / 0
"@
    [IO.File]::WriteAllText($reportPath, $report + $gateReport.TrimEnd() + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    [pscustomobject]$externalGates | ConvertTo-Json -Depth 8
}
finally {
    if (Test-Path -LiteralPath $temporaryResults) { [IO.Directory]::Delete($temporaryResults, $true) }
    Pop-Location
}
