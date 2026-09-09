param([switch] $SkipTests)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Get-Utf8Text([string] $Value) {
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Value))
}

function Assert-God2ReleaseChildPath([string] $Path, [string] $Root) {
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe release path: $resolvedPath"
    }
}

function Remove-God2RepositoryRootObjectFiles {
    Get-ChildItem -LiteralPath $repoRoot -Filter "*.obj" -File |
        ForEach-Object {
            [System.IO.File]::Delete($_.FullName)
        }
}

$solution = Join-Path $repoRoot "God2ClassicServer.sln"
$project = Join-Path $repoRoot "src\God2.ClassicServer.ConsoleHost\God2.ClassicServer.ConsoleHost.csproj"
$releaseRoot = Join-Path $repoRoot "Release\God2ClassicServer"
$target = Join-Path $releaseRoot "current"
$staging = Join-Path $releaseRoot (".staging-{0}" -f [Guid]::NewGuid().ToString("N"))
$backup = Join-Path $releaseRoot (".previous-{0}" -f [Guid]::NewGuid().ToString("N"))
$testRunToken = "publish-{0}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$testResultsDirectory = Join-Path $repoRoot ("Artifacts\FormalServerLogs\TestResults\{0}" -f $testRunToken)
$testSummary = $null
Assert-God2ReleaseChildPath $target $releaseRoot
Assert-God2ReleaseChildPath $staging $releaseRoot
Assert-God2ReleaseChildPath $backup $releaseRoot

$runningServer = Get-Process -Name "God2 Classic Server" -ErrorAction SilentlyContinue
if ($runningServer) {
    throw (Get-Utf8Text "5YG15ris5Yiw5q2j5byP5pyN5YuZ56uv5LuN5Zyo5Z+36KGM77yM6KuL5YWI6Zec6ZaJ5b6M5YaN55m85L2I44CC")
}

try {
    Write-Host (Get-Utf8Text "5bu6572u5q2j5byP5pyN5YuZ56uvLi4u") -ForegroundColor Cyan
    & dotnet build $solution -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }
    Remove-God2RepositoryRootObjectFiles

    if (-not $SkipTests) {
        Write-Host (Get-Utf8Text "5Z+36KGM5a6M5pW05ris6KmmLi4u") -ForegroundColor Cyan
        New-Item -ItemType Directory -Path $testResultsDirectory -Force | Out-Null
        & dotnet test $solution -c Release --no-build --nologo `
            --results-directory $testResultsDirectory `
            --logger ("trx;LogFilePrefix={0}" -f $testRunToken)
        if ($LASTEXITCODE -ne 0) { throw "Release tests failed: $LASTEXITCODE" }

        $testEvidence = @(
            Get-ChildItem -LiteralPath $testResultsDirectory -Filter "*.trx" -File |
                Sort-Object Name |
                ForEach-Object {
                    [xml]$trx = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
                    $counters = $trx.SelectSingleNode("//*[local-name()='Counters']")
                    if ($null -eq $counters) {
                        throw "Release TRX has no Counters node: $($_.FullName)"
                    }
                    [ordered]@{
                        path = $_.FullName.Substring($repoRoot.Length + 1)
                        length = $_.Length
                        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
                        total = [int]$counters.total
                        passed = [int]$counters.passed
                        failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
                        skipped = [int]$counters.notExecuted + [int]$counters.inconclusive
                    }
                }
        )
        if ($testEvidence.Count -eq 0) {
            throw "Release tests completed without a retained TRX result."
        }
        $testSummary = [ordered]@{
            total = [int](($testEvidence | ForEach-Object { $_.total } | Measure-Object -Sum).Sum)
            passed = [int](($testEvidence | ForEach-Object { $_.passed } | Measure-Object -Sum).Sum)
            failed = [int](($testEvidence | ForEach-Object { $_.failed } | Measure-Object -Sum).Sum)
            skipped = [int](($testEvidence | ForEach-Object { $_.skipped } | Measure-Object -Sum).Sum)
            trxFileCount = $testEvidence.Count
            evidence = $testEvidence
        }
        if ($testSummary.total -le 0 -or $testSummary.failed -ne 0 -or
            ($testSummary.passed + $testSummary.skipped) -ne $testSummary.total) {
            throw "Release TRX counters are inconsistent: total=$($testSummary.total);passed=$($testSummary.passed);failed=$($testSummary.failed);skipped=$($testSummary.skipped)."
        }
    }

    Write-Host (Get-Utf8Text "55Si55Sf5q2j5byP55m85L2I5qqULi4u") -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    & dotnet publish $project -c Release --no-build --nologo -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Release publish failed: $LASTEXITCODE" }

    $entryPoint = "God2 Classic Server.exe"
    if (-not (Test-Path -LiteralPath (Join-Path $staging $entryPoint) -PathType Leaf)) {
        throw "Published server entry point is missing."
    }

    $buildId = "{0}-{1}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ"), [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $files = @(Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($staging.Length + 1)
            length = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    $manifest = [ordered]@{
        schemaVersion = 1
        buildId = $buildId
        configuration = "Release"
        targetFramework = "net10.0"
        entryPoint = $entryPoint
        publishedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        testsExecuted = (-not $SkipTests)
        testSummary = $testSummary
        files = $files
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $staging "release-manifest.json") -Encoding UTF8

    . (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
    $verification = Test-God2ServerReleaseManifest -ReleaseDirectory $staging
    if (-not $verification.succeeded) {
        throw "Staged release verification failed: $($verification.diagnostic)"
    }

    $previousMoved = $false
    if (Test-Path -LiteralPath $target) {
        Move-Item -LiteralPath $target -Destination $backup
        $previousMoved = $true
    }
    try {
        Move-Item -LiteralPath $staging -Destination $target
    }
    catch {
        if ($previousMoved) {
            if (Test-Path -LiteralPath $target) {
                Remove-Item -LiteralPath $target -Recurse -Force
            }
            if (Test-Path -LiteralPath $backup) {
                Move-Item -LiteralPath $backup -Destination $target
            }
        }
        throw
    }
    if ($previousMoved -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
    Write-Host ((Get-Utf8Text "55m85L2I5a6M5oiQ") + ": " + $buildId) -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Assert-God2ReleaseChildPath $staging $releaseRoot
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $target)) {
        Assert-God2ReleaseChildPath $backup $releaseRoot
        Move-Item -LiteralPath $backup -Destination $target
    }
}
