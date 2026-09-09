param(
    [string] $TaskName = "God2Classic.AutomationHost"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")

$repoRoot = Get-God2RepoRoot
$spec = Get-God2AutomationTaskSpec -TaskName $TaskName
$existing = Get-God2AutomationTaskValidation -TaskName $TaskName
if ($existing.exists -and $existing.isCorrect) {
    [pscustomobject]@{
        taskName = $TaskName
        exitCode = 0
        status = "AlreadyRegistered"
        runLevel = $existing.runLevel
        interactiveOnly = $true
        workingDirectory = $existing.workingDirectory
        action = "$($spec.command) $($spec.arguments)"
        diagnosticZh = "Scheduled task already exists and is correct; no rebuild was performed."
    } | ConvertTo-Json -Depth 6
    exit 0
}

$action = New-ScheduledTaskAction `
    -Execute $spec.command `
    -Argument $spec.arguments `
    -WorkingDirectory $repoRoot
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5)
$principal = New-ScheduledTaskPrincipal `
    -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) `
    -LogonType Interactive `
    -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Hours 3) `
    -MultipleInstances IgnoreNew

try {
    Register-ScheduledTask `
        -TaskName $TaskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Force | Out-Null

    [pscustomobject]@{
        taskName = $TaskName
        exitCode = 0
        status = if ($existing.exists) { "Repaired" } else { "Registered" }
        runLevel = $spec.runLevel
        interactiveOnly = $true
        workingDirectory = $repoRoot
        action = "$($spec.command) $($spec.arguments)"
        diagnosticZh = if ($existing.exists) { "Scheduled task configuration was repaired." } else { "Scheduled task was registered." }
    } | ConvertTo-Json -Depth 6
}
catch {
    [pscustomobject]@{
        taskName = $TaskName
        exitCode = 1
        error = $_.Exception.Message
        status = "RegistrationFailed"
        requiresElevation = ($_.Exception.Message -match "Access is denied")
        runLevel = $spec.runLevel
        interactiveOnly = $true
        workingDirectory = $repoRoot
        action = "$($spec.command) $($spec.arguments)"
        diagnosticZh = if ($_.Exception.Message -match "Access is denied") {
            "Current PowerShell token cannot create or repair a Highest RunLevel scheduled task."
        }
        else {
            "Scheduled task registration failed; inspect Task Scheduler permissions and task configuration."
        }
    } | ConvertTo-Json -Depth 6
    exit 1
}
