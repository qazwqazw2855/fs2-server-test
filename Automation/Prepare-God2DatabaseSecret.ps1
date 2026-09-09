param(
    [string] $EnvironmentVariableName = "GOD2_DB_PASSWORD",
    [switch] $UseCurrentProcessEnvironment,
    [switch] $UseUserEnvironment,
    [switch] $UseMachineEnvironment
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Add-Type -AssemblyName System.Security

$repoRoot = Get-God2RepoRoot
$stateRoot = Get-God2AutomationStateRoot
New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

function Get-PlainTextFromSecureString {
    param([Security.SecureString] $Value)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

$password = $null
$source = $null
try {
    if ($UseCurrentProcessEnvironment) {
        $candidate = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, "Process")
        if (-not [string]::IsNullOrEmpty($candidate)) {
            $password = $candidate
            $source = "ProcessEnvironment"
        }
    }

    if (-not $password -and $UseUserEnvironment) {
        $candidate = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, "User")
        if (-not [string]::IsNullOrEmpty($candidate)) {
            $password = $candidate
            $source = "UserEnvironment"
        }
    }

    if (-not $password -and $UseMachineEnvironment) {
        $candidate = [Environment]::GetEnvironmentVariable($EnvironmentVariableName, "Machine")
        if (-not [string]::IsNullOrEmpty($candidate)) {
            $password = $candidate
            $source = "MachineEnvironment"
        }
    }

    if (-not $password) {
        $secure = Read-Host -Prompt "MariaDB password for $EnvironmentVariableName" -AsSecureString
        $password = Get-PlainTextFromSecureString -Value $secure
        $source = "SecurePrompt"
    }

    if ([string]::IsNullOrEmpty($password)) {
        throw "Database password cannot be empty."
    }

    $payload = [ordered]@{
        schemaVersion = 1
        environmentVariableName = $EnvironmentVariableName
        password = $password
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        protectedFor = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    }
    $plain = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress))
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        $plain,
        $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [Array]::Clear($plain, 0, $plain.Length)

    $secretPath = Get-God2DatabaseSecretPath
    [IO.File]::WriteAllBytes($secretPath, $protected)

    $statusPath = Get-God2DatabaseSecretStatusPath
    Write-God2AtomicJson -Value ([pscustomobject]@{
        schemaVersion = 1
        environmentVariableName = $EnvironmentVariableName
        secretPath = "Automation\State\db-secret.bin"
        source = $source
        secretValue = "MASKED"
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        diagnosticZh = "資料庫密碼已建立為目前 Windows 使用者可解密的 DPAPI Secret；未輸出明文。"
    }) -Path $statusPath

    Get-Content -LiteralPath $statusPath -Raw
}
finally {
    $password = $null
    $candidate = $null
    $payload = $null
}


