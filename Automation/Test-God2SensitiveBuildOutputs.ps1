param(
    [string] $RepositoryRoot = "",
    [string] $OutputPath = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "God2Automation.Common.ps1")
Add-Type -AssemblyName System.Security
if (-not ("God2SensitiveBuildOutputByteScanner" -as [type])) {
    Add-Type -TypeDefinition @"
public static class God2SensitiveBuildOutputByteScanner
{
    public static bool Contains(byte[] value, byte[] pattern)
    {
        if (value == null || pattern == null || pattern.Length == 0 || pattern.Length > value.Length)
            return false;
        for (var offset = 0; offset <= value.Length - pattern.Length; offset++)
        {
            var matched = true;
            for (var index = 0; index < pattern.Length; index++)
            {
                if (value[offset + index] == pattern[index])
                    continue;
                matched = false;
                break;
            }
            if (matched)
                return true;
        }
        return false;
    }
}
"@
}

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { Get-God2RepoRoot } else { [IO.Path]::GetFullPath($RepositoryRoot) }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "Artifacts\GameplayContentRecoveryPhase2\sensitive-build-output-scan.json"
}

$secretValues = New-Object System.Collections.Generic.List[string]
$plain = $null
try {
    $null = Initialize-God2DatabasePasswordEnvironment
    $config = Get-God2DatabaseConfig
    $databaseSecret = [Environment]::GetEnvironmentVariable([string]$config.passwordEnvironmentVariable, "Process")
    if (-not [string]::IsNullOrWhiteSpace($databaseSecret) -and $databaseSecret.Length -ge 4) {
        $secretValues.Add($databaseSecret)
    }

    $loginSecretPath = Join-Path $repoRoot "Automation\State\login-secret.bin"
    if (Test-Path -LiteralPath $loginSecretPath) {
        $protected = [IO.File]::ReadAllBytes($loginSecretPath)
        $plain = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected,
            $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        try {
            $loginSecret = [Text.Encoding]::UTF8.GetString($plain) | ConvertFrom-Json
            foreach ($value in @([string]$loginSecret.account, [string]$loginSecret.password)) {
                if (-not [string]::IsNullOrWhiteSpace($value) -and $value.Length -ge 4) {
                    $secretValues.Add($value)
                }
            }
        }
        finally {
            [Array]::Clear($plain, 0, $plain.Length)
            $plain = $null
            $loginSecret = $null
        }
    }

    $uniqueSecrets = @($secretValues | Select-Object -Unique)
    $secretByteSequences = @(
        for ($secretOrdinal = 0; $secretOrdinal -lt $uniqueSecrets.Count; $secretOrdinal++) {
            $secret = $uniqueSecrets[$secretOrdinal]
            [pscustomobject]@{ ordinal = $secretOrdinal + 1; encoding = "UTF-8"; bytes = [Text.Encoding]::UTF8.GetBytes($secret) }
            [pscustomobject]@{ ordinal = $secretOrdinal + 1; encoding = "UTF-16LE"; bytes = [Text.Encoding]::Unicode.GetBytes($secret) }
            [pscustomobject]@{ ordinal = $secretOrdinal + 1; encoding = "UTF-16BE"; bytes = [Text.Encoding]::BigEndianUnicode.GetBytes($secret) }
        }
    )
    $buildFiles = @(
        Get-ChildItem -LiteralPath $repoRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "[\\/](bin|obj)[\\/]" }
    )
    $findings = @()
    foreach ($file in $buildFiles) {
        try {
            $fileBytes = [IO.File]::ReadAllBytes($file.FullName)
            foreach ($pattern in $secretByteSequences) {
                if ([God2SensitiveBuildOutputByteScanner]::Contains($fileBytes, [byte[]]$pattern.bytes)) {
                    $findings += [pscustomobject]@{
                        path = $file.FullName.Substring($repoRoot.Length).TrimStart([char[]]@('\', '/'))
                        encoding = $pattern.encoding
                        secretOrdinal = $pattern.ordinal
                    }
                    break
                }
            }
        }
        catch {
            $relativeFile = $file.FullName.Substring($repoRoot.Length).TrimStart([char[]]@('\', '/'))
            throw "Sensitive build-output scan could not read '$relativeFile': $($_.Exception.Message)"
        }
    }

    $result = [ordered]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        status = if ($findings.Count -eq 0) { "PASS" } else { "FAILED" }
        filesScanned = $buildFiles.Count
        secretValuesResolved = $uniqueSecrets.Count
        encodedPatternsScanned = $secretByteSequences.Count
        matchCount = $findings.Count
        matches = @($findings)
    }
    Write-God2AtomicJson -Value $result -Path $OutputPath
    $result | ConvertTo-Json -Depth 8
    if ($findings.Count -ne 0) {
        throw "Sensitive build-output scan found $($findings.Count) matches."
    }
}
finally {
    if ($null -ne $plain) { [Array]::Clear($plain, 0, $plain.Length) }
    if ($null -ne $config -and -not [string]::IsNullOrWhiteSpace($config.passwordEnvironmentVariable)) {
        [Environment]::SetEnvironmentVariable([string]$config.passwordEnvironmentVariable, $null, "Process")
    }
    $databaseSecret = $null
    $secretValues = $null
    $uniqueSecrets = $null
    $secretByteSequences = $null
}
