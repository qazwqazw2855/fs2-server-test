param(
    [string] $ClientRoot,
    [ValidateSet("Debug", "Release")] [string] $Configuration = "Release",
    [switch] $ScanOnly,
    [switch] $SkipExternalGates
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$ClientRoot = if ([string]::IsNullOrWhiteSpace($ClientRoot)) {
    Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) "XJZ2"
}
else {
    [IO.Path]::GetFullPath($ClientRoot)
}
$project = Join-Path $repoRoot "tools\God2.GameplayContentRecovery\God2.GameplayContentRecovery.csproj"
$secretStatus = Initialize-God2DatabasePasswordEnvironment
if (-not $secretStatus.hasSecret) {
    Write-Error "SECURE DATABASE SECRET PROVISIONING: BLOCKED - CREDENTIAL VALUE NOT AVAILABLE ($($secretStatus.failureCode))"
}

try {
    Write-Output "credentialSourceType=$($secretStatus.source)"
    Write-Output "clientRootIdentity=$([IO.Path]::GetFileName([IO.Path]::GetFullPath($ClientRoot)))"
    $toolArguments = @("--repository-root", $repoRoot, "--client-root", $ClientRoot, "--phase", "phase2")
    if ($ScanOnly) {
        $toolArguments += @("--scan-only", "true")
    }
    & dotnet run --project $project --configuration $Configuration -- @toolArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Gameplay content recovery Phase 2 exited with code $LASTEXITCODE."
    }
    if (-not $ScanOnly -and -not $SkipExternalGates) {
        & (Join-Path $repoRoot "Automation\Finalize-GameplayContentRecoveryPhase2Gates.ps1") -RepositoryRoot $repoRoot
    }
}
finally {
    $config = Get-God2DatabaseConfig
    if (-not [string]::IsNullOrWhiteSpace($config.passwordEnvironmentVariable)) {
        [Environment]::SetEnvironmentVariable([string]$config.passwordEnvironmentVariable, $null, "Process")
    }
}
