param(
    [ValidateRange(300, 7200)]
    [int] $ObserveSeconds = 1800,
    [ValidateRange(60, 3600)]
    [int] $WaitForClientSeconds = 1200,
    [switch] $LaunchOfficialLauncher
)

$ErrorActionPreference = 'Stop'
$start = Join-Path $PSScriptRoot 'Start-UserOperatedOfficialCapture.ps1'
$arguments = @{
    CaptureKind = 'BattleCampaign'
    ObserveSeconds = $ObserveSeconds
    WaitForClientSeconds = $WaitForClientSeconds
}
if ($LaunchOfficialLauncher) { $arguments.LaunchOfficialLauncher = $true }

& $start @arguments
