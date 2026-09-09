Set-StrictMode -Version 2.0

$script:God2EvidenceAuthorityProfiles = @(
    'SyntheticContractFixture',
    'ExactSelfTestClient',
    'ExactBinaryStaticAnalysis',
    'HistoricalOfficialClientLive',
    'CurrentOfficialClientLive',
    'OfflineReanalysis',
    'PortableExternalHardwareRun'
)

$script:God2OfficialExecutable = 'God2_opt.exe'
$script:God2OfficialVersion = '1.0.0.1'
$script:God2OfficialSha256 =
    '6B127086E0C00014DE26137B4EC482801E06E0724C5C05C64561D7F9FF32BD9B'

function Test-God2CanonicalSha256([object]$Value) {
    return $Value -is [string] -and
        [string]$Value -cmatch '^[0-9A-F]{64}$'
}

function Test-God2PositiveInteger([object]$Value) {
    if ($Value -isnot [byte] -and $Value -isnot [sbyte] -and
        $Value -isnot [int16] -and $Value -isnot [uint16] -and
        $Value -isnot [int32] -and $Value -isnot [uint32] -and
        $Value -isnot [int64] -and $Value -isnot [uint64]) {
        return $false
    }
    return [uint64]$Value -gt 0
}

function New-God2EvidenceAuthorityRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet(
            'SyntheticContractFixture',
            'ExactSelfTestClient',
            'ExactBinaryStaticAnalysis',
            'HistoricalOfficialClientLive',
            'CurrentOfficialClientLive',
            'OfflineReanalysis',
            'PortableExternalHardwareRun')]
        [string]$AuthorityProfile,
        [Parameter(Mandatory)][string]$SessionId,
        [Parameter(Mandatory)][string]$SourceArtifact,
        [Parameter(Mandatory)][string]$SourceSHA256,
        [AllowNull()][string]$TargetExecutable,
        [AllowNull()][string]$TargetVersion,
        [AllowNull()][string]$TargetSHA256,
        [AllowNull()][Nullable[uint32]]$TargetProcessId,
        [AllowNull()][string]$TargetProcessCreationTime,
        [Parameter(Mandatory)][bool]$CurrentSessionObserved,
        [Parameter(Mandatory)][bool]$HistoricalEvidenceReused,
        [Parameter(Mandatory)][bool]$FixtureOnly,
        [Parameter(Mandatory)][bool]$PromotionEligible,
        [AllowNull()][string]$AttachStatus,
        [AllowNull()][string]$DetachStatus,
        [AllowNull()][string]$PackageSHA256,
        [uint64]$RuntimeEventCount = 0
    )

    return [pscustomobject][ordered]@{
        AuthorityProfile = $AuthorityProfile
        SessionId = $SessionId
        SourceArtifact = $SourceArtifact.Replace('\', '/')
        SourceSHA256 = $SourceSHA256
        TargetExecutable = $TargetExecutable
        TargetVersion = $TargetVersion
        TargetSHA256 = $TargetSHA256
        TargetProcessId = if ($null -eq $TargetProcessId) { $null } else { [uint32]$TargetProcessId }
        TargetProcessCreationTime = $TargetProcessCreationTime
        CurrentSessionObserved = $CurrentSessionObserved
        HistoricalEvidenceReused = $HistoricalEvidenceReused
        FixtureOnly = $FixtureOnly
        PromotionEligible = $PromotionEligible
        AttachStatus = $AttachStatus
        DetachStatus = $DetachStatus
        PackageSHA256 = if ([string]::IsNullOrWhiteSpace($PackageSHA256)) { $null } else { $PackageSHA256 }
        RuntimeEventCount = $RuntimeEventCount
    }
}

