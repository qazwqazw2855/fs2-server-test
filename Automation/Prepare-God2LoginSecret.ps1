param(
    [string] $Account = $env:GOD2_LOCAL_TEST_ACCOUNT,
    [int] $PasswordLength = 8,
    [string] $CharacterName = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Add-Type -AssemblyName System.Security

$repoRoot = Get-God2RepoRoot
$stateRoot = Join-Path $repoRoot "Automation\State"
New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($Account)) {
    throw "A local test account must be supplied through -Account or GOD2_LOCAL_TEST_ACCOUNT."
}
if ($PasswordLength -lt 8) {
    throw "PasswordLength must be at least 8."
}

$alphabet = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()
$overridePassword = $env:GOD2_LOCAL_TEST_PASSWORD_OVERRIDE
$password = if ([string]::IsNullOrWhiteSpace($overridePassword)) {
    -join (1..$PasswordLength | ForEach-Object { $alphabet | Get-Random })
} else {
    $overridePassword
}
$env:GOD2_LOCAL_TEST_PASSWORD = $password
try {
    $databaseSecretStatus = Initialize-God2DatabasePasswordEnvironment
    if (-not $databaseSecretStatus.hasSecret) {
        throw "GOD2_DB_PASSWORD is required before creating the login secret. $($databaseSecretStatus.diagnosticZh)"
    }

    $runDir = Join-Path $repoRoot ("Artifacts\ClientInstrumentation\LauncherAutomation\host-login-secret-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    dotnet run --project (Join-Path $repoRoot "tools\God2.ClientInstrumentation\Analyzer\God2.ClientInstrumentation.Analyzer.csproj") -c Release -- ensure-local-account --repo-root $repoRoot --run-dir $runDir --account $Account | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "ensure-local-account failed with exit code $LASTEXITCODE"
    }
    if (-not [string]::IsNullOrWhiteSpace($CharacterName)) {
        dotnet run --project (Join-Path $repoRoot "tools\God2.ClientInstrumentation\Analyzer\God2.ClientInstrumentation.Analyzer.csproj") -c Release -- ensure-local-character --repo-root $repoRoot --run-dir $runDir --account $Account --character-name $CharacterName | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "ensure-local-character failed with exit code $LASTEXITCODE"
        }
    }

    $plain = [Text.Encoding]::UTF8.GetBytes((@{
        schemaVersion = 1
        account = $Account
        password = $password
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    } | ConvertTo-Json -Compress))
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect($plain, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    }
    finally {
        [Array]::Clear($plain, 0, $plain.Length)
    }
    $secretPath = Join-Path $stateRoot "login-secret.bin"
    [IO.File]::WriteAllBytes($secretPath, $protected)

    Write-God2AtomicJson -Value ([pscustomobject]@{
        schemaVersion = 1
        account = "MASKED"
        accountSource = "ParameterOrEnvironment"
        secretPath = $secretPath
        password = "MASKED"
        runDir = $runDir
    }) -Path (Join-Path $stateRoot "login-secret-status.json")
    Get-Content -LiteralPath (Join-Path $stateRoot "login-secret-status.json") -Raw
}
finally {
    Remove-Item Env:\GOD2_LOCAL_TEST_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\GOD2_LOCAL_TEST_PASSWORD_OVERRIDE -ErrorAction SilentlyContinue
    $password = $null
}
