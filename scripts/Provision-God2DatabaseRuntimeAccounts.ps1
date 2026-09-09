$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
. (Join-Path $repoRoot "Automation\God2Automation.Common.ps1")
Add-Type -AssemblyName System.Security

$adminStatus = Initialize-God2DatabaseAdminPasswordEnvironment
if (-not $adminStatus.hasSecret) { throw $adminStatus.diagnosticZh }

function New-God2RandomPassword {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Write-God2ProtectedSecret {
    param([string] $Name, [string] $Password, [string] $Path)
    $payload = [ordered]@{
        schemaVersion = 1
        environmentVariableName = $Name
        password = $Password
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        protectedFor = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    }
    $plain = [Text.Encoding]::UTF8.GetBytes(($payload | ConvertTo-Json -Compress))
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect(
            $plain, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        [IO.File]::WriteAllBytes($Path, $protected)
    }
    finally {
        [Array]::Clear($plain, 0, $plain.Length)
        $payload = $null
    }
}

$runtimePassword = New-God2RandomPassword
$builderPassword = New-God2RandomPassword
try {
    $config = Get-God2DatabaseConfig
    $client = Get-ChildItem "C:\Program Files\MariaDB*\bin\mariadb.exe" |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($client)) { throw "MariaDB client was not found." }

    $runtimeSqlPassword = $runtimePassword.Replace("'", "''")
    $builderSqlPassword = $builderPassword.Replace("'", "''")
    $sql = @"
CREATE USER IF NOT EXISTS 'god2_server'@'localhost' IDENTIFIED BY '$runtimeSqlPassword';
ALTER USER 'god2_server'@'localhost' IDENTIFIED BY '$runtimeSqlPassword';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'god2_server'@'localhost';
GRANT god2_runtime_role TO 'god2_server'@'localhost';
SET DEFAULT ROLE god2_runtime_role FOR 'god2_server'@'localhost';
CREATE USER IF NOT EXISTS 'god2_server'@'127.0.0.1' IDENTIFIED BY '$runtimeSqlPassword';
ALTER USER 'god2_server'@'127.0.0.1' IDENTIFIED BY '$runtimeSqlPassword';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'god2_server'@'127.0.0.1';
GRANT god2_runtime_role TO 'god2_server'@'127.0.0.1';
SET DEFAULT ROLE god2_runtime_role FOR 'god2_server'@'127.0.0.1';
CREATE USER IF NOT EXISTS 'god2_catalog_builder'@'localhost' IDENTIFIED BY '$builderSqlPassword';
ALTER USER 'god2_catalog_builder'@'localhost' IDENTIFIED BY '$builderSqlPassword';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'god2_catalog_builder'@'localhost';
GRANT god2_catalog_builder_role TO 'god2_catalog_builder'@'localhost';
SET DEFAULT ROLE god2_catalog_builder_role FOR 'god2_catalog_builder'@'localhost';
GRANT INSERT ON god2_game_meta.admin_change_audit TO 'god2_catalog_builder'@'localhost';
GRANT SELECT, INSERT, UPDATE ON god2_game_meta.admin_field_locks TO 'god2_catalog_builder'@'localhost';
CREATE USER IF NOT EXISTS 'god2_catalog_builder'@'127.0.0.1' IDENTIFIED BY '$builderSqlPassword';
ALTER USER 'god2_catalog_builder'@'127.0.0.1' IDENTIFIED BY '$builderSqlPassword';
REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'god2_catalog_builder'@'127.0.0.1';
GRANT god2_catalog_builder_role TO 'god2_catalog_builder'@'127.0.0.1';
SET DEFAULT ROLE god2_catalog_builder_role FOR 'god2_catalog_builder'@'127.0.0.1';
GRANT INSERT ON god2_game_meta.admin_change_audit TO 'god2_catalog_builder'@'127.0.0.1';
GRANT SELECT, INSERT, UPDATE ON god2_game_meta.admin_field_locks TO 'god2_catalog_builder'@'127.0.0.1';
"@

    $env:MYSQL_PWD = $env:GOD2_DB_ADMIN_PASSWORD
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    # Send account DDL over stdin so generated passwords never appear in the
    # mariadb process command line.
    $sql | & $client --protocol=tcp -h $config.host -P $config.port -u root --silent 2>$null
    $databaseExit = $LASTEXITCODE
    $ErrorActionPreference = $oldPreference
    if ($databaseExit -ne 0) { throw "MariaDB runtime account provisioning failed." }

    # MariaDB executes triggers with the DEFINER account and does not activate
    # that account's default role. Restore only the direct SELECT/TRIGGER
    # grants needed to read OLD/NEW values for tables that currently have a
    # Catalog Builder-owned trigger; never grant broad schema write privileges
    # directly to the account.
    $triggerGrantQuery = @'
SELECT CONCAT(
    'GRANT SELECT, TRIGGER ON `',REPLACE(`TRIGGER_SCHEMA`,'`','``'),'`.`',
    REPLACE(`EVENT_OBJECT_TABLE`,'`','``'),'` TO ''',
    SUBSTRING_INDEX(`DEFINER`,'@',1),'''@''',SUBSTRING_INDEX(`DEFINER`,'@',-1),''';')
FROM `information_schema`.`TRIGGERS`
WHERE `DEFINER` IN ('god2_catalog_builder@localhost','god2_catalog_builder@127.0.0.1')
GROUP BY `TRIGGER_SCHEMA`,`EVENT_OBJECT_TABLE`,`DEFINER`
ORDER BY `TRIGGER_SCHEMA`,`EVENT_OBJECT_TABLE`,`DEFINER`;
'@
    $env:MYSQL_PWD = $env:GOD2_DB_ADMIN_PASSWORD
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $triggerGrantSql = @($triggerGrantQuery | & $client --protocol=tcp -h $config.host -P $config.port -u root --batch --skip-column-names --silent 2>$null)
        if ($LASTEXITCODE -ne 0) { throw "MariaDB trigger privilege discovery failed." }
        if ($triggerGrantSql.Count -gt 0) {
            ($triggerGrantSql -join "`n") | & $client --protocol=tcp -h $config.host -P $config.port -u root --silent 2>$null
            if ($LASTEXITCODE -ne 0) { throw "MariaDB trigger privilege provisioning failed." }
        }
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    Write-God2ProtectedSecret "GOD2_DB_PASSWORD" $runtimePassword (Get-God2DatabaseSecretPath)
    Write-God2ProtectedSecret "GOD2_DB_BUILDER_PASSWORD" $builderPassword (Get-God2DatabaseBuilderSecretPath)
    Write-Output "Runtime and Catalog Builder accounts created; passwords were written only to current-user DPAPI secrets."
}
finally {
    $runtimePassword = $null
    $builderPassword = $null
    $runtimeSqlPassword = $null
    $builderSqlPassword = $null
    $sql = $null
    $triggerGrantQuery = $null
    $triggerGrantSql = $null
    $env:MYSQL_PWD = $null
}