function Test-God2EvidenceAuthorityConsistency {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Records)

    $issues = [Collections.Generic.List[string]]::new()
    $required = @(
        'AuthorityProfile','SessionId','SourceArtifact','SourceSHA256',
        'TargetExecutable','TargetVersion','TargetSHA256','TargetProcessId',
        'TargetProcessCreationTime','CurrentSessionObserved','HistoricalEvidenceReused',
        'FixtureOnly','PromotionEligible','AttachStatus','DetachStatus',
        'PackageSHA256','RuntimeEventCount'
    )

    for ($index = 0; $index -lt $Records.Count; ++$index) {
        $record = $Records[$index]
        $prefix = "records[$index]"
        $propertyNames = @($record.PSObject.Properties.Name)
        if ($propertyNames.Count -ne $required.Count -or
            @($required | Where-Object { $propertyNames -cnotcontains $_ }).Count -ne 0) {
            $issues.Add("$prefix.exact-properties")
            continue
        }

        $profile = [string]$record.AuthorityProfile
        if ($script:God2EvidenceAuthorityProfiles -cnotcontains $profile) {
            $issues.Add("$prefix.authority-profile")
        }
        if ([string]::IsNullOrWhiteSpace([string]$record.SessionId)) {
            $issues.Add("$prefix.session-id")
        }
        $sourceArtifact = [string]$record.SourceArtifact
        if ([string]::IsNullOrWhiteSpace($sourceArtifact) -or
            [IO.Path]::IsPathRooted($sourceArtifact) -or
            $sourceArtifact.Contains('\') -or $sourceArtifact -match '(^|/)\.\.(/|$)') {
            $issues.Add("$prefix.source-artifact")
        }
        if (-not (Test-God2CanonicalSha256 $record.SourceSHA256)) {
            $issues.Add("$prefix.source-sha256")
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$record.TargetSHA256) -and
            -not (Test-God2CanonicalSha256 $record.TargetSHA256)) {
            $issues.Add("$prefix.target-sha256")
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$record.PackageSHA256) -and
            -not (Test-God2CanonicalSha256 $record.PackageSHA256)) {
            $issues.Add("$prefix.package-sha256")
        }

        $current = $record.CurrentSessionObserved -is [bool] -and
            [bool]$record.CurrentSessionObserved
        $historical = $record.HistoricalEvidenceReused -is [bool] -and
            [bool]$record.HistoricalEvidenceReused
        $fixture = $record.FixtureOnly -is [bool] -and [bool]$record.FixtureOnly
        $promotable = $record.PromotionEligible -is [bool] -and
            [bool]$record.PromotionEligible

        foreach ($booleanName in @('CurrentSessionObserved','HistoricalEvidenceReused',
                'FixtureOnly','PromotionEligible')) {
            if ($record.$booleanName -isnot [bool]) {
                $issues.Add("$prefix.$($booleanName.ToLowerInvariant()).type")
            }
        }

        if ($fixture -and ($current -or $historical -or $promotable)) {
            $issues.Add("$prefix.fixture-authority-conflict")
        }
        if ($profile -in @('SyntheticContractFixture','ExactSelfTestClient') -and -not $fixture) {
            $issues.Add("$prefix.fixture-profile-mismatch")
        }
        if ($profile -ceq 'HistoricalOfficialClientLive' -and
            ($current -or -not $historical -or $fixture -or $promotable)) {
            $issues.Add("$prefix.historical-authority-conflict")
        }
        if ($profile -ceq 'CurrentOfficialClientLive') {
            $exactTarget = [string]$record.TargetExecutable -ceq $script:God2OfficialExecutable -and
                [string]$record.TargetVersion -ceq $script:God2OfficialVersion -and
                [string]$record.TargetSHA256 -ceq $script:God2OfficialSha256
            $completeRuntime = (Test-God2PositiveInteger $record.TargetProcessId) -and
                -not [string]::IsNullOrWhiteSpace([string]$record.TargetProcessCreationTime) -and
                [string]$record.AttachStatus -ceq 'ATTACHED' -and
                [string]$record.DetachStatus -ceq 'DETACHED' -and
                (Test-God2CanonicalSha256 $record.PackageSHA256) -and
                (Test-God2PositiveInteger $record.RuntimeEventCount)
            if (-not $current -or $historical -or $fixture -or
                -not $exactTarget -or -not $completeRuntime) {
                $issues.Add("$prefix.current-official-live-contract")
            }
        } elseif ($current) {
            $issues.Add("$prefix.current-session-profile-mismatch")
        }
        if ($promotable -and $profile -notin @(
                'CurrentOfficialClientLive','OfflineReanalysis','PortableExternalHardwareRun')) {
            $issues.Add("$prefix.promotion-profile-mismatch")
        }
    }

    return [pscustomobject]@{
        Passed = $issues.Count -eq 0
        Status = if ($issues.Count -eq 0) { 'PASS' } else { 'FAILED' }
        Issues = @($issues)
    }
}

function New-God2AuthorityConsistencyReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Records,
        [string]$GeneratedAtUtc = [DateTime]::UtcNow.ToString('o')
    )

    $validation = Test-God2EvidenceAuthorityConsistency -Records $Records
    return [ordered]@{
        SchemaVersion = 'god2-evidence-authority-consistency-v1'
        GeneratedAtUtc = $GeneratedAtUtc
        Status = $validation.Status
        ConflictCount = $validation.Issues.Count
        Conflicts = @($validation.Issues)
        Records = @($Records)
    }
}

function Test-God2EvidenceAuthorityContract {
    $fixture = New-God2EvidenceAuthorityRecord `
        -AuthorityProfile ExactSelfTestClient -SessionId 'authority-selftest-fixture' `
        -SourceArtifact 'fixture/product.json' -SourceSHA256 ('A' * 64) `
        -TargetExecutable 'God2TraceSelfTestClient.exe' -TargetVersion '1' `
        -TargetSHA256 ('B' * 64) -TargetProcessId 7 -TargetProcessCreationTime 'fixture' `
        -CurrentSessionObserved $false -HistoricalEvidenceReused $false `
        -FixtureOnly $true -PromotionEligible $false
    $historical = New-God2EvidenceAuthorityRecord `
        -AuthorityProfile HistoricalOfficialClientLive -SessionId 'authority-selftest-history' `
        -SourceArtifact 'history/official.json' -SourceSHA256 ('C' * 64) `
        -TargetExecutable $script:God2OfficialExecutable -TargetVersion $script:God2OfficialVersion `
        -TargetSHA256 $script:God2OfficialSha256 -TargetProcessId 8 `
        -TargetProcessCreationTime $null -CurrentSessionObserved $false `
        -HistoricalEvidenceReused $true -FixtureOnly $false -PromotionEligible $false `
        -AttachStatus ATTACHED -DetachStatus DETACHED -RuntimeEventCount 46
    $positive = Test-God2EvidenceAuthorityConsistency -Records @($fixture,$historical)

    $fixtureConflict = [pscustomobject]($fixture | ConvertTo-Json -Depth 3 | ConvertFrom-Json)
    $fixtureConflict.CurrentSessionObserved = $true
    $historicalConflict = [pscustomobject]($historical | ConvertTo-Json -Depth 3 | ConvertFrom-Json)
    $historicalConflict.CurrentSessionObserved = $true
    $currentIncomplete = [pscustomobject]($historical | ConvertTo-Json -Depth 3 | ConvertFrom-Json)
    $currentIncomplete.AuthorityProfile = 'CurrentOfficialClientLive'
    $currentIncomplete.HistoricalEvidenceReused = $false
    $currentIncomplete.CurrentSessionObserved = $true
    $fixtureNegative = Test-God2EvidenceAuthorityConsistency -Records @($fixtureConflict)
    $historicalNegative = Test-God2EvidenceAuthorityConsistency -Records @($historicalConflict)
    $currentNegative = Test-God2EvidenceAuthorityConsistency -Records @($currentIncomplete)

    $checks = [Collections.Generic.List[object]]::new()
    $checks.Add([pscustomobject]@{
        Name='positive.fixture-and-historical-separated'
        Passed=[bool]$positive.Passed
    })
    $checks.Add([pscustomobject]@{
        Name='negative.fixture-cannot-claim-current'
        Passed=(-not [bool]$fixtureNegative.Passed)
    })
    $checks.Add([pscustomobject]@{
        Name='negative.historical-cannot-claim-current'
        Passed=(-not [bool]$historicalNegative.Passed)
    })
    $checks.Add([pscustomobject]@{
        Name='negative.current-live-requires-creation-package'
        Passed=(-not [bool]$currentNegative.Passed)
    })
    return [pscustomobject]@{
        Passed = @($checks | Where-Object { -not $_.Passed }).Count -eq 0
        CheckCount = $checks.Count
        PassedCount = @($checks | Where-Object Passed).Count
        Checks = $checks
    }
}
